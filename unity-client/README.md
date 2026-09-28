# Adaptive Passthrough XR — Unity Client

Meta Quest 3에서 정적 장애물과 사람의 위험을 분석하고, 위험 영역에 선택적으로 Passthrough를 표시하는 Unity 클라이언트입니다.

졸업과제 **Context-aware Adaptive Passthrough Framework**의 실행 환경으로, 공간·센서 정보 수집, 정적·동적 위험 판단, 선택적 Passthrough, 온디바이스 ML 개인화와 사용자 실험용 게임을 통합합니다.



## 1. 시스템 구성

```text
Environment Depth / Room Scene / HMD·Controller
    → 공간·움직임 측정 → 정적 위험 정책 ─┐
                                        ├→ Selective Passthrough
Quest Camera → YOLOv9 → 사람 추적        │   + 시각·진동 경고
    → 거리·접근 속도·TTC → 동적 위험 정책 ┘

실제 표시 이력·사용자 움직임 → 개인화 ONNX 추론
    → 정적·동적 정책 임계값 조정
```

정적·동적 위험은 각각 독립적으로 판단합니다. 통합 `Rtotal` 하나로 전체 표시를 결정하지 않으며, `SelectivePassthroughController`가 각 정책의 표시 요구를 하나의 Passthrough 레이어에 반영합니다.

## 2. 구현된 기능

| 영역 | 구현 내용 |
|---|---|
| 정적 위험 | Depth·Room Scene 공간 측정, 머리·양손·낮은 장애물별 거리·접근 속도·TTC 기반 판단 |
| 동적 위험 | YOLOv9 사람 검출, 추적 ID 유지, 깊이 기반 거리·접근 속도·TTC, 깊이 유실 시 BBox 대체 추정 |
| 선택적 표시 | 정적 Surface Window, 사람 상체 Capsule Window, 낮은 장애물 바닥 안내 |
| 경고 안정화 | 히스테리시스, 최소 유지시간, 긴급 근접 조건, 표시 보간과 유실 유지·페이드 |
| 안전 피드백 | 시각적 경고, 컨트롤러 햅틱, 후방 위험 방향 진동 |
| 개인화 | 7차원 Feature 수집, 기기 내 ONNX 추론, 정적·동적 임계값 조정과 로그 기록 |
| 실험 게임 | 조준·발사·회피 튜토리얼, 고정 발사 일정, 점수·효과음, 세 가지 비교 조건 |
| 진단 | 위험·추적·개인화 상태 패널, 품질 프리셋, JSONL 로그, Mock 검증 씬 |

정적 공간 측정은 유효한 Depth·Room Scene 입력을 사용합니다. Room Scene 경로에는 Space Setup이 필요하지만 모든 공간 측정의 필수 조건은 아닙니다. 현재 동적 객체 분석 대상은 `person`입니다.

### ML 개인화

PC에서 학습·변환한 `Assets/Models/personalization_runtime.onnx`를 Unity Inference Engine으로 실행합니다. 기기 내부에서 모델을 재학습하는 구조는 아닙니다.

- 실제 표시 이력과 머리 움직임 등으로 7차원 입력을 구성합니다.
- 앱은 ML 적용이 꺼진 상태로 시작하며, 패널에서 ML을 켜거나 `APPLY ML NOW`로 적용합니다.
- 자동 추론은 누적 세션이 5회 미만이면 기본값을 유지합니다. 즉시 적용은 이 제한을 한 번 우회합니다.
- 현재 기본 임계값은 정적 안정 상태 `0.50`, 빠른 접근 상태 `0.45`, 손 `0.40`, 동적 ON/OFF `0.60/0.45`입니다.
- 개인화는 기본값에서 최대 `0.10`만큼 상향 조정하며, 동적 ON/OFF 간격을 유지합니다. 긴급 거리·최소 유지시간은 조정하지 않습니다.

현재 모델은 파이프라인 검증용입니다. 실제 사용자에 대한 개인화 효과는 별도 검증해야 합니다. 이전 문서나 Python 설정의 기본값은 현재 Unity와 다를 수 있으므로 재학습·연동 시 함께 확인해야 합니다.

### 실험 조건

| 라운드 | 조건 | 커스텀 안전 출력 | Guardian 요청 |
|---|---|---|---|
| Round 1 | GuardianDefault | 억제 | 표시 |
| Round 2 | StaticOnly | 정적 위험 표시 | 숨김 |
| Round 3 | StaticAndDynamic | 정적·동적 위험 표시 | 숨김 |

조건 전환 중에도 측정·추론·로그는 계속 실행됩니다. ML은 별도 설정이므로 비교 실험 전에 적용 여부를 통일해야 합니다. Guardian 조건에서는 실제 suppression 해제를 확인한 뒤 라운드를 시작합니다.

## 3. 개발 환경과 폴더

| 항목 | 기준 |
|---|---|
| 기기 | Meta Quest 3 및 컨트롤러 |
| Unity | 6000.4.2f1 |
| 렌더링·XR | URP / OpenXR |
| Meta XR | Core SDK, Interaction SDK, MR Utility Kit 203.0.0 |
| 추론 | Unity Inference Engine 2.6.1 |
| Android 빌드 | Android Build Support, SDK·NDK, OpenJDK |

전체 패키지는 [manifest.json](Packages/manifest.json)을 참고합니다.

```text
Assets/
├─ Scenes/
│  ├─ SampleScene.unity          # 정적·동적 Passthrough 기능 확인용
│  ├─ DynamicRiskMock.unity      # Mock 입력·표현 검증
│  └─ ExperimentGameTest.unity   # 사용자 실험용 게임 씬
├─ Scripts/
│  ├─ AdaptivePassthrough/      # 위험 계산·추적·Quest 입력
│  ├─ Experiment/              # 튜토리얼·게임·조건 전환
│  └─ *.cs                     # 정책·표시·개인화·진단 컨트롤러
├─ Models/                     # 검출·개인화 모델
├─ Prefabs/Experiment/         # 통합 게임 Prefab
├─ Editor/                     # 씬 구성·빌드 도구
└─ Tests/                      # EditMode·PlayMode 테스트
Tools/                         # Quest APK 설치 도구
```

## 4. 씬 구성과 사용 방법

### Sample Scene — Passthrough 기능 확인

[SampleScene.unity](Assets/Scenes/SampleScene.unity)는 평면 위에 사용자가 위치하며, 정적·동적 Passthrough를 선택하여 확인하는 씬입니다. 사용자 화면 중앙에는 정적 Passthrough와 동적 Passthrough를 각각 활성화하는 버튼 두 개가 있습니다.

| 정적 버튼 | 동적 버튼 | 적용 방식 |
|---|---|---|
| 선택 | 선택 | 정적·동적 Passthrough 모두 활성화 |
| 선택 | 미선택 | 정적 Passthrough만 활성화 |
| 미선택 | 선택 | 동적 Passthrough만 활성화 |
| 미선택 | 미선택 | 기본 Passthrough 적용 |

정적 기능은 벽·가구 등 주변 장애물에 대한 반응을, 동적 기능은 사람의 접근에 대한 반응을 확인하는 데 사용합니다. 두 버튼을 함께 선택하면 두 기능을 동시에 확인할 수 있습니다.

### Experiment Scene — 사용자 실험

[ExperimentGameTest.unity](Assets/Scenes/ExperimentGameTest.unity)는 사용자 실험용 씬입니다. 조준·발사·회피 게임과 튜토리얼을 통해 과제를 익히고, Guardian·정적 위험·정적 및 동적 위험의 세 조건을 비교합니다.

게임 진행 방식과 점수 규칙은 [실험 게임 구현 상세](PROTOTYPE_DETAILS.md#5-실험-게임-구성)를 참고합니다.

### Dynamic Risk Mock — 개발용 검증

[DynamicRiskMock.unity](Assets/Scenes/DynamicRiskMock.unity)는 Mock 입력을 사용해 동적 위험과 표시 동작을 확인하는 개발용 씬입니다.

## 5. 빌드 및 실행

1. Unity Hub에서 `unity-client` 폴더를 Unity `6000.4.2f1`로 엽니다.
2. 패키지·모델 임포트와 Console의 컴파일 오류 여부를 확인합니다.
3. Quest의 개발자 모드를 켜고 USB로 연결한 뒤 헤드셋에서 USB 디버깅을 허용합니다.
4. 기능 확인에는 `Assets/Scenes/SampleScene.unity`, 사용자 실험에는 `Assets/Scenes/ExperimentGameTest.unity`를 엽니다.
5. `File > Build Profiles`에서 Android를 활성화하고 실행할 씬이 빌드에 포함되며 시작 씬으로 지정되어 있는지 확인합니다. 현재 저장된 기본 빌드 설정은 `SampleScene`만 활성화되어 있습니다.
6. `Build And Run`으로 설치·실행합니다.

전용 빌드 메뉴는 `TeamVR > Adaptive Passthrough > Build Quest 3 APK`입니다. 기본 APK 출력 경로:

```text
Builds/Android/AdaptivePassthrough.apk
```

APK 생성 후 `Tools/Install-Quest3.cmd`를 실행하면 연결된 Quest에 덮어쓰기 설치하고 앱을 실행합니다. 설치 후에는 Quest 앱 라이브러리의 **알 수 없는 출처(Unknown Sources)**에서 다시 실행할 수 있습니다.

기기에서는 카메라·공간 권한을 허용하고, Room Scene 사용 시 Space Setup을 완료합니다. Sample Scene에서는 중앙의 두 버튼으로 사용할 Passthrough 기능을 선택합니다. Experiment Scene에서는 Guardian 비교 조건에 필요한 기기 경계를 설정하고, 튜토리얼 진행 후 라운드를 선택합니다.

## 6. 로그와 검증

로그 위치는 `Application.persistentDataPath/RiskLogs/`입니다.

| 파일 패턴 | 내용 |
|---|---|
| dynamic-risk-*.jsonl | 검출·추적·거리·움직임·동적 위험 진단 |
| personalization-*.jsonl | 개인화 입력, 모델 결과, 임계값과 적용 상태 |

현재 기본 씬에는 기존 `RiskSnapshotSessionLogger`가 연결되어 있지 않습니다. `ml-personalization`의 `risk-snapshot-*.jsonl` 전용 파서는 현재 개인화 로그를 그대로 처리하지 못하므로 재학습에는 입력 형식 연결이 필요합니다.

Unity Test Runner의 EditMode·PlayMode 테스트는 위험 계산, 추적, 표현 상태와 게임 통합을 검사합니다. 센서 정확도, Guardian 동작, 양안 표시 품질과 Quest 성능은 실기기에서 별도 확인해야 합니다. 이 문서 갱신 시 테스트나 APK 빌드를 새로 실행하지는 않았습니다.


## 7. 관련 문서

- [정적 위험도 알고리즘과 실험 게임 상세](PROTOTYPE_DETAILS.md)
- [프로젝트 전체 소개](../README.md)
- [공간 측정·사람 추적 품질 개선](../docs/SPATIAL_TRACKING_QUALITY_IMPLEMENTATION.md)
- [온디바이스 개인화 통합](../docs/ONDEVICE_SENTIS_INTEGRATION.md)
- [실험 게임 통합](../docs/EXPERIMENT_GAME_INTEGRATION.md)
- [개인화 학습 파이프라인](../ml-personalization/README.md)
- [모델 안내](Assets/Models/README.md) / [제3자 고지](Assets/Models/THIRD_PARTY_NOTICES.md)

관련 문서에는 이전 개발 단계의 설정이 남아 있을 수 있습니다. 현재 동작은 활성 씬과 런타임 코드를 기준으로 확인합니다.
