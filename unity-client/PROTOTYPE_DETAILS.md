# 정적 위험도 알고리즘 및 실험 게임 구현 상세

이 문서는 Adaptive Passthrough 프로젝트의 **정적 위험도 계산·정책과 사용자 실험용 게임 개발**을 설명합니다. 전체 Unity 클라이언트의 기능과 설치 방법은 [README](README.md)를 참고합니다.

> 기준: 2026-09-28 작업 폴더의 코드, SampleScene과 실험 게임 Prefab. 초기 벽 기반 프로토타입과 현재 통합 경로를 구분합니다. 

## 1. 목적과 책임 분리

정적 위험 분석은 벽·가구·낮은 장애물에 가까워지거나 그쪽으로 움직일 때 필요한 영역을 보여 주는 것을 목적으로 합니다. 거리, 접근 속도와 TTC를 함께 사용하며 머리·양손·낮은 장애물을 독립적으로 판단합니다.

실험 게임은 조준·발사·회피 과제를 수행하는 동안 세 가지 안전 출력 조건을 비교하는 환경입니다. 게임 점수는 과제 피드백이며 위험도 계산의 입력으로 사용하지 않습니다.

```text
공간·움직임 측정 → StaticBoundaryRiskFrame
    → StaticPassthroughPolicyController
    → 머리 / 왼손 / 오른손 / 낮은 장애물별 판정
    → SelectivePassthroughController 및 안전 피드백

실험 메뉴·튜토리얼·라운드 → 게임 과제
    → 조건별 표시·Guardian override
```

## 2. 정적 공간·움직임 측정

### 2.1 현재 입력 경로

현재 통합 경로의 공급자는 `QuestSpatialObstacleProvider`입니다. Environment Depth와 Room Scene 측정에 유효성·신뢰도·사용자 몸에 대한 자기 검출 검사를 적용하고 공간 표면과 위험 방향을 구성합니다.

- 머리·양손과 이동 방향의 낮은 장애물 측정값을 분리합니다.
- 거리·접근 속도·TTC와 표시용 표면 정보를 함께 전달합니다.
- 확인된 머리 근접 overlap을 긴급 조건으로 처리합니다.
- 손 overlap을 임의의 0m 거리로 합성하는 경로는 사용하지 않습니다.

`QuestRiskExperimentLogger`는 이 공급자가 연결되어 있으면 공급자의 프레임을 사용합니다. 초기 Room Scene 전용 계산도 코드에 남아 있으므로 Inspector의 옛 필드만으로 현재 위험식을 판단하면 안 됩니다.

### 2.2 접근 속도와 사용자 상태

위험 방향 단위벡터를 n, 머리 속도를 v라 할 때:

```text
v_toward = max(0, dot(v, n))
UserState = clamp01((v_toward - 0.10) / 0.90)
```

머리·낮은 장애물 중 위험도가 높은 유효 대상의 방향을 사용합니다. 장애물과 무관한 옆 방향 움직임까지 같은 접근 상태로 취급하지 않도록 합니다.

거리 변화율을 사용할 때는 운동학적 접근 속도로 증가량을 제한합니다. 대상 방향이 크게 바뀌거나 거리 이력이 초기화되면 운동학적 값을 사용하여 거리 점프로 인한 과도한 접근 속도를 억제합니다.

## 3. 정적 위험도 계산

`clamp01(x)`는 값을 0~1로 제한하는 함수입니다. 접근 속도는 장애물 방향의 양수 성분입니다.

### 3.1 공통 성분

```text
R_distance = clamp01(1 - distance / safeDistance)
TTC = distance / closingSpeed
R_ttc = clamp01(1 - TTC / safeTime)

x = clamp01((closingSpeed - startSpeed) / (fullSpeed - startSpeed))
R_speed = x² × (3 - 2x)
```

접근 속도가 최소 조건 이하이면 TTC 위험은 0입니다. 속도 위험에는 부드러운 보간을 적용합니다.

### 3.2 머리와 낮은 장애물

현재 공간 공급자의 `ToHeadRisk`는 머리와 낮은 장애물에 다음 계산을 적용합니다.

| 항목 | 값 |
|---|---:|
| 거리 기준 | 1.50m |
| TTC 기준 | 2.00초 |
| TTC 최소 접근 속도 | 0.01m/s |
| 속도 위험 시작 / 최대 | 0.05 / 0.80m/s |
| 사각 성분 입력 | 0.20 고정 |

```text
R_head = 0.45 × R_distance
       + 0.30 × R_speed
       + 0.20 × R_ttc
       + 0.05 × 0.20
```

긴급 측정 조건에서는 위험을 1로 올립니다. 현재 융합 경로의 사각 성분은 고정값이며, 초기 프로토타입처럼 시선 각도별 값을 계산하지 않습니다. 접근 가속도도 이 경로의 최종 가중식에는 들어가지 않습니다.

### 3.3 양손

왼손·오른손을 각각 계산합니다. 손이 더 뻗을 수 있는 거리를 반영하는 Reach Gate와 직접 근접 위험을 함께 사용합니다.

```text
extension = distance(HMD, hand)
remainingReach = max(0, 0.75 - extension)
gate = clamp01((remainingReach - obstacleDistance + 0.15) / 0.15)

R_reach = gate × (0.35 × R_distance + 0.40 × R_speed + 0.25 × R_ttc)
R_direct = 0.60 × R_distance + 0.25 × R_speed + 0.15 × R_ttc
R_hand = max(R_reach, R_direct)
```

| 항목 | 값 |
|---|---:|
| 손 거리 기준 | 0.75m |
| 손 TTC 기준 | 1.25초 |
| TTC 최소 접근 속도 | 0.01m/s |
| 속도 위험 시작 / 최대 | 0.10 / 1.50m/s |
| Reach 기준 / 전이 폭 | 0.75 / 0.15m |

직접 근접 경로는 이미 뻗은 손이 장애물에 가까워졌을 때 Reach Gate만으로 위험이 과도하게 낮아지는 것을 보완합니다. 긴급 측정 조건은 별도로 처리합니다.

### 3.4 초기 벽 기반 프로토타입과의 차이

초기 구현은 Scene API의 벽과 HMD·컨트롤러를 중심으로 거리, 접근 가속도, 시선 각도와 `Rcollision`, `Rstate`, `Rtotal`을 분석했습니다.

현재는 공간 측정과 정책을 분리하고, 정적·동적 정책이 각각 표시를 요청합니다. 남아 있는 Room Scene 전용 경로에는 목 피벗 보정, 0.5초 순이동 창, 시선 각도별 사각 위험과 별도 거리·시간 설정이 있습니다. 이는 기본 씬의 공간 융합 경로와 구분해야 합니다.

## 4. 정적 Passthrough 정책

`StaticBoundaryHazardPolicy`는 머리·왼손·오른손·낮은 장애물의 활성 상태와 유지시간을 개별 관리합니다.

### 4.1 진입·유지·해제

현재 기본 씬과 개인화 기본 상수:

| 설정 | 값 |
|---|---:|
| 안정 상태 ON 임계값 | 0.50 |
| 빠른 접근 상태 ON 임계값 | 0.45 |
| 손 ON 임계값 | 0.40 |
| 히스테리시스 폭 | 0.08 |
| 최소 유지시간 | 1.50초 |
| 해제 지연 | 0.35초 |
| 긴급 근접 거리 | 0.25m |

```text
headOn = lerp(0.50, 0.45, UserState)
headOff = headOn - 0.08
handOn = 0.40
handOff = 0.32
```

머리·낮은 장애물은 가변 임계값을, 손은 손 임계값을 사용합니다. 해제 조건을 충족해도 최소 유지시간과 해제 지연을 적용합니다. 입력 유실에도 유지·해제 로직을 거칩니다.

근접 거리와 확인된 머리 overlap은 일반 임계값과 별도로 긴급 진입을 유발합니다. 채널 비활성화나 실험 조건에 의한 표시 억제와는 구분됩니다.

### 4.2 표시와 개인화

정적 정책은 원인과 표면 정보를 발행하고, 렌더러가 벽·가구 창이나 낮은 장애물 안내를 구성합니다. 내부 경고 단계 이름인 `Full`은 전체 화면 Passthrough 전환을 의미하지 않습니다.

개인화는 `ApplyPersonalizedThresholds`로 진입 임계값을 바꿉니다. 갱신 시 정책을 초기화하지 않아 진행 중인 긴급·최소 유지 상태를 보존합니다. 개인화 기능과 사용법은 [Unity README](README.md)를 참고합니다.

## 5. 실험 게임 구성

실제 앱은 `SampleScene`에서 실행하며 `ExperimentGameRoot.prefab`이 게임을 구성합니다. `ExperimentGameTest`는 원본 배치·참고용으로 기본 빌드에 포함하지 않습니다.

| 컴포넌트 | 역할 |
|---|---|
| ExperimentMenuController | 튜토리얼·라운드 선택과 메뉴 표시 |
| ExperimentTutorialController | 단계별 조준·발사·회피 교육 |
| ExperimentRoundController | 라운드 시작·시간·종료와 식별자 |
| PassthroughConditionSwitcher | 조건별 표시·Guardian override |
| ProjectileScheduleSet | 발사 시간·방향·종류·속도 일정 |
| ExperimentBallSpawner | 참가자 기준 배치와 발사체 생성 |
| ExperimentGun | 오른손 조준·트리거 입력, raycast 명중 |
| ExperimentBall | 직선 비행, 명중·몸 충돌·소멸 |
| ExperimentScoreSystem | 점수와 보상·벌점 효과음 |

### 5.1 비교 조건

| 조건 | 커스텀 안전 출력 | Guardian 요청 |
|---|---|---|
| Round 1 / GuardianDefault | 억제 | 표시 |
| Round 2 / StaticOnly | 정적 출력 | 숨김 |
| Round 3 / StaticAndDynamic | 정적·동적 출력 | 숨김 |


### 5.2 발사 일정과 좌표

- 라운드 시간은 현재 코드·Prefab 기준 **305초**입니다.
- 기본 설계는 300초 동안의 72개 발사 항목과 마지막 발사체 처리 여유시간입니다. 실제 항목은 조건별 `ProjectileScheduleSet_ConditionA/B/C.asset`을 기준으로 합니다.
- 9개 생성 위치는 라운드 시작 시 HMD 위치와 수평 방향에 맞춥니다.
- 생성 시점의 HMD 위치·높이 오프셋을 목표로 직선 비행하며, 이후 사용자를 따라 방향을 바꾸지 않습니다.
- 마지막 발사체가 일찍 사라져도 설정된 라운드 시간을 단축하지 않습니다.

### 5.3 입력과 충돌

총은 라운드 진행 중 또는 튜토리얼 활성 상태에서 발사할 수 있습니다. 오른손 검지 트리거 입력과 raycast로 명중을 판정합니다.

몸 충돌은 HMD 기준 수평 반경과 수직 범위의 원기둥 형태 근사 판정입니다. 코드 기본값은 반경 0.40m, 머리 위 0.15m부터 아래 1.50m까지이며 실제 Prefab 값으로 조정할 수 있습니다. 전신 추적 기반 충돌은 아닙니다.

### 5.4 점수 규칙

| 사건 | 점수 변화 |
|---|---|
| 과녁 명중 | +100 |
| 과녁이 몸에 닿음 | -100, 최소 0 |
| 폭탄을 쏨 | 현재 점수를 정수 나눗셈으로 절반 처리 |
| 폭탄이 몸에 닿음 | 0으로 초기화 |

점수는 라운드마다 초기화됩니다. `ExperimentScoreSystem` 자체는 영구 저장이나 점수 로깅을 하지 않으므로 연구 지표로 활용하려면 별도 수집 경로가 필요합니다.

### 5.5 튜토리얼

1. **조준·발사:** 정면 과녁으로 트리거와 보상 규칙을 익힙니다.
2. **이동 과녁:** 날아오는 과녁 명중과 몸 충돌 벌점을 익힙니다.
3. **폭탄 회피:** 과녁과 폭탄을 구별하고 쏘거나 몸에 닿지 않도록 회피합니다. 실패 시 다시 시도합니다.
4. **종합 연습:** 앞서 익힌 과제를 함께 수행합니다.

완료 화면과 메뉴 복귀를 제공하며 발사체 명중·몸 충돌 이벤트로 진행을 판정합니다.

## 6. 검증과 실험 운영

자동 테스트는 위험 계산·정책 상태 전이, 조건 매핑, 점수 규칙과 게임 통합을 검사합니다. 이 문서 작성에서는 코드를 대조했으며 테스트와 기기 빌드를 새로 실행하지 않았습니다.

실기 확인 항목:

- 머리·양손·낮은 장애물 접근에 해당 위험 채널이 반응하는지
- 임계값 주변에서 표시가 반복 점멸하지 않는지
- 긴급 근접·입력 유실 시 유지·해제가 의도대로 동작하는지
- 튜토리얼 단계·점수·종료·재시작이 정상인지
- 세 조건의 출력과 Guardian 상태가 일치하는지
- 참가자별 시작 위치·높이와 라운드 시간·발사 일정이 일관되는지
- 조건 사이에서 ML·기기 설정이 통제되는지

구현 설명과 사용자 실험 결과는 구분합니다. 참가자 수, 설문 결과와 통계 분석은 프로젝트 보고서 및 별도 실험 자료를 기준으로 관리합니다.

## 7. 구현 근거

- [공간 측정·융합과 현재 위험 계산](Assets/Scripts/AdaptivePassthrough/Quest/QuestSpatialObstacleProvider.cs)
- [공통 위험 계산식](Assets/Scripts/AdaptivePassthrough/Core/StaticBoundaryRiskMath.cs)
- [채널별 정적 정책](Assets/Scripts/AdaptivePassthrough/Core/StaticBoundaryHazardPolicy.cs)
- [정적 정책 컨트롤러](Assets/Scripts/StaticPassthroughPolicyController.cs)
- [초기 벽 계산 및 측정 어댑터](Assets/Scripts/QuestRiskExperimentLogger.cs)
- [현재 개인화 기본값](Assets/Scripts/AdaptivePassthrough/Core/PersonalizationModels.cs)
- [라운드 제어](Assets/Scripts/Experiment/ExperimentRoundController.cs)
- [튜토리얼](Assets/Scripts/Experiment/ExperimentTutorialController.cs)
- [조건 매핑과 점수 규칙](Assets/Scripts/AdaptivePassthrough/ExperimentRuntimeOverrides.cs)
- [실험 게임 Prefab](Assets/Prefabs/Experiment/ExperimentGameRoot.prefab)
