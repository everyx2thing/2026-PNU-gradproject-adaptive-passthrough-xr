"""
Cold-start 처리 로직 (3.4.2절)

세션 수가 N_MIN_SESSIONS 미만이면 전체 사용자 공통 기본 가중치(w_Default)를 쓰고,
그 이상 누적되면 개인화 값으로 전환합니다.

## 왜 필요한가
- 신규 사용자는 아직 로그가 적어서 개인화 모델이 신뢰할 만한 예측을 못 함
- 데이터 부족 상태에서 개인화를 강행하면 오히려 이상한 가중치가 나올 위험 있음
- 그래서 "안전한 기본값 -> 점진적 개인화" 전환 구조가 필요

## 주의: 이 파일은 아직 완성이 아님
`get_personalized_params()`의 "개인화 분기"는 지금 자리만 잡아둔 상태입니다.
분류 모델(Positive/Negative) 결과를 실제 가중치(w_c, w_s, w_d, w_i)와
임계값(tau_viz)으로 변환하는 매핑 로직은 아직 설계 전이라 다음 단계에서 채울 예정
(TODO: 이 부분이 진짜 핵심 산출물이고 다음에 작업할 것).
지금은 세션 수 기준으로 "기본값을 쓸지 말지" 판단하는 게이트만 완성된 상태입니다.
"""

from config import N_MIN_SESSIONS

# ===== 기본 가중치 (전체 사용자 공통, cold-start 구간에서 사용) =====
# TODO: 이 값들은 지금 임의로 균등 배분한 것. 3.3절 담당자(아영님)의 R_collision/
# R_static/R_dynamic/R_intent 산출 방식이 확정되면, 그에 맞춰 초기값 재조정 필요
W_DEFAULT = {
    "w_c": 0.30,  # collision 가중치
    "w_s": 0.20,  # static 가중치
    "w_d": 0.30,  # dynamic 가중치
    "w_i": 0.20,  # intent 가중치
}
assert abs(sum(W_DEFAULT.values()) - 1.0) < 1e-6, "가중치 합은 1이어야 함"

TAU_VIZ_DEFAULT = 0.5  # TODO: 임의값, 실험/사용자 테스트로 조정 필요

# N_MIN_SESSIONS는 config.py에서 import (train_model.py와 값 공유)


def is_cold_start(session_count: int) -> bool:
    """세션 수 기준으로 아직 cold-start 구간인지 판단"""
    return session_count < N_MIN_SESSIONS


def get_default_params() -> dict:
    """전체 사용자 공통 기본 파라미터 반환"""
    return {
        **W_DEFAULT,
        "tau_viz": TAU_VIZ_DEFAULT,
        "source": "default",
    }


def get_personalized_params(session_count: int, feature_vector=None, model=None) -> dict:
    """
    세션 수에 따라 기본값 또는 개인화 값을 반환하는 진입점.

    Parameters
    ----------
    session_count : int
        해당 사용자의 누적 세션 수
    feature_vector : list[float] or None
        7차원 feature 벡터 (개인화 분기에서 사용 예정, 아직 미사용)
    model : object or None
        학습된 RF 모델 (개인화 분기에서 사용 예정, 아직 미사용)

    Returns
    -------
    dict : {"w_c", "w_s", "w_d", "w_i", "tau_viz", "source"}
    """
    if is_cold_start(session_count):
        return get_default_params()

    # ===== 여기부터 아직 미완성 (다음 작업 대상) =====
    # 지금은 분류 모델(Positive/Negative)만 있고, 이걸 실제 가중치로 바꾸는
    # 매핑 로직이 없어서 임시로 기본값을 그대로 반환합니다.
    # 다음 단계에서: feature_vector, model을 받아서 실제 w_c/w_s/w_d/w_i/tau_viz를
    # 계산하는 로직으로 교체할 예정.
    print("[TODO] 개인화 매핑 로직 미구현 - 임시로 기본값 반환")
    result = get_default_params()
    result["source"] = "default_fallback_not_yet_personalized"
    return result


if __name__ == "__main__":
    # 간단한 동작 확인 (세션 수별로 어떤 값이 나오는지)
    print("=== Cold-start 게이트 동작 확인 ===\n")
    for session_count in [0, 3, 4, 5, 6, 10, 20]:
        params = get_personalized_params(session_count)
        print(f"세션 수={session_count:>3} -> source={params['source']:<35} "
              f"tau_viz={params['tau_viz']}")
