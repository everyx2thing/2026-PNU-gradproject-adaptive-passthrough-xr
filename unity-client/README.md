
## 실험 앱: 3조건 비교용 총게임

참가자가 Meta Quest 3로 날아오는 공/폭탄을 총으로 쏘거나 피하는 슈터 게임을 하는 동안, 서로 다른 3가지 Passthrough 방식을 겪게 해서 몰입감 및 안전성을 비교하기 위한 앱입니다. ExperimentGameTest씬에 구현된 게임은 로직이 잘 작동하는지 확인하기 위한 개별 씬으로, 세 조건 모두 패스스루 없음으로 작동합니다. 다음 세 조건이 작동하려면 SampleScene에 이동 및 연결이 필요합니다.

- **A. 패스스루 없음** — Passthrough Layer를 강제로 숨김
- **B. 기존 Guardian 기반** — 커스텀 정책 없이 Quest OS 기본 Guardian 트리거만 동작
- **C. Adaptive(새 방식)** — `SelectivePassthroughController` 스택을 그대로 켜둠

### 실행

1. Unity Hub에서 프로젝트 열기
2. `Assets/Scenes/ExperimentGameTest.unity` 실행 (실험 게임은 이 씬 기준으로 구성되어 있습니다 — `SampleScene.unity`는 코어 시스템 프로토타입용 씬입니다)
3. Android 플랫폼으로 전환 후 Quest 3에 Build & Run
4. (앱 실행 전) 헤드셋에서 Space Setup(Room Setup) 완료

### 전체 흐름

1. **메뉴**에서 참가자에게 중립적으로 보이는 버튼 3개 중 하나를 실험 진행자가 누름
2. 눌린 버튼에 매핑된 조건(A/B/C)으로 **Passthrough 전환**
3. 해당 조건의 고정 스케줄에 따라 **라운드 진행**(최대 5분, 또는 발사체 소진 시 조기 종료)
4. 라운드 종료 → 메뉴로 복귀, 버튼에 "이미 경험함" 표시
5. 다음 참가자 전 **리셋 버튼**으로 "이미 경험함" 표시만 초기화

### 컴포넌트 구조 (`Assets/Scripts/Experiment/`)

| 스크립트 | 역할 |
|---|---|
| `ExperimentMenuController` | 3개 라운드 버튼(조건·스케줄 매핑은 인스펙터에서 슬롯별로 설정) + 리셋 버튼. 참가자에게는 조건을 노출하지 않음 |
| `ExperimentRoundController` | 라운드 오케스트레이션 — 조건 적용 → 발사체 스폰 시작 → 최대 라운드 시간 타임아웃 관리 |
| *`PassthroughConditionSwitcher` | A/B/C 조건에 맞게 코어 컴포넌트의 `enabled`/`hidden`만 토글(내부 위험도 계산 로직은 미수정) |
| `ExperimentBallSpawner` | 조건별 `ProjectileScheduleSet`을 시간순으로 재생해 공/폭탄 생성. 스폰 위치는 씬에 배치된 9개 방향별 스포너 오브젝트의 실제 위치 사용 |
| `ExperimentBall` | 개별 발사체 동작 — 목표를 향해 이동, 총에 맞으면(`RegisterShotHit`) 또는 참가자 몸에 닿으면(`RegisterBodyHit`) 결과 처리 |
| `ExperimentGun` | 오른손에 고정된 히트스캔 총 — 트리거 시 Raycast 판정, 조준 중 실시간 레티클, 머즐 플래시, 트레이서 |
| `ExperimentScoreSystem` | 점수 규칙 적용 및 이벤트별 효과음 재생 |
| `ExperimentScoreHud` | 중앙 상단 월드스페이스 점수 HUD |
| `ExperimentThreatIndicatorController` | 시야 밖 발사체를 화면 가장자리 화살표로 표시(공=노랑/폭탄=빨강) — 사양 외 추가 기능 |
| `BallSpawnEntry` / `ExperimentCondition` | 발사체 한 개, 실험 조건을 나타내는 데이터 타입 |
| `ProjectileScheduleSet` | 조건별 고정 발사체 스케줄을 담는 ScriptableObject (`Data/ProjectileScheduleSet_ConditionA/B/C.asset`) |

*PassthroughConditionSwitcher 는 임의 script로 수정이 필요합니다!

### 게임의 특징
- 총으로 쏠 수 있는 공은 과녁, 총으로 쏘면 안되는 공은 폭탄으로 3D오브젝트를 연결했습니다.
- 각 발사체가 스폰될 때, 스폰되는 방향에서 소리가 발생합니다.
- 총으로 과녁 또는 폭탄을 쐈을 때 소리 및 시각 이펙트가 발생하며, 과녁 또는 폭탄이 몸에 맞았을 때 소리가 발생합니다.

### 발사체 스폰 구조

- **방향**: 참가자 정면 기준 좌우 180도 반원에 9개 스포너(`Left, LeftMid, FrontLeft, FrontLeftMid, Front, FrontRightMid, FrontRight, RightMid, Right`, 22.5도 간격)를 씬에 실제로 배치해 그 위치에서 스폰. 뒤쪽 스포너는 없음.
- **타이밍**: 라운드 전체에서 "5초 박자" 리듬 유지, 구간별로 박자당 등장 개수가 늘어남
  - 0:00–1:00: 박자당 1개 (12개)
  - 1:00–3:00: 기본 1개, 30초마다(4회) 2개 동시 (28개)
  - 3:00–5:00: 기본 1개, 30초마다(4회) 3개 동시 (32개)
  - 총 **72개**, 일반 공:폭탄 ≈ 7:3
- 조건 A/B/C는 타이밍/개수 구조는 동일하고 방향 패턴만 달라서 참가자가 패턴을 외워 유리해지지 않게 함
- 발사체 하나하나의 방향/속도/타입/스폰 시각은 각 `ProjectileScheduleSet_Condition*.asset`을 클릭하면 인스펙터에서 직접 조정 가능
- 라운드 전체 길이는 `ExperimentRoundController`의 `Max Round Seconds` 값(기본 300초)으로 조정

### 점수 규칙

| 이벤트 | 점수 변화 |
|---|---|
| 일반 공을 총으로 맞힘 | +100 |
| 일반 공이 몸에 닿음 | -100 |
| 폭탄을 총으로 쏨(폭발) | 현재 점수의 절반 상실 |
| 폭탄이 몸에 닿음 | 점수 전액 상실(0) |

라운드 시작 시 0으로 초기화되며, 별도 로그/저장은 하지 않습니다(설문은 앱 밖에서 진행).


### AdaptivePassthrough 코어 로직과의 격리

발사체/총/점수 관련 스크립트는 `AdaptivePassthrough` 네임스페이스를 전혀 참조하지 않습니다. 유일한 접점은 `PassthroughConditionSwitcher`이며, 이마저도 코어 컴포넌트의 `enabled` 여부와 Passthrough Layer의 `hidden` 플래그만 토글할 뿐 내부 위험도 계산식은 건드리지 않습니다.

### SampleScene으로 이전 필요

실제 디바이스 빌드 기준으로는 `SampleScene`이 곧 실험 앱입니다. 이어서 게임 로직을 마저 옮기실 때 아래 사항 참고해주세요.

- **프리팹 참조는 자동으로 안 채워집니다.** `ExperimentBallSpawner`의 `Bomb Ball Prefab` / `Target Ball Prefab`, `ExperimentBall`의 `Spawn Projectile Clip` / `Bomb Explosion Effect Prefab` 필드는 다른 스크립트들과 달리 `FindAnyObjectByType` 같은 런타임 fallback이 없습니다. `Experiment Systems`를 복사해서 옮길 때 인스펙터에서 이 필드들이 비어있지 않은지 꼭 확인해주세요. (비어있으면 라운드가 시작돼도 볼이 아예 안 스폰되거나, 폴백 스피어로만 스폰되고 스폰 사운드가 안 남.)
- **대부분의 다른 참조는 `FindAnyObjectByType`으로 씬에서 자동으로 찾습니다** (`cameraRig`, `conditionSwitcher`, `ballSpawner`, `roundController`, `audioSource`, `boundaryVisibility` 등 — 인스펙터에 null로 보여도 정상). 단, 이 방식은 씬에 해당 타입 컴포넌트가 **정확히 하나만** 있다는 걸 전제로 하므로, 복사·붙여넣기 과정에서 `Experiment Systems`나 `Adaptive Dynamic Risk System` 같은 오브젝트가 중복으로 남지 않도록 주의해주세요. 중복되면 어느 인스턴스가 잡힐지 보장이 안 됩니다.
- `PassthroughConditionSwitcher.cs`를 고쳐야합니다.** 라운드 전환 시마다 `OVRManager.shouldBoundaryVisibilityBeSuppressed`를 직접 강제로 세팅하도록 고쳐서(`ForceBoundarySuppressed`), 라운드 1(NoPassthrough)·3(Adaptive)은 Guardian 항상 꺼짐, 라운드 2(GuardianDefault)만 켜짐이 순서와 무관하게 보장되도록 했으나, 실제 실행을 해봤을 때 세 조건 모두 경계가 계속 꺼짐으로 유지되었습니다.
- 위 항목들은 `SampleScene`에서 실제로 라운드를 돌려보며 확인한 내용입니다. 세 라운드를 한 번씩 돌려서 (1) 과녁/폭탄이 정상 스폰되는지, (2) 스폰 사운드 및 이펙트가 발생하는지, (3) 라운드 2에서만 Guardian이 뜨는지 확인해주세요.

