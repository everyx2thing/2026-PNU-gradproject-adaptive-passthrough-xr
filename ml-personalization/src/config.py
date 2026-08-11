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
# 2026-07-28: mock은 원래 가정(활성화 3~15초)에 맞춘 값 그대로 유지.
# real은 실제 Quest 로그 확인 결과 활성화가 평균 0.17초/최대 0.57초로 훨씬 짧아서
# 별도 값 사용 (임시 실험값, 팀 회의에서 실데이터 기준 재조정 필요 -> TODO).
CANCEL_NEGATIVE_THRESHOLD = 2.0   # mock용: 이 시간(초) 미만 -> Negative
POSITIVE_THRESHOLD = 3.0          # mock용: 이 시간(초) 이상 -> Positive

REAL_CANCEL_NEGATIVE_THRESHOLD = 0.15  # real용 (TODO: 임시값, 데이터 더 쌓이면 재조정)
REAL_POSITIVE_THRESHOLD = 0.3          # real용 (TODO: 임시값, 데이터 더 쌓이면 재조정)

# 슬라이딩 윈도우 기본값 (build_features.py)
# 2026-07-28: real 데이터는 세션/이벤트 수가 아직 적어서 mock과 다른 값 사용.
DEFAULT_WINDOW_SIZE = 10  # mock용. TODO: hyperparam_tuning.py 실험 결과 보고 재조정 검토
DEFAULT_STRIDE = 5        # mock용. TODO: 위와 동일

REAL_WINDOW_SIZE = 3      # real용 (TODO: 데이터 늘어나면 mock과 같은 10으로 복귀 검토)
REAL_STRIDE = 1           # real용 (TODO: 위와 동일)

# Cold-start 기준 (3.4.2절): 세션 수가 이보다 적으면 기본 가중치 사용
N_MIN_SESSIONS = 5  # TODO: 하이퍼파라미터, 실험 필요

# 기본 임계값 (전체 사용자 공통, cold-start 구간에서 사용) — cold_start.py, personalize.py 공유
# (여기 둔 이유: cold_start.py <-> personalize.py 상호 import를 피하기 위해 공통 상수로 분리)
#
# 2026-08-06: 3.3절 담당(아영님)의 unity-client 쪽이 "Rtotal = w_c*R_c + w_s*R_s + w_d*R_d + w_i*R_i
# 가중합 + 단일 tau_viz 임계값" 구조에서 "R_static_head/R_static_hand 두 경로 + UserState로
# on/off 임계값을 Lerp 조절하는 구조"(QuestRiskExperimentLogger.cs, feature/static-boundary-passthrough
# 브랜치)로 바뀌어서, 개인화 출력도 그에 맞춰 가중치가 아니라 임계값 조정으로 바꿈.
# 아래 기본값은 QuestRiskExperimentLogger.cs의 [SerializeField] 기본값과 동일하게 맞춤 —
# 그쪽 값이 바뀌면 여기도 같이 바꿔야 함 (아직 그 브랜치는 main에 머지되지 않음, TODO: 머지되면
# 실제 필드명 노출 방식(public setter 등) 확인 후 PersonalizationSentisRunner.cs 연결).
THRESHOLD_DEFAULT = {
    "stable_on_threshold": 0.65,   # UserState=0(정적)일 때 Passthrough ON 기준
    "rapid_on_threshold": 0.45,    # UserState=1(동적)일 때 Passthrough ON 기준
    "hand_full_threshold": 0.85,   # R_static_hand ON 기준
}

# 안전장치: 개인화로 인해 임계값이 너무 1에 가까워져서(=사실상 절대 안 켜짐) 안전 기능이
# 무력화되지 않도록 하는 상한선
MAX_THRESHOLD = 0.95
