"""
임계값 매핑 로직 (3.4.2절 핵심 산출물)

RF 모델의 확률 예측(predict_proba)을 이용해서, 최근 윈도우들의 "Negative였을 확률"
평균을 구하고 -> 그 값에 따라 Passthrough ON 임계값들을 연속적으로 조정합니다.

## 2026-08-06 설계 변경
원래는 "Rtotal = w_c*R_c + w_s*R_s + w_d*R_d + w_i*R_i 가중합 + 단일 tau_viz 임계값"
구조를 전제로 w_c/w_s/w_d/w_i,tau_viz를 출력했는데, unity-client(아영님) 쪽 구조가
`QuestRiskExperimentLogger.cs`(feature/static-boundary-passthrough 브랜치) 기준
"R_static_head/R_static_hand 두 경로 + UserState로 on/off 임계값을 Lerp 조절"하는
방식으로 바뀌어서, 더 이상 대응되는 4개 가중치가 없음. 그래서 개인화 출력도
그 구조가 실제로 쓰는 임계값(stable_on_threshold, rapid_on_threshold,
hand_full_threshold)을 직접 조정하는 방식으로 바꿈.
(이 브랜치는 이 문서 작성 시점에 아직 main에 머지되지 않음 — TODO: 머지 여부/필드
노출 방식 확인 후 PersonalizationSentisRunner.cs 쪽 재확인 필요)

## 핵심 아이디어 (예전과 동일, 적용 대상만 바뀜)
- p_negative가 높다 = 최근에 "괜히 켜진" 활성화가 많았다는 뜻
  -> Passthrough가 너무 민감하게 반응하고 있다는 신호
  -> ON 임계값들을 높여서 "더 확실한 위험 상황에서만 반응하도록" 보수적으로 전환
- p_negative가 낮다 = 활성화들이 대체로 필요했다는 뜻 -> 기본값 근처 유지

## 왜 규칙 기반이 아니라 확률 기반인가
- 계단식(예: "Negative면 -0.1")이 아니라 연속적으로 부드럽게 조정되어야
  보고서에서 말한 "점진적 최적화"에 맞음
- p_negative=0.51과 0.95는 실제로 다른 정도의 조정을 받아야 함
"""

from config import THRESHOLD_DEFAULT, MAX_THRESHOLD

# 조정 강도: p_negative가 0~1로 변할 때 임계값이 최대 얼마나 움직일지
# TODO: 임의값. 사용자 테스트 결과 보면서 튜닝 필요
ADJUSTMENT_SCALE = 0.2


def _get_negative_probability(feature_vectors, model) -> float:
    """
    최근 윈도우들(feature_vectors)에 대해 모델이 예측한 "Negative일 확률"의 평균을 계산

    Parameters
    ----------
    feature_vectors : list[list[float]]
        최근 N개 윈도우의 7차원 feature 벡터들
    model : sklearn 모델 (predict_proba 지원해야 함)

    Returns
    -------
    float : 0~1 사이, Negative로 예측될 평균 확률
    """
    probs = model.predict_proba(feature_vectors)  # shape: (n_windows, n_classes)
    classes = list(model.classes_)

    if "Negative" not in classes:
        # 학습 데이터에 Negative 샘플이 아예 없었던 극단적 케이스 방어
        # TODO: 이런 경우 별도 처리 필요 (지금은 안전하게 0 반환 -> 기본값 유지)
        return 0.0

    negative_idx = classes.index("Negative")
    negative_probs = probs[:, negative_idx]
    return float(negative_probs.mean())


def compute_personalized_params(feature_vectors, model,
                                 threshold_default=None,
                                 adjustment_scale=ADJUSTMENT_SCALE) -> dict:
    """
    feature_vectors + model -> 실제 개인화된 stable_on_threshold, rapid_on_threshold,
    hand_full_threshold 계산

    조정 규칙:
    - delta = (p_negative - 0.5) * adjustment_scale
      (p_negative=0.5를 기준점으로 삼음: 그 이상이면 보수적으로, 이하면 기본값 유지 쪽)
    - 모든 ON 임계값에 delta를 더함 (상한 MAX_THRESHOLD) — 값이 클수록 더 확실한
      위험 상황에서만 Passthrough가 켜짐 (덜 민감해짐)
    """
    if threshold_default is None:
        threshold_default = THRESHOLD_DEFAULT

    p_negative = _get_negative_probability(feature_vectors, model)
    delta = (p_negative - 0.5) * adjustment_scale

    # delta가 음수(=p_negative < 0.5)면 임계값을 낮추는(더 민감해지는) 방향인데,
    # 지금은 "보수적으로 전환"하는 방향만 우선 구현. 음수 delta는 0으로 clip
    # (즉, 활성화가 잘 맞았으면 굳이 더 민감하게 만들지는 않음 -> 안전 우선)
    # TODO: 사용자 테스트에서 "너무 둔감하다"는 피드백 나오면 이 부분 재검토
    delta = max(delta, 0.0)

    result = {
        key: round(min(value + delta, MAX_THRESHOLD), 4)
        for key, value in threshold_default.items()
    }
    result["p_negative"] = round(p_negative, 4)
    result["source"] = "personalized"

    return result


if __name__ == "__main__":
    # 간단한 동작 확인용 (가짜 확률로 delta 방향만 검증)
    print("=== delta 방향 확인 (모델 없이 수식만 검증) ===")
    for p_neg in [0.0, 0.3, 0.5, 0.7, 0.95, 1.0]:
        delta = max((p_neg - 0.5) * ADJUSTMENT_SCALE, 0.0)
        stable_on = min(THRESHOLD_DEFAULT["stable_on_threshold"] + delta, MAX_THRESHOLD)
        rapid_on = min(THRESHOLD_DEFAULT["rapid_on_threshold"] + delta, MAX_THRESHOLD)
        print(f"p_negative={p_neg:.2f} -> stable_on_threshold={stable_on:.4f}, "
              f"rapid_on_threshold={rapid_on:.4f}")
