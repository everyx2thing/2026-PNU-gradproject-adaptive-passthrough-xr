"""
라벨링 규칙 (3.4.3절) + 7차원 Feature 벡터 추출 (3.4.1절)

입력: data/mock_sessions.csv, data/mock_events.csv (generate_mock_logs.py 결과물)
출력: data/mock_features.csv (윈도우 단위 feature 벡터 + 집계 라벨)

## 라벨링 규칙
- Negative (불필요한 활성화): 수동 해제 + 지속시간 2초 미만
- Positive (필요했던 활성화): 지속시간 3초 이상 + 수동 해제 없음
- Neutral (경계 케이스, 2~3초 구간): 애매한 구간이라 완충 처리
  -> 이 규칙은 heuristic이라 나중에 실 데이터 보면서 재조정 필요 (TODO)

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

import csv
import os
from statistics import mean

from config import (
    CANCEL_NEGATIVE_THRESHOLD, POSITIVE_THRESHOLD,
    DEFAULT_WINDOW_SIZE, DEFAULT_STRIDE,
)

EPS = 1e-6
WINDOW_SIZE = DEFAULT_WINDOW_SIZE  # config.py에서 가져옴 (하이퍼파라미터 실험은 hyperparam_tuning.py에서)
STRIDE = DEFAULT_STRIDE

DATA_DIR = os.path.join(os.path.dirname(__file__), "..", "data")


def read_csv(path):
    with open(path, "r", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def label_event(event):
    """이벤트 하나에 대해 Positive / Negative / Neutral 라벨 부여"""
    duration = float(event["duration_sec"])
    is_cancelled = event["is_manual_cancel"] == "1"

    if is_cancelled and duration < CANCEL_NEGATIVE_THRESHOLD:
        return "Negative"
    if (not is_cancelled) and duration >= POSITIVE_THRESHOLD:
        return "Positive"
    # 2~3초 구간 등 애매한 경계 케이스는 Neutral로 완충 처리 (TODO: 재검토)
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


def load_sessions_and_events():
    """mock_sessions.csv, mock_events.csv를 읽어서 build_feature_windows에 필요한
    형태로 가공. hyperparam_tuning.py에서도 재사용하기 위해 분리함.
    """
    sessions = read_csv(os.path.join(DATA_DIR, "mock_sessions.csv"))
    events = read_csv(os.path.join(DATA_DIR, "mock_events.csv"))

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
    sessions_by_id, events_by_session, space_range, tsession_range = load_sessions_and_events()

    rows = build_feature_windows(events_by_session, sessions_by_id, space_range, tsession_range)

    output_path = os.path.join(DATA_DIR, "mock_features.csv")
    fieldnames = [
        "session_id", "window_start_event", "f_pt", "r_cancel", "t_pt_bar",
        "v_h_bar", "v_h_max", "A_space_norm", "T_session_norm",
        "positive_ratio", "negative_ratio", "window_label",
    ]
    with open(output_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)

    print(f"윈도우 {len(rows)}개 생성 완료 (WINDOW_SIZE={WINDOW_SIZE}, STRIDE={STRIDE})")
    print(f"라벨 분포: Positive={sum(1 for r in rows if r['window_label']=='Positive')}, "
          f"Negative={sum(1 for r in rows if r['window_label']=='Negative')}, "
          f"Neutral={sum(1 for r in rows if r['window_label']=='Neutral')}")
    print(f"저장 위치: {output_path}")


if __name__ == "__main__":
    main()
