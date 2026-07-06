"""
공통 상수 모음

지금까지 FEATURE_COLUMNS, N_MIN_SESSIONS 같은 값들이 여러 파일
(train_model.py, cold_start.py, convert_to_onnx.py, test_personalization.py,
hyperparam_tuning.py)에 각각 따로 정의돼 있었음. 한쪽만 고치고 다른 쪽을
안 고치면 값이 어긋나는 위험이 있어서 여기 한 곳으로 모음.

앞으로 새 스크립트를 만들 때도 상수를 직접 정의하지 말고 여기서 import해서 쓸 것.
"""

# 7차원 feature 벡터의 컬럼 순서 (모든 스크립트에서 이 순서를 지켜야 함)
FEATURE_COLUMNS = [
    "f_pt", "r_cancel", "t_pt_bar", "v_h_bar", "v_h_max",
    "A_space_norm", "T_session_norm",
]

# 라벨링 규칙 (3.4.3절)
CANCEL_NEGATIVE_THRESHOLD = 2.0   # 이 시간(초) 미만 + 수동해제 -> Negative
POSITIVE_THRESHOLD = 3.0          # 이 시간(초) 이상 + 해제 없음 -> Positive

# Cold-start 기준 (3.4.2절): 세션 수가 이보다 적으면 기본 가중치 사용
N_MIN_SESSIONS = 5  # TODO: 하이퍼파라미터, 실험 필요

# 슬라이딩 윈도우 기본값 (build_features.py)
DEFAULT_WINDOW_SIZE = 10  # TODO: hyperparam_tuning.py 실험 결과 보고 재조정 검토
DEFAULT_STRIDE = 5        # TODO: 위와 동일
