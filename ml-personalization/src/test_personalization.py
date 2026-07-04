"""
개인화 파이프라인 전체 동작 확인 (cold_start.py + personalize.py 통합 테스트)

세션별로 다음을 확인합니다:
- 세션 수가 적으면 (N_MIN_SESSIONS 미만) -> 기본값 반환하는지
- 세션 수가 충분하면 -> 실제 모델 확률 기반으로 조정된 값이 나오는지
- Negative 비율이 높은 세션 vs 낮은 세션이 서로 다른 값을 받는지
"""

import os
import joblib
import pandas as pd

from cold_start import is_cold_start, get_default_params, N_MIN_SESSIONS
from personalize import compute_personalized_params
from config import FEATURE_COLUMNS

DATA_DIR = os.path.join(os.path.dirname(__file__), "..", "data")
MODEL_DIR = os.path.join(os.path.dirname(__file__), "..", "models")


def get_params_for_session(session_id, session_windows, model):
    """cold_start 게이트 + personalize 매핑을 합친 최종 진입점"""
    session_count = len(session_windows)  # 지금은 "이 세션 내 윈도우 개수"로 대체
    # TODO: 실제로는 "이 사용자의 누적 세션 수"가 맞음. 지금 mock 구조에서는
    # 세션 하나 = 사용자 한 명이 아니라서, 실 데이터 붙을 때 이 부분 재정의 필요

    if is_cold_start(session_count):
        return get_default_params()

    X = session_windows[FEATURE_COLUMNS].values
    return compute_personalized_params(X, model)


def main():
    model_path = os.path.join(MODEL_DIR, "rf_personalization.joblib")
    model = joblib.load(model_path)

    df = pd.read_csv(os.path.join(DATA_DIR, "mock_features.csv"))

    print(f"=== 세션별 개인화 파라미터 확인 (N_MIN_SESSIONS={N_MIN_SESSIONS}) ===\n")

    for session_id, group in df.groupby("session_id"):
        neg_ratio = group["negative_ratio"].mean()
        params = get_params_for_session(session_id, group, model)

        print(f"세션 {session_id} (윈도우 {len(group)}개, 평균 negative_ratio={neg_ratio:.2f})")
        print(f"  -> source={params['source']}, w_c={params['w_c']}, "
              f"tau_viz={params['tau_viz']}"
              + (f", p_negative={params.get('p_negative')}" if 'p_negative' in params else ""))
        print()


if __name__ == "__main__":
    main()
