"""
실제 risk-snapshot-*.jsonl 로그 파서 (fetch_real_logs.py 대체/구체화)

Unity RiskSnapshotSessionLogger.cs가 찍는 실제 로그 형식을 읽어서
build_features.py가 기대하는 data/real_sessions.csv, data/real_events.csv로 변환한다.

실제 로그는 "이벤트"가 아니라 10Hz 연속 스냅샷 스트림이므로,
passthroughEnabled가 False -> True -> False로 바뀌는 구간을
하나의 "활성화 이벤트"로 직접 계산해서 만든다.

## 현재 한계 (2026-07-28 기준, 로그 실물 확인 결과)
- is_manual_cancel: 항상 0. Passthrough를 사용자가 직접 끄는 제어 자체가
  아직 미구현이라 "수동 해제"라는 사건이 로그에 없음. (TODO: 제어 구현되면 수정)
- controller_idle_sec: 항상 0. 컨트롤러 유휴시간 추적 로직이 프로젝트에
  아직 없음. (TODO: Unity 쪽에 새로 구현 필요)
- head_speed_mps: RiskSnapshotSessionLogger.cs에 headSpeedMps 필드를
  추가한 로그부터만 실제 값이 들어옴. 없으면 0으로 채움.
- A_space (공간 크기, m^2): 로그에 없는 값. --space-m2 인자로 직접 입력.
  (세션 찍을 때 방 크기를 팀원한테 물어봐서 넣을 것)

## 사용법
    python parse_risk_snapshot_log.py \
        --input risk-snapshot-20260728-141623.jsonl \
        --session-id s1 \
        --space-m2 4.0
"""

import argparse
import csv
import json
import os

DATA_DIR = os.path.join(os.path.dirname(__file__), "..", "data")

SESSION_FIELDS = ["session_id", "A_space", "T_session"]
EVENT_FIELDS = [
    "session_id", "event_id", "timestamp_sec", "duration_sec",
    "is_manual_cancel", "controller_idle_sec", "head_speed_mps",
]


def read_jsonl(path):
    """UTF-16(파워쉘 Out-File 기본 인코딩)과 UTF-8 둘 다 시도해서 읽는다."""
    for encoding in ("utf-8-sig", "utf-16"):
        try:
            with open(path, "r", encoding=encoding) as f:
                lines = f.readlines()
            # 정상적으로 JSON 파싱되는지 첫 줄로 확인
            json.loads(lines[0])
            return [json.loads(line) for line in lines if line.strip()]
        except (UnicodeError, json.JSONDecodeError, IndexError):
            continue
    raise ValueError(f"'{path}' 인코딩을 인식하지 못했습니다 (utf-8-sig, utf-16 둘 다 실패)")


def extract_activation_events(records):
    """passthroughEnabled False->True->False 구간을 이벤트로 변환.

    구간 안의 headSpeedMps는 평균을 낸다 (필드 없으면 0 처리).
    """
    events = []
    active_start = None
    active_speeds = []

    snapshots = [r for r in records if r.get("recordType") == "riskSnapshot"]
    snapshots.sort(key=lambda r: r["timestampSeconds"])

    for r in snapshots:
        enabled = r.get("passthroughEnabled", False)
        speed = r.get("headSpeedMps", 0.0)

        if enabled and active_start is None:
            active_start = r["timestampSeconds"]
            active_speeds = [speed]
        elif enabled and active_start is not None:
            active_speeds.append(speed)
        elif (not enabled) and active_start is not None:
            end = r["timestampSeconds"]
            events.append({
                "timestamp_sec": round(active_start, 3),
                "duration_sec": round(end - active_start, 3),
                "head_speed_mps": round(sum(active_speeds) / len(active_speeds), 4) if active_speeds else 0.0,
            })
            active_start = None
            active_speeds = []

    # 파일이 활성화된 채로 끝난 경우 마지막 이벤트 처리
    if active_start is not None and snapshots:
        end = snapshots[-1]["timestampSeconds"]
        events.append({
            "timestamp_sec": round(active_start, 3),
            "duration_sec": round(end - active_start, 3),
            "head_speed_mps": round(sum(active_speeds) / len(active_speeds), 4) if active_speeds else 0.0,
        })

    return events, snapshots


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--input", required=True, help="risk-snapshot-*.jsonl 파일 경로")
    parser.add_argument("--session-id", required=True, help="이 로그에 붙일 session_id (임의로 지정, 예: s1)")
    parser.add_argument("--space-m2", type=float, required=True,
                         help="이 세션을 찍은 방의 크기(m^2). 로그에 없는 값이라 직접 입력 필요")
    parser.add_argument("--append", action="store_true",
                         help="기존 real_sessions.csv/real_events.csv에 이어붙이기 (여러 로그 파일 합칠 때)")
    args = parser.parse_args()

    records = read_jsonl(args.input)
    events, snapshots = extract_activation_events(records)

    if not snapshots:
        raise SystemExit("riskSnapshot 레코드를 찾지 못했습니다. 파일이 올바른 로그인지 확인하세요.")

    t_session_min = round((snapshots[-1]["timestampSeconds"] - snapshots[0]["timestampSeconds"]) / 60.0, 4)

    session_row = {
        "session_id": args.session_id,
        "A_space": args.space_m2,
        "T_session": t_session_min,
    }
    event_rows = [
        {
            "session_id": args.session_id,
            "event_id": i,
            "timestamp_sec": e["timestamp_sec"],
            "duration_sec": e["duration_sec"],
            "is_manual_cancel": 0,          # 제어 미구현 -> 항상 0 (한계, 문서 상단 참고)
            "controller_idle_sec": 0,       # 추적 로직 없음 -> 항상 0 (한계, 문서 상단 참고)
            "head_speed_mps": e["head_speed_mps"],
        }
        for i, e in enumerate(events)
    ]

    os.makedirs(DATA_DIR, exist_ok=True)
    session_path = os.path.join(DATA_DIR, "real_sessions.csv")
    events_path = os.path.join(DATA_DIR, "real_events.csv")

    mode = "a" if args.append and os.path.exists(session_path) else "w"
    write_header = mode == "w"

    with open(session_path, mode, newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=SESSION_FIELDS)
        if write_header:
            writer.writeheader()
        writer.writerow(session_row)

    mode = "a" if args.append and os.path.exists(events_path) else "w"
    write_header = mode == "w"
    with open(events_path, mode, newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=EVENT_FIELDS)
        if write_header:
            writer.writeheader()
        writer.writerows(event_rows)

    print(f"세션 1개(session_id={args.session_id}), 활성화 이벤트 {len(event_rows)}개 추출 완료")
    print(f"전체 스냅샷 레코드 수: {len(snapshots)}개, 세션 길이: {t_session_min}분")
    print(f"저장 위치: {session_path}")
    print(f"저장 위치: {events_path}")
    if len(event_rows) == 0:
        print("경고: 활성화 이벤트가 0개입니다. 이 로그 구간에서는 Passthrough가 한 번도 켜지지 않았을 수 있어요.")


if __name__ == "__main__":
    main()
