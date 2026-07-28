// PersonalizationSentisRunner.cs (스켈레톤 — Sentis API 세부는 설치된 버전에 맞춰 조정 필요)
//
// 역할: 7차원 feature 벡터를 만들고, ONNX 모델(rf_personalization_real.onnx)로
//       Sentis 추론을 돌려서 Negative 확률을 얻은 뒤, 개인화된 가중치/임계값을 계산한다.
//
// 사용 전 준비:
// 1. rf_personalization_real.onnx 파일을 Assets/Models/ 에 넣기
// 2. Package Manager에서 com.unity.sentis 패키지 설치
// 3. Inspector에서 이 스크립트의 modelAsset 필드에 위 onnx 파일 드래그

using System;
using UnityEngine;
using Unity.Sentis; // 설치된 Sentis 버전에 따라 네임스페이스/API가 다를 수 있음 (TODO: 버전 확인)

namespace TeamVR.AdaptivePassthrough
{
    [Serializable]
    public struct PersonalizedParams
    {
        public float wCollision;
        public float wState;
        public float wDynamic;
        public float wIntent;
        public float tauViz;
        public float pNegative;
        public bool isColdStart;
    }

    public sealed class PersonalizationSentisRunner : MonoBehaviour
    {
        // ===== 기본값 (cold-start 구간, personalize.py의 W_DEFAULT/TAU_VIZ_DEFAULT와 동일) =====
        private const float W_C_DEFAULT = 0.30f;
        private const float W_S_DEFAULT = 0.20f;
        private const float W_D_DEFAULT = 0.30f;
        private const float W_I_DEFAULT = 0.20f;
        private const float TAU_VIZ_DEFAULT = 0.5f;

        private const float ADJUSTMENT_SCALE = 0.2f; // TODO: 임의값, 사용자 테스트로 튜닝
        private const float MIN_WEIGHT = 0.05f;
        private const int N_MIN_SESSIONS = 5;

        // ===== feature 정규화 기준 (TODO: 실제 Scene API 값 범위 확인 후 조정) =====
        private const float SPACE_MIN_M2 = 4.0f;
        private const float SPACE_MAX_M2 = 8.0f;
        private const float SESSION_MAX_SECONDS = 30f * 60f;

        [SerializeField] private ModelAsset modelAsset; // Assets/Models/rf_personalization_real.onnx
        private Model runtimeModel;
        private IWorker worker; // Sentis 버전에 따라 IWorker 대신 Worker일 수 있음 (TODO)

        private void Awake()
        {
            runtimeModel = ModelLoader.Load(modelAsset);
            worker = WorkerFactory.CreateWorker(BackendType.CPU, runtimeModel);
        }

        private void OnDestroy()
        {
            worker?.Dispose();
        }

        /// <summary>
        /// 최근 활성화 이벤트 통계 + 현재 세션 상태로 7차원 feature 벡터를 만든다.
        /// 순서는 docs/ONDEVICE_SENTIS_INTEGRATION.md 표와 반드시 일치해야 함.
        /// </summary>
        public float[] BuildFeatureVector(
            int recentActivationCount,
            int windowSize,
            float meanDurationSeconds,
            float meanHeadSpeed,
            float maxHeadSpeed,
            float spaceAreaM2,
            float sessionElapsedSeconds)
        {
            float fPt = windowSize > 0 ? (float)recentActivationCount / windowSize : 0f;
            const float rCancel = 0f; // 수동 제어 미구현 -> 항상 0 (학습 때와 동일하게 유지)

            float spaceNorm = Mathf.InverseLerp(SPACE_MIN_M2, SPACE_MAX_M2, spaceAreaM2);
            float sessionNorm = Mathf.Clamp01(sessionElapsedSeconds / SESSION_MAX_SECONDS);

            return new float[]
            {
                fPt,
                rCancel,
                meanDurationSeconds,
                meanHeadSpeed,
                maxHeadSpeed,
                spaceNorm,
                sessionNorm,
            };
        }

        /// <summary>
        /// feature 벡터로 Sentis 추론을 돌려서 Negative 확률을 반환한다.
        /// TODO: 실제 Sentis 출력 텐서 이름/순서는 모델 export 결과 보고 맞춰야 함
        /// (convert_to_onnx.py 쪽 classes_ 순서: Negative/Neutral/Positive 알파벳 순일 가능성 높음,
        ///  하지만 반드시 실제 출력 확인 후 인덱스 고정할 것)
        /// </summary>
        public float RunInference(float[] featureVector)
        {
            using var inputTensor = new TensorFloat(new TensorShape(1, featureVector.Length), featureVector);
            worker.Execute(inputTensor);

            // TODO: 출력 텐서 이름 확인 필요. convert_to_onnx.py 검증 결과를 보면
            // 출력이 2개(class label, probability map) 나오는 구조였음.
            // 확률 출력에서 "Negative" 클래스에 해당하는 값을 뽑아야 함.
            using var probabilityOutput = worker.PeekOutput("probabilities") as TensorFloat;
            probabilityOutput.MakeReadable();

            int negativeClassIndex = 0; // TODO: 실제 클래스 순서 확인 후 수정
            return probabilityOutput[negativeClassIndex];
        }

        /// <summary>
        /// cold-start 게이트 + personalize.py 가중치 매핑 로직을 그대로 이식.
        /// </summary>
        public PersonalizedParams GetPersonalizedParams(int accumulatedSessionCount, float[] featureVector)
        {
            if (accumulatedSessionCount < N_MIN_SESSIONS)
            {
                return new PersonalizedParams
                {
                    wCollision = W_C_DEFAULT,
                    wState = W_S_DEFAULT,
                    wDynamic = W_D_DEFAULT,
                    wIntent = W_I_DEFAULT,
                    tauViz = TAU_VIZ_DEFAULT,
                    pNegative = 0f,
                    isColdStart = true,
                };
            }

            float pNegative = RunInference(featureVector);
            float delta = Mathf.Max((pNegative - 0.5f) * ADJUSTMENT_SCALE, 0f);

            float wC = Mathf.Max(W_C_DEFAULT - delta, MIN_WEIGHT);
            float actualReduction = W_C_DEFAULT - wC;

            float otherTotal = W_S_DEFAULT + W_D_DEFAULT + W_I_DEFAULT;
            float wS = W_S_DEFAULT + actualReduction * (W_S_DEFAULT / otherTotal);
            float wD = W_D_DEFAULT + actualReduction * (W_D_DEFAULT / otherTotal);
            float wI = W_I_DEFAULT + actualReduction * (W_I_DEFAULT / otherTotal);

            float tauViz = Mathf.Min(TAU_VIZ_DEFAULT + delta, 1.0f);

            return new PersonalizedParams
            {
                wCollision = wC,
                wState = wS,
                wDynamic = wD,
                wIntent = wI,
                tauViz = tauViz,
                pNegative = pNegative,
                isColdStart = false,
            };
        }
    }
}
