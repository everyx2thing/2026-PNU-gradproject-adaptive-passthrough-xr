// PersonalizationSentisRunner.cs (스켈레톤 — Sentis API 세부는 설치된 버전에 맞춰 조정 필요)
//
// 역할: 7차원 feature 벡터를 만들고, ONNX 모델(rf_personalization_real.onnx)로
//       Sentis 추론을 돌려서 Negative 확률을 얻은 뒤, 개인화된 Passthrough ON 임계값을 계산한다.
//
// 2026-08-06: QuestRiskExperimentLogger.cs(feature/static-boundary-passthrough 브랜치, 아직
// main 미머지)가 "Rtotal 가중합 + 단일 tau_viz" 구조를 "R_static_head/R_static_hand 두 경로 +
// UserState로 on 임계값을 Lerp 조절"하는 구조로 바꿔서, 이 스크립트의 출력도 예전 4개 가중치
// (wCollision 등) 대신 그쪽이 실제로 쓰는 stableOnThreshold/rapidOnThreshold/handFullThreshold를
// 그대로 조정하는 방식으로 바꿈. 이 필드들은 QuestRiskExperimentLogger.cs에서 지금 private
// [SerializeField]라서, 개인화 값을 실제로 주입하려면 그쪽에 public setter(또는 프로퍼티)를
// 추가하는 작업이 아직 필요함 — 그건 이 스크립트가 아니라 QuestRiskExperimentLogger.cs 쪽 작업.
//
// 사용 전 준비:
// 1. rf_personalization_real.onnx 파일을 Assets/Models/ 에 넣기
// 2. Package Manager에서 com.unity.sentis 패키지 설치
// 3. Inspector에서 이 스크립트의 modelAsset 필드에 위 onnx 파일 드래그
// 4. onnx 파일과 함께 전달받은 "학습된 클래스 순서 (model.classes_)" 로그를 보고
//    Inspector의 negativeClassIndex를 "Negative"의 위치로 설정 (기본값 -1은 일부러 미설정 상태)

using System;
using UnityEngine;
using Unity.Sentis; // 설치된 Sentis 버전에 따라 네임스페이스/API가 다를 수 있음 (TODO: 버전 확인)

namespace TeamVR.AdaptivePassthrough
{
    [Serializable]
    public struct PersonalizedParams
    {
        public float stableOnThreshold;
        public float rapidOnThreshold;
        public float handFullThreshold;
        public float pNegative;
        public bool isColdStart;
    }

    public sealed class PersonalizationSentisRunner : MonoBehaviour
    {
        // ===== 기본값 (cold-start 구간). QuestRiskExperimentLogger.cs의 [SerializeField]
        // 기본값(stableOnThreshold=0.65, rapidOnThreshold=0.45, handFullThreshold=0.85)과
        // ml-personalization/src/config.py의 THRESHOLD_DEFAULT를 항상 동일하게 맞출 것 =====
        private const float STABLE_ON_DEFAULT = 0.65f;
        private const float RAPID_ON_DEFAULT = 0.45f;
        private const float HAND_FULL_DEFAULT = 0.85f;

        private const float ADJUSTMENT_SCALE = 0.2f; // TODO: 임의값, 사용자 테스트로 튜닝
        private const float MAX_THRESHOLD = 0.95f;   // 안전장치: 완전히 둔감해지지 않도록 상한
        private const int N_MIN_SESSIONS = 5;

        // ===== feature 정규화 기준 (TODO: 실제 Scene API 값 범위 확인 후 조정) =====
        private const float SPACE_MIN_M2 = 4.0f;
        private const float SPACE_MAX_M2 = 8.0f;
        private const float SESSION_MAX_SECONDS = 30f * 60f;

        [SerializeField] private ModelAsset modelAsset; // Assets/Models/rf_personalization_real.onnx

        // 모델을 convert_to_onnx.py로 export할 때 콘솔에 찍히는
        // "학습된 클래스 순서 (model.classes_): [...]" 로그를 보고 여기 인덱스를 맞출 것.
        // 표본이 적으면 "Negative" 클래스 자체가 학습 데이터에서 빠질 수 있어서(실제로 dry-run
        // 검증 중 발생함), 모델 파일을 교체할 때마다 반드시 다시 확인해야 함. 이 값이 실제 클래스
        // 순서와 다르면 p_negative가 엉뚱한 클래스의 확률을 가리키게 되어 조용히 틀린 값을 냄.
        [SerializeField] private int negativeClassIndex = -1; // -1 = 아직 미확인 (일부러 기본값을 안전하지 않게 둠)

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
        ///
        /// convert_to_onnx.py가 zipmap=False로 변환하기 때문에 출력은 두 개:
        /// "output_label"(문자열 클래스, 안 씀), "output_probability"(shape (1, n_classes)
        /// 의 순수 float 텐서). ZipMap(dict) 출력이 아님 — Sentis는 시퀀스/맵 타입을 못 읽어서
        /// 일부러 이렇게 변환해뒀음 (docs/ONDEVICE_SENTIS_INTEGRATION.md 5번 항목 참고).
        /// </summary>
        public float RunInference(float[] featureVector)
        {
            if (negativeClassIndex < 0)
            {
                throw new InvalidOperationException(
                    "negativeClassIndex가 설정되지 않았습니다. convert_to_onnx.py 실행 로그의 " +
                    "'학습된 클래스 순서 (model.classes_)'를 보고 Inspector에서 값을 지정하세요. " +
                    "(모델 파일을 바꿀 때마다 다시 확인 필요)");
            }

            using var inputTensor = new TensorFloat(new TensorShape(1, featureVector.Length), featureVector);
            worker.Execute(inputTensor);

            using var probabilityOutput = worker.PeekOutput("output_probability") as TensorFloat;
            probabilityOutput.MakeReadable();

            return probabilityOutput[negativeClassIndex];
        }

        /// <summary>
        /// cold-start 게이트 + personalize.py 임계값 매핑 로직을 그대로 이식.
        /// p_negative(최근 활성화 중 "괜히 켜진" 비율)가 높을수록 세 임계값을 모두 올려서
        /// Passthrough가 더 확실한 위험 상황에서만 켜지도록 만듦 (둔감해짐).
        /// </summary>
        public PersonalizedParams GetPersonalizedParams(int accumulatedSessionCount, float[] featureVector)
        {
            if (accumulatedSessionCount < N_MIN_SESSIONS)
            {
                return new PersonalizedParams
                {
                    stableOnThreshold = STABLE_ON_DEFAULT,
                    rapidOnThreshold = RAPID_ON_DEFAULT,
                    handFullThreshold = HAND_FULL_DEFAULT,
                    pNegative = 0f,
                    isColdStart = true,
                };
            }

            float pNegative = RunInference(featureVector);
            float delta = Mathf.Max((pNegative - 0.5f) * ADJUSTMENT_SCALE, 0f);

            return new PersonalizedParams
            {
                stableOnThreshold = Mathf.Min(STABLE_ON_DEFAULT + delta, MAX_THRESHOLD),
                rapidOnThreshold = Mathf.Min(RAPID_ON_DEFAULT + delta, MAX_THRESHOLD),
                handFullThreshold = Mathf.Min(HAND_FULL_DEFAULT + delta, MAX_THRESHOLD),
                pNegative = pNegative,
                isColdStart = false,
            };
        }
    }
}
