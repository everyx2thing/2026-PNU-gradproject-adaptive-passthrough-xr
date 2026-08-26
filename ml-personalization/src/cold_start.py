"""
Cold-start 처리 로직 (3.4.2절)

세션 수가 N_MIN_SESSIONS 미만이면 전체 사용자 공통 기본 가중치(w_Default)를 쓰고,
그 이상 누적되면 개인화 값으로 전환합니다.

## 왜 필요한가
- 신규 사용자는 아직 로그가 적어서 개인화 모델이 신뢰할 만한 예측을 못 함
- 데이터 부족 상태에서 개인화를 강행하면 오히려 이상한 가중치가 나올 위험 있음
- 그래서 "안전한 기본값 -> 점진적 개인화" 전환 구조가 필요

`get_personalized_params()`가 cold-start 게이트 + personalize.py의 확률 기반
매핑까지 합친 최종 진입점입니다 (Unity 쪽 PersonalizationSentisRunner.GetPersonalizedParams와
동일한 구조 — docs/ONDEVICE_SENTIS_INTEGRATION.md 참고).
"""

from config import N_MIN_SESSIONS, THRESHOLD_DEFAULT
from personalize import compute_personalized_params

# THRESHOLD_DEFAULT, N_MIN_SESSIONS는 config.py에서 공유 (train_model.py,
# personalize.py와 값 일치 보장)


def is_cold_start(session_count: int) -> bool:
    """세션 수 기준으로 아직 cold-start 구간인지 판단"""
    return session_count < N_MIN_SESSIONS


def get_default_params() -> dict:
    """전체 사용자 공통 기본 파라미터 반환"""
    return {
        **THRESHOLD_DEFAULT,
        "source": "default",
    }


def get_personalized_params(session_count: int, feature_vectors=None, model=None) -> dict:
    """
    세션 수에 따라 기본값 또는 개인화 값을 반환하는 최종 진입점.

    Parameters
    ----------
    session_count : int
        해당 사용자의 누적 세션 수 (cold-start 판단 기준)
    feature_vectors : list[list[float]] or None
        최근 윈도우들의 7차원 feature 벡터 목록. cold-start가 아니면 필수.
    model : object or None
        학습된 RF 모델 (predict_proba 지원). cold-start가 아니면 필수.

    Returns
    -------
    dict : {"stable_on_threshold", "rapid_on_threshold", "hand_full_threshold",
            "source", (p_negative)}
    """
    if is_cold_start(session_count):
        return get_default_params()

    if feature_vectors is None or model is None:
        raise ValueError(
            "cold-start 구간이 아니면 feature_vectors와 model이 필요합니다 "
            f"(session_count={session_count}, N_MIN_SESSIONS={N_MIN_SESSIONS})"
        )

    return compute_personalized_params(feature_vectors, model)


if __name__ == "__main__":
    # 간단한 동작 확인 (게이트 자체만: 모델 없이 확인 가능한 cold-start 분기)
    # 실제 모델까지 포함한 전체 동작 확인은 test_personalization.py 참고.
    print("=== Cold-start 게이트 동작 확인 ===\n")
    for session_count in [0, 3, 4, 5, 6, 10, 20]:
        if is_cold_start(session_count):
            params = get_personalized_params(session_count)
            print(f"세션 수={session_count:>3} -> source={params['source']:<35} "
                  f"stable_on_threshold={params['stable_on_threshold']}")
        else:
            print(f"세션 수={session_count:>3} -> cold-start 아님 (모델 필요, "
                  f"test_personalization.py에서 확인)")
