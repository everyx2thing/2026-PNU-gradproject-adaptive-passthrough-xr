"""
라벨링 규칙 (3.4.3절) + 7차원 Feature 벡터 추출 (3.4.1절)

입력: data/mock_sessions.csv, data/mock_events.csv (generate_mock_logs.py 결과물)
출력: data/mock_features.csv (윈도우 단위 feature 벡터 + 집계 라벨)

## 라벨링 규칙 (label_event 참고, 2026-09-21 duration+peak_risk 결합으로 변경)
- Positive (필요했던 활성화): 지속시간 3초 이상, 또는 짧아도 활성화 중 최고
  위험도(peak_risk)가 아주 높았던 경우(사람이 순간적으로 아주 가까이 왔던 경우 등)
- Negative (불필요한 활성화로 추정): 지속시간 2초 미만 + peak_risk도 임계값을
  살짝 넘긴 수준(오탐으로 추정)
- Neutral (경계 케이스): 나머지 애매한 구간은 완충 처리
  -> duration 임계값과 peak_risk 임계값 모두 heuristic이라 나중에 실 데이터
     보면서 재조정 필요 (TODO)

## Feature 벡터 (7차원)
x = [f_pt, r_cancel, t_pt_bar, v_h_bar, v_h_max, A_space_norm, T_session_norm]
- f_pt      : 윈도우 내 활성화 빈도 = N_activate / W
- r_cancel  : 수동 해제 비율 = N_cancel / (N_activate + eps)
- t_pt_bar  : 윈도우 내 평균 지속시간
- v_h_bar   : 윈도우 내 평균 이동속도
- v_h_max   : 윈도우 내 최대 이동속도
- A_space_norm   : 공간 크기 정규화 (세션 전체 범위 기준 0~1)
- T_session_norm : 누적 세션 시간 정규화 (세션 전체 범위 기준 0~1)

## 슬라이딩 윈도우 하이퍼파라미터
- WINDOW_SIZE : 윈도우 하나에 포함될 이벤트 개수 (초기값, TODO: 실험으로 튜닝 필요)
- STRIDE      : 윈도우 이동 간격 (이벤트 개수 기준)
지금은 "이벤트 개수" 기준 윈도우를 쓰지만, 실제 배포판(3.4.4절)은 "5초 시간 윈도우"
기준이라 나중에 시간 기반으로 바꿀 가능성 있음 (TODO)
"""

import argparse
import csv
import os
from statistics import mean

from config import (
    CANCEL_NEGATIVE_THRESHOLD, POSITIVE_THRESHOLD,
    DEFAULT_WINDOW_SIZE, DEFAULT_STRIDE,
    REAL_CANCEL_NEGATIVE_THRESHOLD, REAL_POSITIVE_THRESHOLD,
    REAL_WINDOW_SIZE, REAL_STRIDE,
    PEAK_RISK_BORDERLINE_THRESHOLD, PEAK_RISK_HIGH_THRESHOLD,
)

EPS = 1e-6
WINDOW_SIZE = DEFAULT_WINDOW_SIZE  # config.py에서 가져옴 (하이퍼파라미터 실험은 hyperparam_tuning.py에서)
STRIDE = DEFAULT_STRIDE

DATA_DIR = os.path.join(os.path.dirname(__file__), "..", "data")


def read_csv(path):
    with open(path, "r", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def label_event(event):
    """이벤트 하나에 대해 Positive / Negative / Neutral 라벨 부여

    ## 설계 변경 (2026-07-28, 실제 Quest 로그 검증 후)
    원래 규칙은 "수동 해제(is_manual_cancel) + 짧은 지속시간 = Negative" 였으나,
    지금 앱에는 Passthrough를 사용자가 직접 끄는 기능 자체가 없어서
    is_manual_cancel이 항상 0으로 고정됨 -> 원래 규칙으로는 Negative가
    데이터를 아무리 모아도 절대 나올 수 없음 (데이터 부족이 아니라 설계 문제).

    그래서 수동 해제 여부와 무관하게, "지속시간이 아주 짧게 저절로 꺼짐"을
    노이즈성 반짝임(불필요했을 가능성 높음)으로 근사해서 Negative로 판단하도록
    바꿈. is_manual_cancel 필드/컬럼 자체는 앞으로 실제 수동 제어 기능이
    생기면 재도입할 수 있도록 이벤트 데이터에는 그대로 남겨둠 (TODO).

    ## 설계 변경 (2026-09-21, peak_risk 결합)
    duration만으로는 "짧게 켜졌지만 실제로 위험했던 경우"(예: 사람이 순간적으로
    아주 가까이 왔다가 바로 멀어짐)와 "그냥 임계값을 살짝 넘겨서 오탐으로 켜진
    경우"를 구분할 수 없음. RiskSnapshot에 이미 있는 static/dynamic 위험도의
    "활성화 중 최고값"(peak_risk, 0~1)을 duration과 같이 봐서 이 둘을 구분함.
    - duration이 충분히 길면(POSITIVE_THRESHOLD 이상) peak_risk와 무관하게
      Positive로 본다 (지속된 활성화는 대체로 실제 필요했다고 가정).
    - 짧아도 peak_risk가 아주 높았다면(PEAK_RISK_HIGH_THRESHOLD 이상) Positive.
    - 짧고 peak_risk도 임계값을 살짝 넘긴 수준(PEAK_RISK_BORDERLINE_THRESHOLD
      미만)이면 오탐으로 추정해 Negative.
    - 나머지(짧지만 peak_risk가 애매하게 높은 경우 등)는 Neutral로 완충 처리.
    peak_risk가 없는 이벤트(과거 데이터, 필드 누락)는 0.0으로 취급해 기존
    duration-only 동작과 최대한 비슷하게 degrade됨.
    """
    duration = float(event["duration_sec"])
    peak_risk = float(event.get("peak_risk", 0.0) or 0.0)

    if duration >= POSITIVE_THRESHOLD:
        return "Positive"
    if peak_risk >= PEAK_RISK_HIGH_THRESHOLD:
        return "Positive"
    if duration < CANCEL_NEGATIVE_THRESHOLD and peak_risk < PEAK_RISK_BORDERLINE_THRESHOLD:
        return "Negative"
    # 나머지(경계 duration, 또는 짧지만 peak_risk가 애매한 경우)는 Neutral로
    # 완충 처리 (TODO: 재검토)
    return "Neutral"


def normalize(value, min_val, max_val):
    if max_val - min_val < EPS:
        return 0.5  # 전부 같은 값이면 중간값 처리
    return (value - min_val) / (max_val - min_val)


def build_feature_windows(events_by_session, sessions_by_id, space_range, tsession_range,
                           window_size=WINDOW_SIZE, stride=STRIDE):
    """세션별로 슬라이딩 윈도우를 돌며 7차원 feature 벡터 생성

    window_size, stride를 인자로 받게 해서 hyperparam_tuning.py에서
    여러 값을 실험할 수 있게 함 (기존 동작은 기본값 그대로라 변화 없음)
    """
    rows = []

    for session_id, events in events_by_session.items():
        session = sessions_by_id[session_id]
        a_space_norm = normalize(float(session["A_space"]), *space_range)
        t_session_norm = normalize(float(session["T_session"]), *tsession_range)

        n = len(events)
        start = 0
        while start < n:
            window = events[start:start + window_size]
            if len(window) < window_size:
                break  # 윈도우 크기 못 채우면 종료 (TODO: 마지막 자투리 윈도우 처리 방식 재검토)

            n_activate = len(window)
            n_cancel = sum(1 for e in window if e["is_manual_cancel"] == "1")
            durations = [float(e["duration_sec"]) for e in window]
            speeds = [float(e["head_speed_mps"]) for e in window]
            labels = [label_event(e) for e in window]

            f_pt = n_activate / window_size
            r_cancel = n_cancel / (n_activate + EPS)
            t_pt_bar = mean(durations)
            v_h_bar = mean(speeds)
            v_h_max = max(speeds)

            # 윈도우 대표 라벨: Positive 비율이 높으면 Positive, Negative 비율 높으면 Negative,
            # 아니면 Neutral -> 이후 모델 학습 시 참고 지표 (TODO: 더 정교한 집계 방식 검토)
            positive_ratio = labels.count("Positive") / len(labels)
            negative_ratio = labels.count("Negative") / len(labels)
            if positive_ratio >= negative_ratio and positive_ratio >= 0.4:
                window_label = "Positive"
            elif negative_ratio > positive_ratio and negative_ratio >= 0.4:
                window_label = "Negative"
            else:
                window_label = "Neutral"

            rows.append({
                "session_id": session_id,
                "window_start_event": window[0]["event_id"],
                "f_pt": round(f_pt, 4),
                "r_cancel": round(r_cancel, 4),
                "t_pt_bar": round(t_pt_bar, 4),
                "v_h_bar": round(v_h_bar, 4),
                "v_h_max": round(v_h_max, 4),
                "A_space_norm": round(a_space_norm, 4),
                "T_session_norm": round(t_session_norm, 4),
                "positive_ratio": round(positive_ratio, 4),
                "negative_ratio": round(negative_ratio, 4),
                "window_label": window_label,
            })

            start += stride

    return rows


def load_sessions_and_events(prefix="mock"):
    """{prefix}_sessions.csv, {prefix}_events.csv를 읽어서 build_feature_windows에 필요한
    형태로 가공. hyperparam_tuning.py에서도 재사용하기 위해 분리함.

    prefix="mock"  -> generate_mock_logs.py 결과물 (기본값, 기존 동작 그대로)
    prefix="real"  -> fetch_real_logs.py 결과물 (실제 로그 연동 후 사용)
    """
    sessions = read_csv(os.path.join(DATA_DIR, f"{prefix}_sessions.csv"))
    events = read_csv(os.path.join(DATA_DIR, f"{prefix}_events.csv"))

    sessions_by_id = {s["session_id"]: s for s in sessions}

    events_by_session = {}
    for e in events:
        events_by_session.setdefault(e["session_id"], []).append(e)
    # 이벤트 순서 보장 (event_id 기준 정렬)
    for sid in events_by_session:
        events_by_session[sid].sort(key=lambda e: int(e["event_id"]))

    a_space_values = [float(s["A_space"]) for s in sessions]
    t_session_values = [float(s["T_session"]) for s in sessions]
    space_range = (min(a_space_values), max(a_space_values))
    tsession_range = (min(t_session_values), max(t_session_values))

    return sessions_by_id, events_by_session, space_range, tsession_range


def main():
    global CANCEL_NEGATIVE_THRESHOLD, POSITIVE_THRESHOLD, WINDOW_SIZE, STRIDE

    parser = argparse.ArgumentParser()
    parser.add_argument("--source", choices=["mock", "real"], default="mock",
                         help="mock: generate_mock_logs.py 결과물 사용 (기본값) / "
                              "real: fetch_real_logs.py / parse_risk_snapshot_log.py 결과물 사용")
    args = parser.parse_args()

    if args.source == "real":
        CANCEL_NEGATIVE_THRESHOLD = REAL_CANCEL_NEGATIVE_THRESHOLD
        POSITIVE_THRESHOLD = REAL_POSITIVE_THRESHOLD
        WINDOW_SIZE = REAL_WINDOW_SIZE
        STRIDE = REAL_STRIDE

    sessions_by_id, events_by_session, space_range, tsession_range = load_sessions_and_events(args.source)

    rows = build_feature_windows(
        events_by_session, sessions_by_id, space_range, tsession_range,
        window_size=WINDOW_SIZE, stride=STRIDE,
    )

    output_path = os.path.join(DATA_DIR, f"{args.source}_features.csv")
    fieldnames = [
        "session_id", "window_start_event", "f_pt", "r_cancel", "t_pt_bar",
        "v_h_bar", "v_h_max", "A_space_norm", "T_session_norm",
        "positive_ratio", "negative_ratio", "window_label",
    ]
    with open(output_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)

    print(f"[{args.source}] 윈도우 {len(rows)}개 생성 완료 (WINDOW_SIZE={WINDOW_SIZE}, STRIDE={STRIDE}, "
          f"CANCEL_NEGATIVE_THRESHOLD={CANCEL_NEGATIVE_THRESHOLD}, POSITIVE_THRESHOLD={POSITIVE_THRESHOLD})")
    print(f"라벨 분포: Positive={sum(1 for r in rows if r['window_label']=='Positive')}, "
          f"Negative={sum(1 for r in rows if r['window_label']=='Negative')}, "
          f"Neutral={sum(1 for r in rows if r['window_label']=='Neutral')}")
    print(f"저장 위치: {output_path}")


if __name__ == "__main__":
    main()
