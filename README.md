# Adaptive Passthrough Framework for Immersive XR

몰입형 XR을 위한 상황 인식 기반 Adaptive Passthrough Framework — 정적/동적 위험 분석과 ML 개인화를 결합한 Meta Quest 3 안전 시스템

Context-aware Adaptive Passthrough Framework for Immersive XR combining static/dynamic risk analysis with ML-based personalization for Meta Quest 3

---

## 프로젝트 소개

Meta Quest Guardian은 경계를 넘으면 Passthrough를 전부 켜는 단순한 On/Off 방식입니다. 이 프로젝트는 그 대신 **정적 경계(벽·바닥 장애물)**, **동적 객체(사람)**, **사용자 움직임 상태**를 매 프레임 독립적으로 위험도로 계산하고, 위험이 실제로 있는 방향에만 선택적으로 Passthrough 창을 여는 프로토타입입니다. 세션 로그를 학습한 ML 모델이 사용자별로 그 판단 임계값을 점진적으로 조정합니다.

지금 저장소에는 이 시스템이 실제로 동작하는 **Unity/C# Quest 3 앱**과, 그 앱이 쓰는 **개인화·동적 위험 계산을 학습·검증하는 Python 파이프라인**이 들어 있습니다. FastAPI 백엔드/PostgreSQL/React 대시보드는 초기 5-레이어 계획에 있었지만 실제로 구현되지 않아 저장소에서 제거했습니다 — 아래 [구현 현황](#구현-현황) 참고.

## 팀원 및 역할

| 이름 | 담당 |
|---|---|
| 따다소 (팀장) | 로그 수집 및 피드백 전략, ML 기반 개인화 모델 구현 및 경량화, 사용자 테스트 진행 및 결과 분석 |
| 최아영 | 센서 데이터 수집 및 Feature 알고리즘 개발, 위험도 계산 알고리즘 구현 |
| 이승주 | 전체 시스템 레이어 구조 구축, FastAPI 백엔드/PostgreSQL 연동, Dashboard 구현 |

> 이승주 담당의 백엔드/PostgreSQL/Dashboard는 계획된 역할이며, 이 저장소 기준으로는 아직 코드가 존재하지 않습니다. 현재 세션 로그는 Quest 기기 로컬(`Application.persistentDataPath/RiskLogs/*.jsonl`)에만 남습니다.

## 리포지토리 구조

```text
.
├─ unity-client/        Quest 3에서 실제로 빌드·실행되는 앱 (Unity 6, C#) — 이 저장소의 핵심 구현체
├─ ml-dynamic-object/    동적 객체(사람) 위험도 파이프라인의 Python 참조 구현 + 테스트 하네스
├─ ml-personalization/  세션 로그 → 7차원 feature → RandomForest → ONNX 개인화 임계값 학습 파이프라인
└─ docs/                 설계·구현 스펙 문서, 중간보고서
```

## 아키텍처 한눈에 보기

```text
[Quest 센서]
 ├─ HMD/컨트롤러 Transform, Scene API 벽 정보
 │    └─▶ QuestRiskExperimentLogger ──▶ 정적 경계 Feature + 사용자 움직임 상태
 ├─ Passthrough 카메라 프레임
 │    └─▶ QuestPersonDetectionRunner (YOLO × Unity Inference Engine) ──▶ 사람 bounding box
 └─ EnvironmentRaycastManager / Room Scene
      └─▶ QuestSpatialObstacleProvider ──▶ 머리·양손·저상해물 공간 위험 측정

[Core 위험도 계산]  (TeamVR.AdaptivePassthrough.Core — 순수 C#, EditMode 유닛테스트 가능)
 ├─ StaticBoundaryRiskMath + LegacyStaticBoundaryPolicy
 │      거리·TTC·접근속도·사각지대 → 머리/양손/저장애물별 정적 위험 + on/off 히스테리시스 + 0.25m 비상 오버라이드
 ├─ DynamicRiskPipeline (SimpleObjectTracker → HistoryMotionEstimator/MetricMotionEstimator
 │      → RelativeLocationEstimator → DynamicRiskEstimator)
 │      사람별 추적·접근/이탈 판정·TTC → 동적 위험 + "초근접 시 트래커 확정 전에도 강제 노출" 안전장치
 └─ RiskSnapshotBuilder + OverallRiskFusion
        정적/사용자상태/동적/의도 위험을 하나로 합친 RiskSnapshot — 실시간 표시 결정에는 쓰이지 않고
        로깅·개인화 feature 계산용으로만 사용 (2026-07 "정적·동적 재분리" 이후 구조)

[정책/표시 계층]  (Assets/Scripts 최상위, MonoBehaviour)
 ├─ StaticPassthroughPolicyController   정적 위험 → on/off (독립적으로 판단)
 ├─ DynamicPassthroughPolicyController  동적 위험 → on/off (독립적으로 판단)
 ├─ SelectivePassthroughController      두 결정을 받아 실제 셰이더 기반 "Passthrough 창"을 렌더링
 │                                       (사람 캡슐, 벽/저장애물 평면, 최대 2~3개 슬롯 교체 히스테리시스, 후방 경고)
 ├─ BoundaryVisibilityController        Guardian(OVRManager) 경계 표시 여부 조정
 └─ SafetyAlertFeedbackController       대체 피드백 모드(빨간 테두리 + 컨트롤러 햅틱)

[ML 개인화]
 └─ PersonalizationRuntimeController
        Passthrough 활성화 이벤트 수집(빈도/평균 지속시간/평균·최대 머리 속도/공간 크기/세션 경과)
        → 7차원 feature → onnx(Unity Sentis) 추론 → "이 활성화가 불필요했을 확률"
        → stable/rapid/hand/dynamic on·off threshold 조정 → 위 두 정책 컨트롤러에 적용
        (기본은 shadow mode로 로그만 남기고, cold-start 세션 수 미달 시 기본값 유지)

[실험 게임 하네스]  (Assets/Scripts/Experiment)
 └─ ExperimentGameRoot + PassthroughConditionSwitcher
        위 파이프라인은 그대로 두고 presentation/boundary override만 바꿔
        3-조건(Guardian 기본값 / 정적만 / 정적+동적) 사용자 연구를 진행
```

## 구성 요소별 설명

### 1. 정적 경계 위험 (Static Boundary Risk)
`QuestSpatialObstacleProvider`가 머리·양손·이동 경로에서 최대 24개의 레이캐스트를 `EnvironmentRaycastManager`/Room Scene에 쏘고, 강건한 평면 추정(RANSAC 유사)으로 벽·저장애물 방향과 형태를 복원합니다. `StaticBoundaryRiskMath`가 거리·TTC·접근속도·시선-경계 각도(사각지대)를 가중합해 위험도를 내고, 사용자 상태(정지/이동/격한 움직임)에 따라 on/off 임계값이 Lerp로 움직이는 히스테리시스가 걸립니다. 0.25m 이내로 접근하면 임계값과 무관하게 즉시 비상 노출됩니다.

### 2. 동적 객체 위험 (Dynamic Object Risk)
`QuestPersonDetectionRunner`가 Passthrough 카메라 프레임을 Unity Inference Engine(Sentis)으로 돌려 YOLO 계열 모델로 사람을 검출합니다(NCHW 입력, boxes/classIds/scores 3-출력 계약). `DynamicRiskPipeline`이 이를 추적(`SimpleObjectTracker`)하고, bbox 크기 변화와 깊이(있으면 `QuestPersonDepthProvider`의 안전거리 측정)를 함께 써서 접근/정지/이탈 상태와 TTC를 추정합니다. 안전거리 1m 이내가 확인되거나 bbox가 화면을 강하게 채우면, 트래커가 아직 신뢰 확정을 못했어도 즉시 위험을 강제 노출하는 안전장치(`ForcePassthrough`)가 있습니다.

### 3. 융합과 표시 결정
과거에는 `Rtotal = w_c R_c + w_s R_s + w_d R_d + w_i R_i` 하나의 가중합으로 Passthrough를 켰지만(`unity-client/README.md`에 남아있는 초기 프로토타입 설명이 이 구조), 2026-07 이후 정적과 동적 판단이 `StaticPassthroughPolicyController`/`DynamicPassthroughPolicyController`로 완전히 분리되어 각자 자신의 히스테리시스로 독립적으로 on/off를 결정합니다. `RiskSnapshotBuilder`가 만드는 통합 `RiskSnapshot`은 지금은 로깅과 개인화 feature 계산에만 쓰입니다. 두 정책의 결과는 `SelectivePassthroughController`가 받아, 위험이 있는 실제 방향/사람 영역에만 셰이더 기반 창을 열어 그 부분만 카메라 패스스루로 보여줍니다.

### 4. Quest 런타임 성능 관리
사람 검출 추론은 프레임을 멈추지 않도록 레이어 단위로 여러 프레임에 걸쳐 슬라이스 실행되며(`InferenceSchedulerPolicy`), 0.75초를 넘기면 워치독이 강제로 재시작합니다. 기기에서 CPU/GPU 백엔드를 실측 벤치마크해 더 빠른 쪽을 자동 선택하고(`InferenceBackendSelector`), `TrackingQualityController`가 성능 프로파일에 따라 추론 주기·레이캐스트 개수를 조절합니다.

### 5. ML 개인화
`PersonalizationRuntimeController`는 Passthrough가 실제로 켜져 있던 구간을 활성화 이벤트로 모아 활성화 빈도, 평균 지속시간, 평균/최대 머리 속도, 공간 크기, 세션 경과시간의 7차원 feature를 만들고, `ml-personalization/`에서 학습한 RandomForest를 ONNX로 변환해 Sentis로 온디바이스 추론합니다. 모델이 내는 "이 활성화가 불필요했을 확률(Neutral≈Negative)"이 높을수록 on 임계값들을 보수적으로 올립니다. 안전을 위해 기본은 shadow mode(로그만 남김)이고, 누적 세션 수가 부족하면(cold start) 개인화를 적용하지 않고 기본값을 유지합니다.

### 6. 실험 게임 하네스 (사용자 연구용)
`docs/EXPERIMENT_GAME_INTEGRATION.md`에 따라, 위 파이프라인이 켜져 있는 동일한 `SampleScene`에 발사체를 쏘는 게임(`ExperimentGameRoot`)을 얹어 참가자가 3개 라운드를 진행합니다.

| 라운드 | 조건(`ExperimentCondition`) | 커스텀 안전 출력 | Guardian |
|---|---|---|---|
| 1 | `GuardianDefault` | 억제 | 표시 |
| 2 | `StaticOnly` | 정적(머리/양손/저장애물)만 표시 | 숨김 |
| 3 | `StaticAndDynamic` | 정적+동적(사람) 모두 표시 | 숨김 |

`PassthroughConditionSwitcher`는 실제 컴포넌트의 `enabled`나 PlayerPrefs를 바꾸지 않고, presentation/boundary override만 전환합니다. 라운드 종료·메뉴 복귀 시 사용자의 기본 설정으로 자동 복원됩니다. 로그는 기존 `schemaVersion=2` JSONL 형식에 condition/round 정보를 얹어서 그대로 남습니다.

## 시작하기

### Unity 클라이언트 (Quest 3)

1. Unity Hub에서 **6000.4.2f1**(Unity 6)로 `unity-client/` 폴더를 엽니다.
2. Android Build Support 모듈 설치, Android 플랫폼으로 전환.
3. Quest 헤드셋에서 개발자 모드 활성화 + Space Setup(Room Setup) 완료.
4. 실행할 씬 선택:
   - `Assets/Scenes/SampleScene.unity` — 실제 앱(정적/동적 위험도 + 개인화 + 실험 게임 포함), 빌드에 포함되는 유일한 씬.
   - `Assets/Scenes/DynamicRiskMock.unity` — 동적 위험도만 목(mock) 데이터로 검증하는 씬.
   - `Assets/Scenes/ExperimentGameTest.unity` — 실험 게임 원본 배치 참고용, 빌드 미포함.
5. Build & Run, 또는 이미 빌드된 `Builds/Android/AdaptivePassthrough.apk`가 있다면 Quest를 USB로 연결한 뒤 `unity-client/Tools/Install-Quest3.cmd`로 덮어쓰기 설치.
6. 실험 게임 Prefab을 다시 생성해야 하면 Unity 메뉴 `Tools > Experiment > Rebuild Integrated Game Prefab` 사용 (`Assets/Editor/ExperimentGameIntegrationBuilder.cs`).

### 동적 객체 위험도 Python MVP (`ml-dynamic-object/`)

```powershell
cd ml-dynamic-object
python -m pip install -e ".[dev,camera]"
python examples/run_mock_demo.py           # 하드웨어 없이 mock 시나리오
python examples/run_usb_camera.py --camera 0 --preview   # 실제 USB 카메라
python -m pytest
```

### ML 개인화 파이프라인 (`ml-personalization/`)

```bash
pip install -r requirements.txt
cd src
python generate_mock_logs.py      # mock 세션/이벤트 로그 생성
python build_features.py          # 라벨링 + 7차원 feature 추출
python train_model.py             # RandomForest 학습
python convert_to_onnx.py         # ONNX 변환 + sklearn 대비 검증
python cold_start.py              # cold-start 게이트 확인
python personalize.py             # 임계값 매핑 수식 확인
python test_personalization.py    # 전체 파이프라인 통합 테스트
```

실제 Quest 로그(`RiskLogs/*.jsonl`)로 개인화 모델을 만드는 절차는 `ml-personalization/README.md`의 "실제 로그 연동 절차"를 참고하세요.

## 문서

더 자세한 설계·구현 배경은 `docs/`에 있습니다.

- [Quest 3 카메라 기반 사람 Bounding Box Unity 앱 제작 가이드](docs/QUEST3_PERSON_BBOX_UNITY_GUIDE.md)
- [동적 객체 인식·위험도 Unity 구현 및 검증 문서](docs/DYNAMIC_RISK_UNITY_IMPLEMENTATION.md)
- [Quest 3 사람 검출·사용자 상태·UI 안정화 제작문서](docs/QUEST3_DETECTION_STABILITY_UI_FIX_SPEC.md) / [구현 결과](docs/QUEST3_DETECTION_STABILITY_UI_FIX_IMPLEMENTATION.md)
- [단일 RiskSnapshot 기반 위험도 통합 제작문서](docs/RISK_SNAPSHOT_INTEGRATION_SPEC.md)
- [정적·동적 위험도 재분리 제작문서](docs/STATIC_DYNAMIC_RISK_SEPARATION_SPEC.md)
- [Quest 3 Depth 기반 동적 위험·인물 Passthrough 개선 제작문서](docs/QUEST3_DEPTH_AWARE_DYNAMIC_RISK_PASSTHROUGH_SPEC.md)
- [공간 측정·사람 추적 품질 개선 구현 및 검증](docs/SPATIAL_TRACKING_QUALITY_IMPLEMENTATION.md)
- [온디바이스(Sentis) ML 개인화 통합 스펙](docs/ONDEVICE_SENTIS_INTEGRATION.md)
- [실험 게임 통합](docs/EXPERIMENT_GAME_INTEGRATION.md)
- [착수보고서](docs/착수보고서.pdf) / [2026 중간보고서](docs/2026중간보고서_08_TeamVR_몰입형_XR을_위한_상황_인식_기반_Adaptive_Passthrough_Framework.pdf)

전체 목록은 [`docs/README.md`](docs/README.md)에서도 볼 수 있습니다(일부 최신 문서는 아직 그 인덱스에 반영되지 않았습니다).

## 구현 현황

| 항목 | 상태 |
|---|---|
| Quest 정적 경계 위험도 + Passthrough 표시 | 구현 완료, Quest 실기 검증 진행 중 |
| Quest 동적(사람) 위험도 + Passthrough 표시 | 구현 완료 (YOLO + Sentis 온디바이스 추론) |
| 정적/동적 독립 정책 + 통합 표시 레이어 | 구현 완료 |
| ML 개인화 (RandomForest → ONNX → Sentis) | mock 데이터로 end-to-end 검증 완료, 실제 Quest 로그 재검증은 TODO |
| 실험 게임(3-조건 사용자 연구 하네스) | 구현 완료, Quest 실기 최종 부하/FPS 검증 TODO |
| 의도 위험(`R_intent`) | 미구현 (항상 0, 구조만 예약) |
| Backend(FastAPI) / PostgreSQL / Dashboard(React) | 미구현 — 로그는 현재 Quest 로컬 JSONL 파일로만 남음 |

## 비교 평가

실험 게임의 3개 라운드(Guardian 기본값 / 정적 전용 / 정적+동적 = Proposed Framework)를 대상으로 Collision Rate, Reaction Time, Presence Score, User Preference, Cognitive Load 지표를 기준으로 비교 평가합니다.
