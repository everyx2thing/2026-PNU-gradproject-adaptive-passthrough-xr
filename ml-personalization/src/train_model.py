"""
Random Forest 개인화 모델 학습 (3.4.2절)

입력: data/mock_features.csv (build_features.py 결과물)
출력:
- 학습된 모델 성능 리포트 (accuracy, feature importance)
- models/rf_personalization.joblib (학습된 모델 파일, 다음 단계 ONNX 변환에 사용)

지금은 window_label(Positive/Negative/Neutral)을 분류하는 문제로 학습합니다.
실제로는 이 모델의 목적이 "위험 가중치(w_c,w_s,w_d,w_i)와 임계값(tau_viz)을 개인화"하는
것이지만, 그 값 자체를 직접 라벨링할 방법이 없어서(실 사용자 반응 데이터가 아직 없음),
1차 프로토타입에서는 "이 활성화가 필요했는지(Positive/Negative)"를 예측하는 걸로
대체합니다. 나중에 실 데이터가 쌓이면, 이 분류 결과를 바탕으로 가중치를 조정하는
로직을 얹는 방식으로 확장할 예정 (TODO).
"""

import os
import pandas as pd
from sklearn.model_selection import train_test_split
from sklearn.ensemble import RandomForestClassifier
from sklearn.metrics import classification_report, accuracy_score
import joblib

DATA_DIR = os.path.join(os.path.dirname(__file__), "..", "data")
MODEL_DIR = os.path.join(os.path.dirname(__file__), "..", "models")

FEATURE_COLUMNS = [
    "f_pt", "r_cancel", "t_pt_bar", "v_h_bar", "v_h_max",
    "A_space_norm", "T_session_norm",
]
LABEL_COLUMN = "window_label"

# Cold-start 처리 기준 (3.4.2절): 세션 수가 이보다 적으면 기본 가중치 w_Default 사용
N_MIN_SESSIONS = 5  # TODO: 하이퍼파라미터, 실험 필요


def main():
    os.makedirs(MODEL_DIR, exist_ok=True)

    df = pd.read_csv(os.path.join(DATA_DIR, "mock_features.csv"))

    print(f"전체 윈도우 수: {len(df)}")
    print(f"라벨 분포:\n{df[LABEL_COLUMN].value_counts()}\n")

    X = df[FEATURE_COLUMNS]
    y = df[LABEL_COLUMN]

    # 데이터가 적어서 stratify 실패할 수 있으니 시도 후 실패하면 일반 split으로 대체
    try:
        X_train, X_test, y_train, y_test = train_test_split(
            X, y, test_size=0.2, random_state=42, stratify=y
        )
    except ValueError:
        print("stratify 실패 (클래스 표본 부족) -> 일반 split으로 대체")
        X_train, X_test, y_train, y_test = train_test_split(
            X, y, test_size=0.2, random_state=42
        )

    model = RandomForestClassifier(
        n_estimators=100,
        max_depth=5,       # 데이터가 적으니 과적합 방지용으로 얕게 (TODO: 튜닝 필요)
        random_state=42,
    )
    model.fit(X_train, y_train)

    y_pred = model.predict(X_test)
    acc = accuracy_score(y_test, y_pred)

    print(f"\n=== 평가 결과 (테스트 셋 {len(X_test)}개) ===")
    print(f"Accuracy: {acc:.3f}")
    print("\n분류 리포트:")
    print(classification_report(y_test, y_pred, zero_division=0))

    print("\n=== Feature Importance ===")
    importances = sorted(
        zip(FEATURE_COLUMNS, model.feature_importances_),
        key=lambda x: x[1], reverse=True,
    )
    for name, importance in importances:
        print(f"  {name}: {importance:.4f}")

    model_path = os.path.join(MODEL_DIR, "rf_personalization.joblib")
    joblib.dump(model, model_path)
    print(f"\n모델 저장 완료: {model_path}")


if __name__ == "__main__":
    main()
