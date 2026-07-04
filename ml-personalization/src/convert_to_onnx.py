"""
ONNX 변환 스크립트 (3.4.4절 경량화·배포 파이프라인 1단계)

입력: models/rf_personalization.joblib (train_model.py 결과물)
출력: models/rf_personalization.onnx

변환 후 원본 sklearn 모델과 ONNX 모델의 예측 결과가 일치하는지 검증까지 합니다.
(변환 과정에서 미묘하게 예측이 달라지는 경우가 있어서, 이 검증 없이 넘어가면 안 됨)

Unity Sentis 로드 테스트는 Unity 프로젝트가 준비되면 별도로 진행 (TODO).
지금 이 스크립트는 "서버에서 변환 → 파일로 저장"까지만 검증합니다.
"""

import os
import joblib
import numpy as np
import onnxruntime as rt
from skl2onnx import convert_sklearn
from skl2onnx.common.data_types import FloatTensorType

MODEL_DIR = os.path.join(os.path.dirname(__file__), "..", "models")
DATA_DIR = os.path.join(os.path.dirname(__file__), "..", "data")

FEATURE_COLUMNS = [
    "f_pt", "r_cancel", "t_pt_bar", "v_h_bar", "v_h_max",
    "A_space_norm", "T_session_norm",
]

TARGET_MAX_SIZE_MB = 50  # 3.4.4절 목표 모델 크기


def main():
    model_path = os.path.join(MODEL_DIR, "rf_personalization.joblib")
    onnx_path = os.path.join(MODEL_DIR, "rf_personalization.onnx")

    print(f"모델 로드: {model_path}")
    model = joblib.load(model_path)

    # ONNX 변환 (입력: 7차원 float 벡터, 배치 크기는 가변 None)
    initial_type = [("float_input", FloatTensorType([None, len(FEATURE_COLUMNS)]))]
    onnx_model = convert_sklearn(model, initial_types=initial_type)

    with open(onnx_path, "wb") as f:
        f.write(onnx_model.SerializeToString())

    size_mb = os.path.getsize(onnx_path) / (1024 * 1024)
    print(f"\nONNX 변환 완료: {onnx_path}")
    print(f"모델 크기: {size_mb:.4f} MB (목표: {TARGET_MAX_SIZE_MB}MB 이하)")
    if size_mb > TARGET_MAX_SIZE_MB:
        print("⚠️ 목표 크기 초과! n_estimators/max_depth 축소 검토 필요")
    else:
        print("✅ 목표 크기 이내")

    # ===== 검증: sklearn 예측 vs ONNX 예측 비교 =====
    print("\n=== 변환 검증 (sklearn vs ONNX 예측 비교) ===")

    import pandas as pd
    df = pd.read_csv(os.path.join(DATA_DIR, "mock_features.csv"))
    X_sample = df[FEATURE_COLUMNS].values.astype(np.float32)[:10]  # 샘플 10개만 비교

    # sklearn 예측
    sklearn_pred = model.predict(X_sample)

    # ONNX 예측
    sess = rt.InferenceSession(onnx_path, providers=["CPUExecutionProvider"])
    input_name = sess.get_inputs()[0].name
    label_name = sess.get_outputs()[0].name
    onnx_pred = sess.run([label_name], {input_name: X_sample})[0]

    match_count = sum(1 for a, b in zip(sklearn_pred, onnx_pred) if a == b)
    print(f"샘플 {len(X_sample)}개 중 {match_count}개 일치")

    for i, (a, b) in enumerate(zip(sklearn_pred, onnx_pred)):
        status = "OK" if a == b else "MISMATCH"
        print(f"  [{i}] sklearn={a}, onnx={b}  {status}")

    if match_count == len(X_sample):
        print("\n✅ 변환 검증 통과 — sklearn과 ONNX 예측이 완전히 일치합니다")
    else:
        print("\n⚠️ 예측 불일치 발견 — 변환 과정 재점검 필요")


if __name__ == "__main__":
    main()
