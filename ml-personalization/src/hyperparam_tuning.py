"""
슬라이딩 윈도우 하이퍼파라미터(WINDOW_SIZE, STRIDE) 튜닝 실험

여러 (W, S) 조합에 대해:
- 생성되는 윈도우 개수
- 라벨 분포 (Positive/Negative/Neutral 균형)
- Cross-validation 평균 accuracy (5-fold)
를 비교해서 어떤 조합이 나은지 참고 자료를 만듭니다.

## 주의 (정직하게 밝혀둠)
지금 라벨(window_label)이 애초에 feature(r_cancel, t_pt_bar)로부터 파생된 값이라,
accuracy는 "모델이 얼마나 잘 맞추는가"보다는 "그 조합에서 클래스가 얼마나 잘
구분되는 구조로 나뉘는가"에 가까운 참고 지표입니다. 실 데이터가 들어오면 이
실험을 다시 돌려서 진짜 의미 있는 비교를 해야 합니다 (TODO).

그래도 지금 비교로 확인할 수 있는 것:
- 윈도우가 너무 작으면(W 작음) 노이즈가 커서 클래스 불균형이 심해질 수 있음
- 윈도우가 너무 크면(W 큼) 윈도우 개수 자체가 줄어서 학습 데이터가 부족해짐
- 이 트레이드오프의 "감"을 잡는 용도
"""

import pandas as pd
from sklearn.ensemble import RandomForestClassifier
from sklearn.model_selection import cross_val_score

from build_features import build_feature_windows, load_sessions_and_events
from config import FEATURE_COLUMNS

# 실험할 (WINDOW_SIZE, STRIDE) 조합들
# TODO: 실 데이터 특성 보고 범위 재조정 필요
CANDIDATES = [
    (5, 2),
    (5, 5),
    (10, 5),   # 지금까지 써온 기본값
    (10, 10),
    (15, 5),
    (20, 10),
]


def evaluate_combo(window_size, stride, sessions_by_id, events_by_session, space_range, tsession_range):
    rows = build_feature_windows(
        events_by_session, sessions_by_id, space_range, tsession_range,
        window_size=window_size, stride=stride,
    )
    df = pd.DataFrame(rows)

    n_windows = len(df)
    if n_windows < 10:
        return {
            "window_size": window_size, "stride": stride, "n_windows": n_windows,
            "positive": 0, "negative": 0, "neutral": 0, "cv_accuracy": None,
            "note": "윈도우 수 부족으로 평가 생략",
        }

    label_counts = df["window_label"].value_counts()

    X = df[FEATURE_COLUMNS]
    y = df["window_label"]

    # 클래스가 2개 미만이면 학습 자체가 의미 없음
    if y.nunique() < 2:
        return {
            "window_size": window_size, "stride": stride, "n_windows": n_windows,
            "positive": label_counts.get("Positive", 0),
            "negative": label_counts.get("Negative", 0),
            "neutral": label_counts.get("Neutral", 0),
            "cv_accuracy": None, "note": "클래스 1개뿐 -> 평가 불가",
        }

    model = RandomForestClassifier(n_estimators=100, max_depth=5, random_state=42)
    # 데이터가 적으니 fold 수를 안전하게 최소 클래스 표본 수 이내로 제한
    n_folds = min(5, label_counts.min())
    n_folds = max(n_folds, 2)  # 최소 2-fold는 되게

    try:
        scores = cross_val_score(model, X, y, cv=n_folds)
        cv_accuracy = round(scores.mean(), 4)
        note = f"{n_folds}-fold CV"
    except ValueError as e:
        cv_accuracy = None
        note = f"CV 실패: {e}"

    return {
        "window_size": window_size, "stride": stride, "n_windows": n_windows,
        "positive": label_counts.get("Positive", 0),
        "negative": label_counts.get("Negative", 0),
        "neutral": label_counts.get("Neutral", 0),
        "cv_accuracy": cv_accuracy, "note": note,
    }


def main():
    sessions_by_id, events_by_session, space_range, tsession_range = load_sessions_and_events()

    print("=== 하이퍼파라미터(W, S) 조합 비교 ===\n")
    results = []
    for window_size, stride in CANDIDATES:
        result = evaluate_combo(window_size, stride, sessions_by_id, events_by_session,
                                 space_range, tsession_range)
        results.append(result)

    result_df = pd.DataFrame(results)
    print(result_df.to_string(index=False))

    print("\n=== 해석 가이드 ===")
    print("- n_windows가 너무 적으면(수십 개 이하) 다음 단계에서 데이터 부족 위험")
    print("- negative가 0에 가까우면 그 조합에서는 Negative 케이스를 거의 못 잡아냄")
    print("- cv_accuracy는 참고용 (라벨이 feature로부터 파생돼서 순환적 성격 있음)")


if __name__ == "__main__":
    main()
