# 정적 경계 위험도 선택 통합 계획서

작성일: 2026-08-07  
소스 브랜치: `origin/feature/static-boundary-passthrough` (`3cf7a23`)  
대상: 현재 작업트리의 정적·동적 독립 정책 및 선택적 Passthrough 구현

## 구현 상태 (2026-08-08)

계획서의 선택 이식 범위를 현재 브랜치에 적용했다.

- `StaticBoundaryRiskModels`와 `StaticBoundaryRiskMath`를 추가해 머리·손 위험 계산과
  연속형 `UserState` 계산을 Unity 의존성이 적은 코어 로직으로 분리했다.
- `QuestRiskExperimentLogger`는 목 피벗·순병진 이동 창·손 reach gate를 포함한
  `StaticBoundaryRiskFrame` 측정만 발행하며 Passthrough 상태를 직접 변경하지 않는다.
- `StaticPassthroughPolicyController`는 `0.60/0.40` 고정 가중합 대신
  `0.65/0.45` 가변 진입 임계값, `0.08` 히스테리시스, 손 `0.85`,
  긴급 거리 `0.25 m` 정책을 사용한다.
- `SelectivePassthroughController`가 원인별 위험 방향을 받아 정적 window를 만들고,
  Scene의 단일 `OVRPassthroughLayer`를 계속 단독 제어한다.
- Scene Builder로 `SampleScene` 참조와 기본값을 갱신했다.
- Unity EditMode 테스트 `65/65` 통과 및 변경 스크립트 진단 오류 0건을 확인했다.

이번 단계에서 `Full`은 전체 화면 전환이 아니라 현재의 방향성 정적 window 활성화로
매핑했다. 후방 위험 UI·햅틱과 emergency 전체 화면 승격은 계획대로 후속 범위다.

## 1. 결론

정적 경계 브랜치는 Git 브랜치 전체를 병합하거나 커밋을 그대로 cherry-pick하지 않는다.
해당 브랜치의 정적 위험 계산과 정책만 현재 구조에 수동으로 선택 이식한다.

이유는 다음과 같다.

- 소스 브랜치는 240개 파일을 변경하고 그중 229개를 삭제한다.
- 전체 병합 시 동적 사람 검출, 모델, 테스트, 카메라 권한, 로그 및 빌드 설정이 제거된다.
- 소스 구현은 `QuestRiskExperimentLogger`가 위험 계산, 정책 판단, UI, 실제
  `OVRPassthroughLayer` 제어까지 모두 담당한다.
- 현재 구현은 정적 측정, 정적·동적 정책, 선택적 렌더링을 분리했으므로 이 책임 분리를
  유지해야 한다.
- 현재 정적 정책의 `0.60 × Rstatic + 0.40 × Rstate` 방식과 소스 브랜치의
  `UserState` 기반 가변 임계값 방식은 의미가 다르다. 이번 통합에서는 소스 브랜치의
  최신 정적 정책을 기준으로 교체한다.

최종 목표는 다음과 같다.

```text
QuestRiskExperimentLogger
  ├─ Room Scene 벽 추출
  ├─ 머리/몸통 위험 R_static_head
  ├─ 손 위험 R_static_hand
  ├─ 연속형 UserState 0..1
  └─ 정적 위험 프레임 발행
             │
             ▼
StaticPassthroughPolicyController
  ├─ UserState 기반 가변 ON/OFF 임계값
  ├─ 머리/손 독립 진입 조건
  ├─ 긴급 거리 override
  ├─ 히스테리시스
  └─ None / Aware / Full 및 원인 발행
             │
             ▼
SelectivePassthroughController
  ├─ 정적 위험 방향 window
  ├─ 동적 사람 bbox window와 합집합
  └─ 유일한 OVRPassthroughLayer 제어자
```

## 2. 통합 범위

### 2.1 가져올 항목

소스 브랜치에서 다음 동작을 이식한다.

1. 머리/몸통 정적 위험 `R_static_head`
   - 벽까지 거리 위험 `Rd`
   - TTC 위험 `RTTC`
   - 벽 방향 접근 가속도 위험 `Ra`
   - 시야 사각 위험 `Rblind`
   - `UserState`와 분리된 순수 환경/기하 위험도
2. 연속형 `UserState`
   - 범위 `0..1`
   - 머리의 순이동 속도만 사용
   - 손 속도와 각속도는 임계값 민감도에 직접 반영하지 않음
   - 목 피벗 보정과 시간 창을 사용하여 제자리 고개 회전 및 왕복 흔들림의 영향을 줄임
3. 손 정적 위험 `R_static_hand`
   - 좌우 손을 독립 계산하고 최댓값을 사용
   - 손-벽 거리와 TTC 사용
   - 팔 도달 가능 범위 gate 적용
   - 실행 중 관측된 최대 reach를 제한 범위 안에서 보정
4. 정적 정책
   - 정적 상태 ON 임계값 기본값 `0.65`
   - 동적 상태 ON 임계값 기본값 `0.45`
   - 히스테리시스 폭 기본값 `0.08`
   - 손 Full 임계값 기본값 `0.85`
   - Aware 임계값 기본값 `0.40`
5. 긴급 조건
   - 거리 기본값 `0.25 m`
   - 접근 속도 기본값 `0.10 m/s`
   - 거리만 가까운 정지 상태에서는 발동하지 않음
   - 접근이 멈추거나 release margin 밖으로 벗어나면 해제
6. 진단 데이터
   - `None / Aware / Full`
   - 현재 유효 ON/OFF 임계값
   - 정적 활성화 원인 `Head / Hand / Emergency`
   - 머리와 손 중 실제 위험을 만든 방향

### 2.2 가져오지 않을 항목

다음 변경은 통합 대상에서 명시적으로 제외한다.

- 소스 브랜치의 파일 삭제 전부
- `SampleScene.unity` 전체 교체
- `ProjectSettings` 전체 교체
- `Packages/manifest.json`, `packages-lock.json` 변경
- Quest 카메라 권한 제거
- `com.unity.ai.inference` 및 MR Utility Kit 제거
- YOLO 및 개인화 ONNX 모델 제거
- 동적 위험 코드, 테스트, 로그 제거
- `QuestRiskExperimentLogger`의 `OVRPassthroughLayer` 직접 참조
- `QuestRiskExperimentLogger.ApplyPassthroughState()` 방식
- 앱 이름, Android application identifier, custom manifest 설정 변경
- 소스 브랜치의 `.gitignore` 전체 적용
- 후방 위험 전체 화면 Passthrough와 햅틱 경고
- ML 개인화 런타임 연결

## 3. 핵심 설계 결정

### 3.1 정적 계산과 정적 정책을 분리한다

`QuestRiskExperimentLogger`는 센서와 Scene 입력을 정적 위험 데이터로 변환하는
측정 제공자다. Passthrough를 켜거나 끄지 않는다.

`StaticPassthroughPolicyController`는 측정 프레임을 받아 다음을 결정한다.

- 정적 source 사용 가능 여부
- 유효 ON/OFF 임계값
- 머리 위험 진입 여부
- 손 위험 진입 여부
- 긴급 진입/유지 여부
- 최종 정적 요청 ON/OFF
- `None / Aware / Full`
- 활성화 원인과 표시 방향

실제 `OVRPassthroughLayer.hidden` 변경은 계속 `SelectivePassthroughController`만 한다.

### 3.2 기존 정적 가중합 정책을 교체한다

현재 정책은 다음과 같다.

```text
StaticDecisionRisk = 0.60 × Rstatic + 0.40 × Rstate
ON  = 0.60
OFF = 0.50
```

통합 후 `UserState`는 위험도에 더하지 않는다.

```text
effectiveOnThreshold =
    Lerp(max(stableOnThreshold, rapidOnThreshold),
         min(stableOnThreshold, rapidOnThreshold),
         clamp01(UserState))

effectiveOffThreshold =
    clamp01(effectiveOnThreshold - max(0, hysteresisWidth))
```

정적 요청의 진입 조건은 다음과 같다.

```text
Emergency
OR R_static_head >= effectiveOnThreshold
OR R_static_hand >= handFullThreshold
```

해제 조건은 다음과 같다.

```text
NOT EmergencyHold
AND R_static_head <= effectiveOffThreshold
AND R_static_hand <= handReleaseThreshold
```

여기서 다음을 사용한다.

```text
handReleaseThreshold = max(0, handFullThreshold - hysteresisWidth)
```

정적 정책은 가변 임계값과 머리/손 별도 release 조건이 필요하므로 기존의 단일 점수용
`PassthroughDecisionFilter`를 그대로 사용하지 않는다. 동적 정책은 현재 필터를 그대로
유지한다.

### 3.3 연속형 UserState와 기존 상태 필터를 함께 유지한다

현재 `UserMotionStateFilter`의 `Static / Dynamic / Agitated` 상태와 EMA 기반 진단값은
삭제하지 않는다. 동적/로그 회귀 방지와 기존 진단 UI를 위해 그대로 보존한다.

정적 임계값 조정에는 별도의 연속형 `UserState01`을 사용한다.

```text
UserState01 = clamp01(netHeadTranslationSpeed / thresholdHeadSpeedScale)
```

계산 원칙은 다음과 같다.

- HMD 중심이 아니라 목 피벗을 근사한 위치를 사용한다.
- 최근 약 `0.5 s` 구간의 시작점과 현재점 사이 순변위를 사용한다.
- 좌우 왕복 성분은 상쇄한다.
- 시간 창이 준비되기 전에는 `0`을 사용한다.
- 손 움직임과 HMD 각속도는 `UserState01`에 넣지 않는다.
- 기존 `UserMotionStateFilter`에는 같은 목 피벗 위치를 입력할지 별도 테스트로 확정한다.
  기본안은 목 피벗 위치를 입력하여 제자리 고개 회전의 가짜 병진 속도를 함께 줄이는 것이다.

### 3.4 정적 Full은 현재 렌더링 구조의 정적 ON으로 해석한다

소스 브랜치의 `Full`은 전체 화면 Passthrough를 의미하지만, 현재 구현은 한 개의
background layer 위에 위험 영역 window를 여는 구조다.

이번 단계에서는 다음처럼 매핑한다.

| 정적 정책 상태 | 현재 렌더링 동작 |
|---|---|
| `None` | 정적 window 숨김 |
| `Aware` | HUD/로그에만 표시, 정적 window 숨김 |
| `Full` | 위험 방향의 정적 window 표시 |
| `Emergency` 원인의 `Full` | 정적 window 표시, emergency 원인 기록 |

전체 화면 승격은 이번 범위에서 제외한다. 이를 추가하려면 정적·동적 window 합성 및
단일 layer 소유권을 유지하는 별도 설계가 필요하다.

### 3.5 표시 방향은 실제 활성화 원인을 따른다

현재 선택 렌더러는 항상 머리 기준 최근접 벽 방향을 사용한다. 손 위험으로 ON된 경우에는
잘못된 벽을 표시할 수 있다.

통합 후 방향 선택 규칙은 다음과 같다.

1. `Emergency` 또는 `Head` 원인: 머리의 최근접 위험 벽 방향
2. `Hand` 원인: 위험도가 더 높은 손의 벽 방향
3. 동률: `Emergency > Head > Hand` 우선순위
4. 선택 방향이 유효하지 않거나 FOV 밖이면 현재와 같이 window를 숨김

후방 방향 cue와 전체 화면 승격은 후속 작업으로 남긴다.

## 4. 데이터 계약

### 4.1 새 정적 측정 프레임

`StaticRiskMeasurement`는 기존 통합 RiskSnapshot 호환을 위해 유지한다. 손 위험,
연속형 상태, 방향 및 긴급 입력을 담을 새 계약을 추가한다.

권장 파일:

```text
unity-client/Assets/Scripts/AdaptivePassthrough/Core/StaticBoundaryRiskModels.cs
```

권장 타입:

```csharp
public sealed class StaticBoundaryRiskFrame
{
    public long Sequence;
    public double TimestampSeconds;
    public bool Available;

    public StaticRiskMeasurement Head;
    public float UserState01;
    public bool MotionWindowWarmedUp;

    public float LeftHandRisk;
    public float RightHandRisk;
    public float MaximumHandRisk;

    public Vector3 HeadHazardDirectionWorld;
    public Vector3 LeftHandHazardDirectionWorld;
    public Vector3 RightHandHazardDirectionWorld;

    public bool HeadApproaching;
    public bool LeftHandApproaching;
    public bool RightHandApproaching;
}
```

실제 구현에서는 public mutable field 대신 현재 코드 스타일에 맞는 읽기 전용 필드와
생성자를 사용한다. NaN/Infinity와 음수 시간·거리 값은 생성 또는 계산 경계에서 정규화한다.

`QuestRiskExperimentLogger`는 다음 출력을 제공한다.

```csharp
public StaticBoundaryRiskFrame CurrentStaticBoundaryFrame { get; }
```

기존 출력은 호환을 위해 유지한다.

```csharp
public StaticRiskMeasurement CurrentStaticMeasurement { get; }
public UserMotionSnapshot CurrentMotionSnapshot { get; }
public bool SceneDataAvailable { get; }
```

### 4.2 정적 정책 결과

정적 전용 정책 상태를 추가한다.

```csharp
public enum StaticWarningLevel
{
    None,
    Aware,
    Full
}

public enum StaticActivationCause
{
    None,
    Head,
    Hand,
    Emergency
}

public sealed class StaticPassthroughDecision
{
    public PassthroughSourceDecision SourceDecision;
    public StaticWarningLevel WarningLevel;
    public StaticActivationCause Cause;
    public float HeadRisk;
    public float HandRisk;
    public float CombinedRisk;
    public float UserState01;
    public float EffectiveOnThreshold;
    public float EffectiveOffThreshold;
    public bool EmergencyTrigger;
    public bool EmergencyHold;
    public Vector3 HazardDirectionWorld;
    public bool HazardDirectionAvailable;
}
```

`StaticPassthroughPolicyController.Latest`의 기존 타입은 동적 정책과 HUD 호환을 위해
유지하고, 상세 결과를 별도 프로퍼티로 노출한다.

```csharp
public StaticPassthroughDecision LatestStatic { get; }
```

## 5. 파일별 변경 계획

| 파일 | 작업 |
|---|---|
| `Core/StaticBoundaryRiskModels.cs` | 정적 측정 프레임, 손 위험 측정, 정책 결과와 enum 추가 |
| `Core/StaticBoundaryRiskMath.cs` | head/hand 위험, reach gate, 임계값, 경고 등 순수 계산 함수 추가 |
| `QuestRiskExperimentLogger.cs` | 목 피벗·연속 UserState·손-벽 위험을 이식하고 측정 프레임 발행. 직접 Passthrough 제어는 추가하지 않음 |
| `StaticPassthroughPolicyController.cs` | 기존 `0.60/0.40` 가중합을 가변 임계값·손·긴급 정책으로 교체 |
| `IndependentPassthroughModels.cs` | 공통 source decision은 유지. 기존 `StateRisk`/`StaticDecisionRisk`는 사용처 제거 후 삭제 여부 결정 |
| `SelectivePassthroughController.cs` | 정적 상세 결정의 원인별 hazard direction 사용. 동적 bbox 흐름은 변경하지 않음 |
| `IndependentPassthroughHud.cs` | head/hand 위험, UserState, 유효 임계값, Aware/Full, 원인을 표시 |
| `AdaptivePassthroughSceneBuilder.cs` | 새 필드와 컴포넌트 참조 연결. 기존 단일 layer와 동적 구성 보존 |
| `SampleScene.unity` | Scene Builder가 만든 최소 직렬화 변경만 반영. 소스 브랜치 씬으로 교체 금지 |
| `SelectivePassthroughMathTests.cs` | 기존 선택 렌더링 및 동적 관련 회귀 테스트 유지 |
| `StaticBoundaryRiskMathTests.cs` | 순수 정적 계산 테스트 신규 추가 |
| `StaticPassthroughPolicyTests.cs` | 상태를 가진 정적 정책 전이 테스트 신규 추가 |
| `QuestIntegrationAssetTests.cs` | 단일 layer, 의존성 방향, Scene 참조 및 직접 제어 금지 검증 보강 |
| `STATIC_DYNAMIC_RISK_SEPARATION_SPEC.md` | 정적 정책을 최신 가변 임계값 구조로 갱신 |

Unity `.meta` 파일은 새 C# 파일과 동시에 생성하고 추적한다. 소스 브랜치의 `.meta`를
무관한 경로에 복사하지 않는다.

## 6. 구현 단계

### 단계 0. 현재 작업 보호

현재 작업트리는 수정 29개, 미추적 36개 파일이 있는 상태다. 구현 전에 현재 작업을
하나의 복구 가능한 체크포인트로 만든다.

1. 현재 변경 파일을 다시 확인한다.
2. 현재 정적·동적 재분리 작업을 별도 커밋 또는 명시적 백업 브랜치로 보존한다.
3. 통합 전 Unity EditMode 테스트 결과를 기록한다.
4. 새 작업 브랜치 `codex/static-boundary-selective-integration`을 사용한다.
5. 이번 단계에서는 `main` 병합과 ML 브랜치 통합을 함께 수행하지 않는다.

완료 조건:

- 현재 변경을 한 커밋 단위로 되돌릴 수 있다.
- 정적 통합 커밋과 기존 재분리 작업을 구분할 수 있다.

### 단계 1. 순수 모델과 계산식 이식

1. `StaticBoundaryRiskModels.cs`를 추가한다.
2. `StaticBoundaryRiskMath.cs`를 추가한다.
3. 소스 브랜치에서 다음 계산만 옮긴다.
   - 안전한 분모 처리
   - `Rd`, `RTTC`, `Ra`, `Rblind`, `R_static_head`
   - 손 reach gate와 손 거리/TTC 위험
   - 가변 ON/OFF 임계값
   - hand release 임계값
   - Aware 판정
4. Unity 컴포넌트 없이 실행되는 EditMode 테스트를 먼저 작성한다.

완료 조건:

- 모든 계산 결과가 `0..1` 범위다.
- 0, 음수, NaN, Infinity 설정값에서도 NaN/Infinity가 출력되지 않는다.
- `R_static_head` 결과가 `UserState01` 변화에 영향을 받지 않는다.

### 단계 2. 측정 제공자에 머리·손·연속 상태 이식

1. 기존 Scene API 벽 수집 로직을 유지한다.
2. 목 피벗 근사 위치와 `0.5 s` 순변위 창을 추가한다.
3. 좌우 손 속도를 EMA로 안정화한다.
4. 좌우 손별 최근접 벽 거리, 접근 속도, reach gate와 위험도를 계산한다.
5. `CurrentStaticBoundaryFrame`을 매 측정 주기 갱신한다.
6. 기존 `CurrentStaticMeasurement`에는 새 head 위험을 계속 반영한다.
7. Scene unavailable이면 head/hand 위험, 방향과 접근 상태를 모두 unavailable/zero로 초기화한다.
8. 텍스트 UI 갱신 주기와 위험 계산 주기를 분리한다.

완료 조건:

- `QuestRiskExperimentLogger`에 `OVRPassthroughLayer` 필드가 없다.
- `QuestRiskExperimentLogger`가 정적 또는 동적 policy/controller를 참조하지 않는다.
- 손을 움직여도 `UserState01`이 직접 증가하지 않는다.
- 제자리에서 고개만 회전할 때 목 피벗 기준 병진 속도가 과도하게 증가하지 않는다.

### 단계 3. 정적 정책 교체

1. `StaticPassthroughPolicyController`가 `CurrentStaticBoundaryFrame`을 소비하게 한다.
2. 기존 `staticRiskWeight/stateRiskWeight`와 고정 `decisionSettings` 경로를 제거한다.
3. 다음 설정을 controller에 직렬화한다.
   - `stableOnThreshold = 0.65`
   - `rapidOnThreshold = 0.45`
   - `hysteresisWidth = 0.08`
   - `handFullThreshold = 0.85`
   - `awareThreshold = 0.40`
   - `awareApproachSpeed = 0.05`
   - `emergencyDistance = 0.25`
   - `emergencyReleaseMargin = 0.05`
   - `emergencyApproachSpeed = 0.10`
4. 정책 내부의 emergency hold를 reset 가능한 상태로 관리한다.
5. provider unavailable, component disable, Scene reload 때 정책 상태를 OFF로 reset한다.
6. 일반 `Latest`와 상세 `LatestStatic`을 같은 sequence/timestamp로 발행한다.

완료 조건:

- UserState가 높을수록 head ON 임계값이 낮아진다.
- 손 위험은 UserState와 무관하게 높은 별도 임계값으로 진입한다.
- 벽 가까이 정지한 상태만으로 emergency가 발동하지 않는다.
- 한 프레임의 임계값 경계 진동으로 ON/OFF가 반복되지 않는다.

### 단계 4. 선택 렌더링과 HUD 연결

1. 정적 원인에 따라 head 또는 hand 방향을 선택한다.
2. 기존 FOV 검사와 벽 방향 window 계산을 유지한다.
3. `Aware`는 HUD에만 표시한다.
4. `Full`일 때만 정적 window를 연다.
5. 정적 window와 동적 사람 window는 기존과 같이 합집합으로 표시한다.
6. HUD에 다음 값을 추가한다.
   - `R_static_head`
   - `R_static_hand`
   - `UserState01`
   - effective ON/OFF threshold
   - `None/Aware/Full`
   - `Head/Hand/Emergency` 원인
7. Static 테스트 토글은 위험 계산을 끄지 않고 window 표현만 차단하는 현재 동작을 유지한다.

완료 조건:

- 손 위험으로 켜지면 해당 손이 접근 중인 벽 방향이 표시된다.
- 정적 feature OFF가 동적 bbox window를 끄지 않는다.
- 동적 feature OFF가 정적 window를 끄지 않는다.
- Scene에 `OVRPassthroughLayer`가 정확히 하나다.

### 단계 5. Scene 및 문서 갱신

1. `AdaptivePassthroughSceneBuilder`로 참조를 생성한다.
2. `SampleScene`의 기존 동적, 카메라, 모델, layer 참조를 보존한다.
3. 정적 정책 기본값이 코드, Scene, 문서에서 동일한지 확인한다.
4. 기존 `STATIC_DYNAMIC_RISK_SEPARATION_SPEC.md`의 정적 가중합 설명을 새 정책으로 갱신한다.
5. 정책 스키마 버전을 로그나 HUD에 남길 수 있도록 `static-boundary-v2` 식별자를 정의한다.

완료 조건:

- Scene 재생 시 Missing Script와 직렬화 오류가 없다.
- Scene Builder를 다시 실행해도 layer나 controller가 중복 생성되지 않는다.
- 동적 파트 설정값에 변화가 없다.

### 단계 6. 검증 및 통합 커밋 정리

1. EditMode 테스트 전체를 실행한다.
2. Unity Editor에서 정적 mock/수동 시나리오를 확인한다.
3. Quest 3에서 Room Scene 기반 실측 시나리오를 수행한다.
4. `git diff --stat`과 삭제 목록을 확인한다.
5. 동적 모델, 권한, 패키지 및 빌드 설정이 유지됐는지 확인한다.
6. 작업을 책임별 커밋으로 정리한다.

권장 커밋 순서:

```text
feat(static-risk): add boundary risk models and pure math
feat(static-risk): port head motion and hand boundary measurements
feat(static-policy): adopt motion-adaptive static thresholds
feat(static-ui): route static hazard direction to selective passthrough
test(static-risk): cover boundary risk and policy transitions
docs(static-risk): document selected static branch integration
```

## 7. 테스트 계획

### 7.1 순수 계산 테스트

| 테스트 | 기대 결과 |
|---|---|
| 벽에서 충분히 멀고 정지 | head/hand 위험 0에 가까움 |
| 같은 거리에서 TTC 감소 | `RTTC`, head 위험 증가 |
| 벽을 등지고 같은 속도로 접근 | 정면 접근보다 `Rblind`와 head 위험이 높음 |
| UserState만 변경 | `R_static_head` 불변, effective threshold만 변경 |
| stable/rapid 임계값 역입력 | 높은 값은 static endpoint, 낮은 값은 dynamic endpoint로 안전 정렬 |
| safe distance/time이 0 | 유한한 `0..1` 결과 |
| 손이 reach 밖 | `R_static_hand = 0` |
| 손이 reach 안에서 빠르게 접근 | 손 TTC 위험과 최종 손 위험 증가 |
| 양손 중 한 손만 위험 | max 위험과 hazard direction이 위험한 손을 선택 |
| 왕복 머리 흔들림 | 짧은 순간 속도보다 순변위 UserState가 낮음 |
| 제자리 고개 회전 | 목 피벗 병진 UserState가 과도하게 상승하지 않음 |

### 7.2 정책 상태 전이 테스트

| 시작 | 입력 | 기대 결과 |
|---|---|---|
| OFF | head risk가 effective ON 미만 | OFF 유지 |
| OFF | head risk가 effective ON 이상 | Head 원인 Full |
| ON | head risk가 ON 아래지만 OFF 위 | ON 유지 |
| ON | head와 hand 모두 release 아래 | OFF |
| OFF | hand risk가 0.85 이상 | Hand 원인 Full |
| ON/Hand | hand risk가 hand release 위 | ON 유지 |
| OFF | emergency 거리지만 접근 속도 0 | OFF |
| OFF | emergency 거리와 접근 속도 충족 | Emergency 원인 Full |
| ON/Emergency | 접근 중이고 release margin 안 | hold 유지 |
| ON/Emergency | 접근 정지 | hold 해제 가능 |
| 임의 | provider unavailable | 즉시 unavailable/OFF 및 상태 reset |
| OFF | combined risk가 Aware 이상이며 접근 중 | Aware, 렌더링 OFF |

### 7.3 통합 회귀 테스트

- 동적 위험 계산, depth, tracker, bbox 후처리 테스트가 모두 통과한다.
- `DynamicPassthroughPolicyController`의 임계값과 필터가 변경되지 않는다.
- `SelectivePassthroughController`에 한 source의 OFF가 다른 source의 ON을 덮어쓰는
  코드가 없다.
- 정적 provider는 동적 namespace/type을 참조하지 않는다.
- 정적 provider와 policy는 `OVRPassthroughLayer.hidden`을 변경하지 않는다.
- Scene에는 background `OVRPassthroughLayer`가 하나만 존재한다.
- shader와 bbox window import/좌표 테스트가 유지된다.
- Android manifest의 `horizonos.permission.HEADSET_CAMERA`가 유지된다.
- Oculus config의 Passthrough Camera Access가 유지된다.
- `com.unity.ai.inference` 및 MR Utility Kit가 유지된다.

### 7.4 Quest 3 시나리오

1. 공간 중앙에서 정지: `None`, 정적 window 없음
2. 벽을 향해 걸음: UserState 상승, 임계값 하강, 벽 접촉 전에 정적 window 표시
3. 같은 속도로 벽에 등을 지고 접근: 더 높은 blind risk 확인
4. 벽 가까이에 정지: emergency 미발동
5. 벽 가까이에서 벽 방향으로 이동: emergency 발동
6. 공간 중앙에서 손만 크게 흔듦: UserState 미상승, 손 위험 0에 가까움
7. 손이 닿을 수 있는 벽으로 접근: Aware 후 충분히 가까우면 Hand 원인 Full
8. 좌우 손이 다른 벽을 향함: 더 위험한 손 방향 window 표시
9. Room Scene 권한 거부: 정적 unavailable, 동적 사람 bbox는 계속 동작
10. 카메라 권한 거부: 동적 unavailable, 정적 벽 위험은 계속 동작
11. 정적·동적 동시 ON: 벽 window와 사람 bbox window가 동시에 표시
12. Static 토글 OFF: 정적 계산/HUD는 유지되고 정적 window만 사라짐

## 8. 완료 기준

다음 조건을 모두 만족해야 정적 통합이 완료된 것으로 본다.

- 소스 브랜치의 229개 삭제가 하나도 유입되지 않는다.
- `R_static_head`와 `R_static_hand`가 독립적으로 계산된다.
- `UserState01`은 위험 점수에 합산되지 않고 head 임계값만 조절한다.
- 손 움직임은 `UserState01`을 직접 올리지 않는다.
- 거리만 가까운 정지 상태에서 emergency가 켜지지 않는다.
- 정적 정책은 head, hand, emergency 원인을 구분한다.
- 실제 Passthrough layer는 `SelectivePassthroughController`만 제어한다.
- 정적 ON은 현재 구조에서 올바른 위험 방향 window로 표현된다.
- 동적 bbox, depth, tracker, 모델 및 로그가 퇴행하지 않는다.
- 단일 Passthrough layer 규칙이 유지된다.
- EditMode 전체 테스트가 통과한다.
- Quest 3 실기에서 정적 단독, 동적 단독, 동시 위험 시나리오가 통과한다.
- 코드, Scene, 문서의 정적 기본 임계값이 일치한다.

## 9. 위험 요소와 대응

| 위험 | 대응 |
|---|---|
| dirty 작업트리에서 통합 중 기존 변경 손실 | 단계 0에서 체크포인트를 먼저 만들고 브랜치 전환 금지 상태를 해소 |
| 소스 커밋의 대량 삭제 유입 | merge/cherry-pick 금지, 계산 단위 수동 이식, 마지막에 삭제 목록 검토 |
| logger와 policy 양쪽에서 ON/OFF를 판단 | logger는 측정 프레임만 발행하고 policy만 상태를 보유 |
| 정적 policy와 renderer가 각각 layer를 제어 | renderer만 layer를 소유하도록 EditMode asset test 추가 |
| 직렬화 필드 이름 변경으로 기존 Scene 값 손실 | 가능한 기존 필드 유지, 변경 시 `FormerlySerializedAs` 또는 Scene Builder 명시 설정 |
| 머리 회전이 병진 속도로 오인 | 목 피벗 보정 및 회전 전용 테스트 추가 |
| 손 velocity 노이즈로 오작동 | EMA, 최소 접근 속도, reach gate 및 높은 hand Full 임계값 사용 |
| 동적인 UserState 때문에 threshold가 매 프레임 흔들림 | head/hand별 release threshold와 상태 기반 히스테리시스 적용 |
| emergency가 벽 가까운 정지 사용자를 계속 붙잡음 | 진입과 유지/해제에 접근 속도 조건 적용 |
| hand 원인인데 head 벽 방향을 표시 | 정책 결과에 activation cause와 hazard direction 포함 |
| 소스의 Full 의미와 현재 window 의미가 다름 | 이번 단계의 의미 매핑을 문서화하고 전체 화면 승격은 별도 작업으로 분리 |
| ML 브랜치가 기대하는 threshold API와 불일치 | 이번 단계에서는 설정 타입과 런타임 적용 메서드의 확장 지점만 확보하고 ML 연결은 후속 작업 |

## 10. 롤백 전략

- 기존 재분리 체크포인트와 정적 통합 커밋을 분리한다.
- 계산, 측정, 정책, 렌더링, 문서를 각각 독립 커밋으로 유지한다.
- Quest 실기에서 치명적인 오작동이 있으면 정적 policy 및 renderer 연결 커밋만 되돌린다.
- 동적 파트와 공통 layer는 정적 롤백의 영향을 받지 않아야 한다.
- Scene은 전체 파일 checkout으로 롤백하지 않고 Scene Builder가 생성한 정적 컴포넌트와
  직렬화 변경만 되돌린다.

## 11. 후속 작업

이번 정적 통합이 검증된 뒤 별도 작업으로 진행한다.

1. `main`의 최신 로그 및 개인화 기반 코드와 현재 작업을 재조정
2. ML `quest-handoff`의 세 threshold 출력을 정적 정책 설정에 주입
3. 개인화 적용 전후 설정과 모델 버전 로그 기록
4. `Aware` 방향 cue 구현
5. 후방 벽 위험 UI와 햅틱 구현
6. emergency의 전체 화면 Passthrough 승격 여부 실기 검토
7. 정적 전용 `static-risk` 및 `passthrough-event` 로그 스키마 확정
