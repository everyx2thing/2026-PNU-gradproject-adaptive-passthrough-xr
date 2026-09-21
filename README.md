# Adaptive Passthrough Framework for Immersive XR

몰입형 XR을 위한 상황 인식 기반 Adaptive Passthrough Framework — 정적/동적 위험 분석과 ML 개인화를 결합한 Meta Quest 3 안전 시스템

Context-aware Adaptive Passthrough Framework for Immersive XR combining static/dynamic risk analysis with ML-based personalization for Meta Quest 3

> Team VR · 부산대학교 졸업과제 (2026.05–2026.09) 

---

## 프로젝트 소개

Meta Quest Guardian은 사전에 설정한 경계선에 근접하면 경계 그리드를 표시하는 단순한 방식이며, 모든 사용자에게 동일한 임계값을 적용하고 사람처럼 스스로 움직이는 동적 위험은 인식하지 못한다는 한계가 있습니다. 이 프로젝트는 그 대신 **정적 경계(벽·바닥 장애물)**, **동적 객체(사람)**, **사용자 움직임 상태**를 매 프레임 독립적으로 위험도로 계산하고, 위험이 실제로 있는 방향에만 선택적으로 Passthrough 창을 여는 프로토타입입니다. 세션 로그를 학습한 ML 모델이 사용자별로 그 판단 임계값을 점진적으로(항상 상향으로만) 조정합니다.

지금 저장소에는 이 시스템이 실제로 동작하는 **Unity/C# Quest 3 앱**과, 그 앱이 쓰는 **개인화, 동적 위험 계산을 학습, 검증하는 Python 파이프라인**이 들어 있습니다. 

12명을 대상으로 한 사용자 실험 결과, 기존 Guardian 방식 대비 안전감이 통계적으로 유의하게 상승했으며 몰입감의 유의한 저하는 관찰되지 않았습니다 — 자세한 내용은 [사용자 실험 결과](#사용자-실험-결과) 참고.

## 팀원 및 역할

| 이름 | 담당 | 세부 내용 |
|---|---|---|
| 따다소 (팀장) | 개인화 모델 개발 및 사용자 실험 연구 진행 | Random Forest 기반 On-device 추론 파이프라인 구축, PC 기반 개인화 모델 재학습 파이프라인 구현·배포, 사용자 실험 설계·진행 및 비모수 통계 분석, 보고서·발표자료 작성 |
| 최아영 | 정적 경계 기반 위험도 알고리즘 및 실험용 게임 개발 | 정적 경계 기반 충돌 위험도 알고리즘 설계·구현, 정적 위험도에 따른 Passthrough 제어 로직 구현, 실험용 VR 게임 개발 및 조건별 위험도 시스템 연동 |
| 이승주 | Quest 3 기반 동적 위험 인식 및 안전 시스템 통합 | 카메라·깊이 정보 기반 사람 검출·추적 및 접근 위험도 계산 구현, 위험 위치에 따른 선택적 Passthrough 시각화·시각/진동 피드백 구현 및 안정화, 개인화 모델 및 실험용 게임과 안전 시스템 통합 |

## 리포지토리 구조

```text
.
├─ unity-client/        Quest 3에서 실제로 빌드·실행되는 앱 (Unity 6, C#) — 이 저장소의 핵심 구현체
├─ ml-dynamic-object/    동적 객체(사람) 위험도 파이프라인의 Python 참조 구현 + 테스트 하네스
├─ ml-personalization/  세션 로그 → Feature → RandomForest → ONNX 개인화 임계값 학습 파이프라인
└─ docs/                 설계·구현 스펙 문서, 중간·최종 보고서
```

## 아키텍처 한눈에 보기

```text
[Quest 센서]
 ├─ HMD/컨트롤러 Transform, Scene API 벽 정보
 │    └─▶ QuestRiskExperimentLogger ──▶ 정적 경계 Feature + 사용자 움직임 상태
 ├─ Passthrough 카메라 프레임
 │    └─▶ QuestPersonDetectionRunner (YOLOv9 × Unity Inference Engine) ──▶ 사람 bounding box
 └─ EnvironmentRaycastManager / Room Scene
      └─▶ QuestSpatialObstacleProvider ──▶ 머리·양손·저장애물 공간 위험 측정

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
        (기본은 shadow mode로 로그만 남기고, cold-start 세션 수(5회) 미달 시 기본값 유지)

[실험 게임 하네스]  (Assets/Scripts/Experiment)
 └─ ExperimentGameRoot + PassthroughConditionSwitcher
        위 파이프라인은 그대로 두고 presentation/boundary override만 바꿔
        3-조건(Guardian 기본값 / 정적만 / 정적+동적) 사용자 연구를 진행
```

## 구성 요소별 설명

### 1. 정적 경계 위험 (Static Boundary Risk)
`QuestSpatialObstacleProvider`가 머리·양손·이동 경로에서 최대 24개의 레이캐스트를 `EnvironmentRaycastManager`/Room Scene에 쏘고, 강건한 평면 추정으로 벽·저장애물 방향과 형태를 복원합니다. `StaticBoundaryRiskMath`가 거리(가중치 0.45)·접근속도(0.30)·TTC(0.20)를 정규화해 가중합한 위험도를 내며, 사용자 상태(정지/이동)에 따라 머리·저장애물 채널의 on 임계값이 0.50(정지) ↔ 0.45(이동) 사이에서 보간되고, 손 채널은 팔 도달 범위를 반영한 별도 임계값(0.40)을 씁니다. 모든 채널의 off 임계값은 on 임계값에서 히스테리시스 폭 0.08을 뺀 값입니다. 사용 가능 거리가 0.25m 이내이면 임계값과 무관하게 즉시 비상 노출됩니다.

### 2. 동적 객체 위험 (Dynamic Object Risk)
`QuestPersonDetectionRunner`가 Passthrough 카메라 프레임을 Unity Inference Engine(Sentis)으로 돌려 YOLOv9 모델로 사람을 검출합니다(NCHW 입력, boxes/classIds/scores 3-출력 계약). `DynamicRiskPipeline`이 이를 추적(`SimpleObjectTracker`)하고, 거리 이력의 최소제곱 기울기(또는 깊이가 부족하면 bbox 크기의 로그 증가율)로 접근/이탈 상태와 TTC를 추정한 뒤, 근접·접근·TTC·경로 4개 성분을 가중합(0.55/0.30/0.10/0.05)해 위험도를 냅니다. on/off 임계값은 각각 0.60/0.45입니다. 안전거리 1m 이내가 확인되거나(신뢰도 0.55 이상, 표본 2개 이상) bbox가 화면을 강하게 채우면, 트래커가 아직 신뢰 확정을 못했어도 즉시 위험을 강제 노출하는 안전장치(`ForcePassthrough`)가 있습니다.

### 3. 융합과 표시 결정
중간보고서 시점(2026년 7월)까지는 `Rtotal = w_static·R_static + w_state·R_state + w_dynamic·R_dynamic + w_intent·R_intent` 하나의 가중합으로 Passthrough를 켰지만, 정적/동적 두 분석 경로는 입력 데이터의 가용성과 반응해야 할 시간 스케일이 서로 다르고 단일 합산 방식은 한 요소의 결측이나 극단값이 다른 요소에 의해 희석될 수 있다는 문제가 있어(전문가 자문 반영), 이후 정적과 동적 판단이 `StaticPassthroughPolicyController`/`DynamicPassthroughPolicyController`로 완전히 분리되어 각자 자신의 히스테리시스로 독립적으로 on/off를 결정하도록 재설계했습니다. `RiskSnapshotBuilder`가 만드는 통합 `RiskSnapshot`은 지금은 로깅과 개인화 feature 계산에만 쓰입니다. 두 정책의 결과는 `SelectivePassthroughController`가 받아, 위험이 있는 실제 방향/사람 영역에만 셰이더 기반 창을 열어 그 부분만 카메라 패스스루로 보여줍니다.

### 4. Quest 런타임 성능 관리
사람 검출 추론은 프레임을 멈추지 않도록 레이어 단위로 여러 프레임에 걸쳐 슬라이스 실행되며(`InferenceSchedulerPolicy`), 0.75초를 넘기면 워치독이 강제로 재시작합니다. 기기에서 CPU/GPU 백엔드를 실측 벤치마크해 더 빠른 쪽을 자동 선택하고(`InferenceBackendSelector`), `TrackingQualityController`가 성능 프로파일에 따라 추론 주기·레이캐스트 개수를 조절합니다.

### 5. ML 개인화
`PersonalizationRuntimeController`는 Passthrough가 실제로 켜져 있던 구간(활성화 이벤트)을 모아 활성화 빈도·평균 지속시간·평균/최대 머리 속도·공간 크기·세션 경과시간의 7차원 feature를 만들고, `ml-personalization/`에서 학습한 RandomForest(100 트리, 최대 깊이 5)를 ONNX로 변환한 뒤 Unity가 지원하는 기본 텐서 연산으로 재구성해 Sentis로 온디바이스 추론합니다. 모델이 내는 "이 활성화가 불필요했을 확률"($p_{neg}$)이 0.5를 넘는 만큼 보정치 $\delta = \mathrm{clip}((p_{neg}-0.5)\times0.20,\,0,\,0.10)$를 다섯 임계값(정적 3종 + 동적 2종) 모두에 동일하게 더해 항상 상향 방향으로만 조정합니다. 안전을 위해 기본은 shadow mode(추론·로깅만, 실제 임계값 미반영)이며, 누적 세션 수가 5회 미만이면(cold start) 개인화를 적용하지 않고 기본값을 유지합니다. **이 모듈은 사용자 실험에서는 통제 변인으로 제외되어 실제 사용자 데이터로 검증되지 않았습니다.**

### 6. 실험 게임 하네스 (사용자 연구용)
`docs/EXPERIMENT_GAME_INTEGRATION.md`에 따라, 위 파이프라인이 켜져 있는 동일한 `SampleScene`에 사격·회피 게임(`ExperimentGameRoot`)을 얹어 참가자가 좁은 플레이 공간에서 3개 라운드를 진행했습니다(라운드 순서 counterbalancing, 학습효과 통제를 위한 사전 튜토리얼 포함).

| 라운드 | 조건(`ExperimentCondition`) | 커스텀 안전 출력 | Guardian |
|---|---|---|---|
| 1 | `GuardianDefault` | 억제 | 표시 (경계 그리드) |
| 2 | `StaticOnly` | 정적(머리/양손/저장애물)만 표시 | 숨김 |
| 3 | `StaticAndDynamic` | 정적+동적(사람) 모두 표시 | 숨김 |

`PassthroughConditionSwitcher`는 실제 컴포넌트의 `enabled`나 PlayerPrefs를 바꾸지 않고, presentation/boundary override만 전환합니다. 라운드 종료·메뉴 복귀 시 사용자의 기본 설정으로 자동 복원됩니다. 로그는 기존 `schemaVersion=2` JSONL 형식에 condition/round 정보를 얹어서 그대로 남습니다.

## 사용자 실험 결과

VR 초심자 위주(83.3%) 12명(N=12, 평균 24.33세)을 대상으로, 좁은 플레이 공간에서 안전감(Safety, Tseng et al. 2024 척도 6문항)과 몰입감(Immersion, IPQ 9문항)을 7점 리커트로 측정하고 Friedman 검정 + 사후 Wilcoxon(Bonferroni 보정)으로 분석했습니다. 개인화 모듈은 라운드 간 비교의 통제 변인 유지를 위해 이번 실험에서 제외했습니다.

| 지표 | Round 1 (Guardian) | Round 2 (정적) | Round 3 (정적+동적) | Friedman |
|---|---|---|---|---|
| 안전감 | 4.50 | 5.45 | 5.73 | χ²=10.59, **p=.005**, W=.44 |
| 몰입감 | 4.51 | 4.32 | 4.03 | χ²=2.13, p=.345, W=.089 |

- 안전감은 Round 1 대비 Round 2·3 모두 유의하게 높았지만(각 p=.005, p=.004), Round 2 vs 3 차이는 유의하지 않았습니다(p=.141) — 정적 인식 도입만으로도 안전감이 척도 상단에 가까워지는 천장효과 가능성이 있습니다.
- 몰입감은 라운드 간 유의한 차이가 없어, 안전 인식 고도화가 몰입감 저하로 이어진다는 근거는 확인되지 않았습니다.
- 보조지표: 성가심은 Round 3에서 평균이 가장 높았으나 비유의(p=.086). 재선호도는 안전감이 가장 낮은 Round 2가 50%(6명)로 가장 많이 선택되어(Round 3는 25%), 참여자들이 안전감 자체보다 안전감-몰입감의 균형을 더 중요하게 고려했을 가능성을 시사합니다.

**한계**: 표본이 작아(N=12) 통계적 검정력이 제한적이며(특히 몰입감), 안전감 척도의 신뢰도가 이례적으로 높게 나타나(α=.955) 문항 간 개념적 중복 가능성이 있습니다. 개인화 모듈은 이번 실험에서 검증되지 않았습니다.

## 시작하기

### Unity 클라이언트 (Quest 3)

1. Unity Hub에서 **6000.4.2f1**(Unity 6)로 `unity-client/` 폴더를 엽니다.
2. Android Build Support 모듈 설치, Android 플랫폼으로 전환.
3. Quest 헤드셋에서 개발자 모드 활성화 + Space Setup(Room Setup) 완료.
4. 실행할 씬 선택:
   - `Assets/Scenes/SampleScene.unity` — 실제 앱(정적/동적 위험도 + 개인화 + 실험 게임 포함), 빌드에 포함되는 유일한 씬.
   - `Assets/Scenes/DynamicRiskMock.unity` — 동적 위험도만 Mock 데이터로 검증하는 씬.
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

실제 Quest 로그(`RiskLogs/*.jsonl`)로 개인화 모델을 만드는 절차는 `ml-personalization/README.md`의 "실제 로그 연동 절차"를 참고하세요. 현재까지 확보된 실제 로그에는 Negative(불필요) 표본이 없어, 배포된 모델은 사실상 Positive 여부만 구분하는 이진 분류기에 가깝습니다.

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

전체 목록은 [`docs/README.md`](docs/README.md)에서도 볼 수 있습니다(일부 최신 문서는 아직 그 인덱스에 반영되지 않았습니다).

## 구현 현황

| 항목 | 상태 |
|---|---|
| Quest 정적 경계 위험도 + Passthrough 표시 | 구현 완료, Quest 실기 검증 완료 |
| Quest 동적(사람) 위험도 + Passthrough 표시 | 구현 완료 (YOLOv9 + Sentis 온디바이스 추론) |
| 정적/동적 독립 정책 + 통합 표시 레이어 | 구현 완료 |
| ML 개인화 (RandomForest → ONNX → Sentis) | Mock 데이터로 end-to-end 검증 완료, 실제 Quest 로그 재검증은 TODO (현재까지 Negative 표본 없음) |
| 실험 게임(3-조건 사용자 연구 하네스) | 구현 완료 |
| 사용자 실험 (N=12, 3-조건 비교) | 완료 — 안전감 유의미한 향상 확인(p=.005), 몰입감 유의미한 저하 없음(p=.345) |

## 향후 연구 방향

- **멀티모달 피드백으로의 확장**: 현재는 시각적 표시(부분적 현실 화면 노출)와 후방 방향 진동만 제공합니다. 3D Spatial Audio와 추가적인 Haptic Feedback 패턴을 경고 표현 계층에 도입해 시각·청각·촉각 신호를 조합할 필요가 있습니다.
- **배포 가능한 형태로의 발전**: 현재는 연구용 프로토타입으로, 특정 개발 환경에서 빌드해 실행하는 수준입니다. 앱 패키징, 설정·권한 안내 UI, 호환성 검증 등을 갖추어야 합니다.
- **개인화 모듈의 실데이터 검증**: 학습·배포 파이프라인 자체는 온디바이스 추론까지 구현되어 있으나, 실제 사용자 로그가 아닌 제한된 데이터로 운용되고 있어 재검증이 필요합니다.
