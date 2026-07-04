"""
가중치/임계값 매핑 로직 (3.4.2절 핵심 산출물)

RF 모델의 확률 예측(predict_proba)을 이용해서, 최근 윈도우들의 "Negative였을 확률"
평균을 구하고 -> 그 값에 따라 w_c(충돌 가중치)와 tau_viz(임계값)를 연속적으로 조정합니다.

## 핵심 아이디어
- p_negative가 높다 = 최근에 "괜히 켜진" 활성화가 많았다는 뜻
  -> Passthrough가 너무 민감하게 반응하고 있다는 신호
  -> w_c(충돌 위험 가중치)를 낮추고, tau_viz(반응 임계값)를 높여서
     "더 확실한 위험 상황에서만 반응하도록" 보수적으로 전환
- p_negative가 낮다 = 활성화들이 대체로 필요했다는 뜻 -> 기본값 근처 유지

## 왜 규칙 기반이 아니라 확률 기반인가
- 계단식(예: "Negative면 -0.1")이 아니라 연속적으로 부드럽게 조정되어야
  보고서에서 말한 "점진적 최적화"에 맞음
- p_negative=0.51과 0.95는 실제로 다른 정도의 조정을 받아야 함
"""

from cold_start import W_DEFAULT, TAU_VIZ_DEFAULT

# 조정 강도: p_negative가 0~1로 변할 때 가중치/임계값이 최대 얼마나 움직일지
# TODO: 임의값. 사용자 테스트 결과 보면서 튜닝 필요
ADJUSTMENT_SCALE = 0.2

# 안전장치: 가중치가 너무 0에 가까워지지 않도록 하한선
MIN_WEIGHT = 0.05


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
                                 w_default=None, tau_default=None,
                                 adjustment_scale=ADJUSTMENT_SCALE) -> dict:
    """
    feature_vectors + model -> 실제 개인화된 w_c, w_s, w_d, w_i, tau_viz 계산

    조정 규칙:
    - delta = (p_negative - 0.5) * adjustment_scale
      (p_negative=0.5를 기준점으로 삼음: 그 이상이면 보수적으로, 이하면 기본값 유지 쪽)
    - w_c는 delta만큼 감소 (하한 MIN_WEIGHT)
    - 감소분은 w_s, w_d, w_i에 균등하게 재분배 (합이 항상 1 유지)
    - tau_viz는 delta만큼 증가 (0~1 범위로 clip)
    """
    if w_default is None:
        w_default = W_DEFAULT
    if tau_default is None:
        tau_default = TAU_VIZ_DEFAULT

    p_negative = _get_negative_probability(feature_vectors, model)
    delta = (p_negative - 0.5) * adjustment_scale

    # delta가 음수(=p_negative < 0.5)면 w_c를 늘리는 방향인데,
    # 지금은 "보수적으로 전환"하는 방향만 우선 구현. 음수 delta는 0으로 clip
    # (즉, 활성화가 잘 맞았으면 굳이 더 민감하게 만들지는 않음 -> 안전 우선)
    # TODO: 사용자 테스트에서 "너무 둔감하다"는 피드백 나오면 이 부분 재검토
    delta = max(delta, 0.0)

    w_c_new = max(w_default["w_c"] - delta, MIN_WEIGHT)
    actual_reduction = w_default["w_c"] - w_c_new  # 하한에 걸렸으면 delta보다 작을 수 있음

    # 감소분을 w_s, w_d, w_i에 원래 비율대로 재분배
    other_keys = ["w_s", "w_d", "w_i"]
    other_total = sum(w_default[k] for k in other_keys)
    redistributed = {
        k: w_default[k] + actual_reduction * (w_default[k] / other_total)
        for k in other_keys
    }

    tau_viz_new = min(tau_default + delta, 1.0)

    result = {
        "w_c": round(w_c_new, 4),
        "w_s": round(redistributed["w_s"], 4),
        "w_d": round(redistributed["w_d"], 4),
        "w_i": round(redistributed["w_i"], 4),
        "tau_viz": round(tau_viz_new, 4),
        "p_negative": round(p_negative, 4),
        "source": "personalized",
    }

    # 검증용: 가중치 합이 여전히 1에 가까운지 확인 (부동소수점 오차 감안)
    weight_sum = result["w_c"] + result["w_s"] + result["w_d"] + result["w_i"]
    assert abs(weight_sum - 1.0) < 1e-3, f"가중치 합 이상함: {weight_sum}"

    return result


if __name__ == "__main__":
    # 간단한 동작 확인용 (가짜 확률로 delta 방향만 검증)
    print("=== delta 방향 확인 (모델 없이 수식만 검증) ===")
    for p_neg in [0.0, 0.3, 0.5, 0.7, 0.95, 1.0]:
        delta = max((p_neg - 0.5) * ADJUSTMENT_SCALE, 0.0)
        w_c = max(W_DEFAULT["w_c"] - delta, MIN_WEIGHT)
        tau = min(TAU_VIZ_DEFAULT + delta, 1.0)
        print(f"p_negative={p_neg:.2f} -> w_c={w_c:.4f}, tau_viz={tau:.4f}")
