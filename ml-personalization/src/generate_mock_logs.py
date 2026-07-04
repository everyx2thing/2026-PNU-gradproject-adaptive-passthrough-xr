"""
Mock 로그 생성 스크립트 (3.4.1절 로그 수집 구조 기준)

생성되는 두 종류의 로그:
- 세션 로그 (data/mock_sessions.csv): 세션당 1행
- 이벤트 로그 (data/mock_events.csv): Passthrough 활성화 이벤트당 1행

일부러 심어둔 패턴 (나중에 모델이 이걸 배우는지 확인용):
- 공간(A_space)이 좁을수록 → 활성화 빈도(N_activate) ↑, 수동 해제 비율(N_cancel) ↑
- 세션 누적 시간(T_session)이 길수록 → 평균 지속시간(t_pt) 약간 ↓ (피로/둔감화 가정)
"""

import csv
import random
import os

random.seed(42)  # 재현 가능하게 고정. 다른 랜덤 패턴 보고 싶으면 값 바꾸거나 주석처리

OUTPUT_DIR = os.path.join(os.path.dirname(__file__), "..", "data")
N_SESSIONS = 30
EVENTS_PER_SESSION_RANGE = (20, 50)  # 사용자가 선택한 범위
SPACE_RANGE_M2 = (2.0, 8.0)  # 한국 원룸 환경 기준으로 조정


def generate_session(session_id: int):
    """세션 하나에 대한 기본 속성 생성"""
    a_space = round(random.uniform(*SPACE_RANGE_M2), 2)
    # 세션이 뒤로 갈수록(=유저가 더 여러 번 플레이했다고 가정) 누적 세션 시간이 길어지는 경향
    t_session = round(random.uniform(5, 15) + session_id * random.uniform(0.5, 2.0), 2)  # 분 단위
    return {
        "session_id": session_id,
        "A_space": a_space,
        "T_session": t_session,
    }


def generate_events_for_session(session):
    """세션 하나에 대한 Passthrough 활성화 이벤트들 생성"""
    events = []
    n_events = random.randint(*EVENTS_PER_SESSION_RANGE)

    a_space = session["A_space"]
    t_session = session["T_session"]

    # 공간이 좁을수록(4㎡에 가까울수록) 위험 신호가 잦다고 가정 -> 활성화 비율 자체는
    # n_events로 이미 반영되어 있으므로, 여기서는 "얼마나 자주 괜히 켜지는지"를 좌우
    narrowness = 1.0 - (a_space - SPACE_RANGE_M2[0]) / (SPACE_RANGE_M2[1] - SPACE_RANGE_M2[0])
    # narrowness: 0 (넓음) ~ 1 (좁음)

    # 세션이 피로 누적됐다고 볼 정도로 길면 지속시간이 짧아지는 경향 부여
    fatigue_factor = min(t_session / 30.0, 1.0)  # 0~1로 정규화

    for event_id in range(n_events):
        # 좁을수록 수동 해제(=괜히 켜졌다고 판단) 확률 ↑
        cancel_prob = 0.15 + 0.5 * narrowness
        is_cancelled = random.random() < cancel_prob

        if is_cancelled:
            # 취소되는 경우 지속시간 짧게 (0.3~2.5초) -> Negative 라벨 후보
            duration = round(random.uniform(0.3, 2.5), 2)
        else:
            # 정상 지속되는 경우 (3~15초), 피로도가 높을수록 살짝 더 짧아짐
            base_duration = random.uniform(3.0, 15.0)
            duration = round(base_duration * (1.0 - 0.3 * fatigue_factor), 2)

        idle_time = round(random.uniform(0.5, 6.0), 2)  # 컨트롤러 유휴시간(초)
        v_h = round(random.uniform(0.05, 1.5), 3)  # 순간 이동속도 (m/s)

        events.append({
            "session_id": session["session_id"],
            "event_id": event_id,
            "timestamp_sec": round(event_id * random.uniform(5, 20), 2),  # 세션 내 상대 시간
            "duration_sec": duration,
            "is_manual_cancel": int(is_cancelled),
            "controller_idle_sec": idle_time,
            "head_speed_mps": v_h,
        })

    return events


def main():
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    sessions = [generate_session(sid) for sid in range(1, N_SESSIONS + 1)]
    all_events = []
    for session in sessions:
        all_events.extend(generate_events_for_session(session))

    # 세션 로그 저장
    session_path = os.path.join(OUTPUT_DIR, "mock_sessions.csv")
    with open(session_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=["session_id", "A_space", "T_session"])
        writer.writeheader()
        writer.writerows(sessions)

    # 이벤트 로그 저장
    events_path = os.path.join(OUTPUT_DIR, "mock_events.csv")
    with open(events_path, "w", newline="", encoding="utf-8") as f:
        fieldnames = [
            "session_id", "event_id", "timestamp_sec", "duration_sec",
            "is_manual_cancel", "controller_idle_sec", "head_speed_mps",
        ]
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(all_events)

    print(f"세션 {len(sessions)}개, 이벤트 {len(all_events)}개 생성 완료")
    print(f"저장 위치: {session_path}")
    print(f"저장 위치: {events_path}")


if __name__ == "__main__":
    main()
