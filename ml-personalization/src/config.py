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

# 2026-09-21: duration만으로는 "짧게 켜졌지만 실제로 위험했던 경우"와 "그냥 임계값을
# 살짝 넘겨서 오탐으로 켜진 경우"를 구분할 수 없음. RiskSnapshot에 이미 있는
# static/dynamic 위험도의 "활성화 중 최고값(peak_risk)"을 duration과 같이 봐서 구분함
# (build_features.py의 label_event 참고). mock/real 공용 — 아직 실 데이터로 이 경계값을
# 검증한 적은 없어서 duration 임계값들처럼 mock/real을 나누지 않음 (TODO: 실 데이터
# 쌓이면 재조정 검토).
PEAK_RISK_BORDERLINE_THRESHOLD = 0.65  # 이 아래면 "임계값을 살짝 넘긴 수준" -> 오탐 추정
PEAK_RISK_HIGH_THRESHOLD = 0.80        # 이 이상이면 짧아도 "실제로 위험했음" -> Positive

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
#
# 2026-09-21: 위 브랜치가 main에 머지된 뒤 실제 Quest 실기 테스트로 기본값이 재조정된 걸
# 여기서도 반영함. 기준은 unity-client의 StaticBoundaryRiskModels.cs(StaticBoundaryPolicySettings)
# 및 PersonalizationModels.cs(PersonalizationMath.Default*) — 그쪽 주석에 따르면 정지 상태
# head/저장애물 위험은 실측상 0.65까지 거의 안 올라가고(0.25m 비상 경로가 별도 처리), 손
# 위험도(reach-gated)는 비상 범위 밖에서 0.52를 못 넘어서 기존 0.85는 사실상 절대 도달 못
# 하는 죽은 임계값이었음. 이 THRESHOLD_DEFAULT는 unity-client 쪽 값과 항상 같아야 함 —
# 한쪽만 바꾸고 다른 쪽을 안 바꾸면 개인화 결과가 서로 다른 기준선을 가정하게 됨.
THRESHOLD_DEFAULT = {
    "stable_on_threshold": 0.50,   # UserState=0(정적)일 때 Passthrough ON 기준
    "rapid_on_threshold": 0.45,    # UserState=1(동적)일 때 Passthrough ON 기준
    "hand_full_threshold": 0.40,   # R_static_hand ON 기준
}

# 안전장치: 개인화로 인해 임계값이 너무 1에 가까워져서(=사실상 절대 안 켜짐) 안전 기능이
# 무력화되지 않도록 하는 상한선
MAX_THRESHOLD = 0.95
