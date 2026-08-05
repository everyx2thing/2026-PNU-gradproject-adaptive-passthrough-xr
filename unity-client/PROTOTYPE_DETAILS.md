# Sensor Feature & Risk Calculation Prototype for Adaptive Passthrough XR

Meta Quest 기반 **센서 Feature 계산 및 환경 기반 충돌 위험도 산출 로직**을 검증하기 위한 Unity 프로토타입 상세 문서입니다.

본 문서는 졸업과제 전체 시스템인 **Context-aware Adaptive Passthrough Framework** 중, 사용자가 담당하는 Unity Client 측 구현 범위를 정리합니다. 현재 구현은 Quest에서 직접 수집 가능한 HMD/컨트롤러 움직임과 Scene API 기반 벽 정보를 이용해 환경 기반 충돌 위험도 `R_static_head`과 팔 길이 기반 손 위험도 `R_static_hand`를 계산하고, 사용자 움직임 상태로 Passthrough 활성화 민감도를 조절해 실제 Passthrough Layer를 제어하는 단계까지 진행되어 있습니다.

---

## 0. 핵심 개념


```text
① 헤드셋·손 움직임 측정   →   ② "벽에 부딪힐 위험" 점수 계산   →   ③ 지금 사용자가
   (속도, 벽까지 거리 등)        (R_static_head, R_static_hand)                가만히 있는지/움직이는지 확인
                                                                          ↓
⑤ 화면 UI에 전부 표시   ←   ④ 위험 점수가 "지금 상태에서의 기준"을 넘으면 Passthrough 켬
```

자주 나오는 용어를 미리 정리하면 다음과 같습니다.

| 용어 | 쉬운 설명 |
|---|---|
| `R_static_head` | "머리/몸통이 벽에 부딪힐 위험도"를 0(안전)~1(매우 위험) 사이 숫자로 나타낸 값. 벽까지 거리, 다가가는 속도, 사각지대 여부로 계산됩니다. |
| `R_static_hand` | "손이 벽에 부딪힐 위험도". `R_static_head`과 별개로 계산되고, 팔이 실제로 닿을 수 있는 거리에 있을 때만 값이 생깁니다. |
| `TTC` (Time-To-Collision) | 지금 속도 그대로 계속 다가가면 몇 초 뒤에 벽에 닿는지. 값이 작을수록 위험합니다. |
| `UserState` | 지금 사용자가 얼마나 움직이고 있는지를 0(완전히 정적)~1(빠르게 이동 중) 연속값으로 나타낸 것 (보고서의 `Rstatic`에 해당하는 개념). 머리 이동 속도만 반영하며, 그 자체로는 위험도가 아니고 "Passthrough를 얼마나 민감하게 켤지"를 정하는 데만 씁니다 (§3.2 참고). |
| threshold (임계값) | 위험도가 이 값 이상이 되면 Passthrough를 켠다는 기준선. 사용자가 가만히 있을 때는 기준을 높게(둔감하게), 움직이고 있을 때는 낮게(민감하게) 자동으로 조절됩니다. |
| 히스테리시스 | 켜지는 기준과 꺼지는 기준을 살짝 다르게 둬서, 위험도가 기준선 근처에서 오르락내리락할 때 Passthrough가 깜빡거리지 않게 하는 장치. (예: 0.65 넘으면 켜지고, 0.57 밑으로 내려가야 꺼짐) |
| `WarningLevel` | 최종 개입 강도를 3단계(`None`/`Aware`/`Full`)로 나타낸 값. `Full`일 때만 실제로 Passthrough가 켜집니다. |
| `clamp01(x)` | 값 `x`를 무조건 0~1 사이로 잘라내는 함수. 1을 넘으면 1, 0보다 작으면 0으로 만듭니다. |
| `Lerp(a, b, t)` | `t`(0~1)의 비율만큼 `a`에서 `b`로 선형 보간(중간값 계산)하는 함수. `t=0`이면 `a`, `t=1`이면 `b`, `t=0.5`면 정확히 중간값입니다. |

**아주 간단한 예시**: 벽에서 3m 떨어진 채로 가만히 서 있다가, 초당 1m 속도로 벽을 향해 걸어가기 시작했다고 하면 — 거리가 줄면서 `Rd`(거리 위험도)가 서서히 올라가고, 동시에 "걷고 있다"는 게 감지되어 Passthrough가 켜지는 기준(threshold)이 낮아집니다. 두 효과가 겹쳐서, 벽에 완전히 닿기 1.4~1.8초 전쯤(기본값 기준) 자동으로 Passthrough가 켜지도록 설계되어 있습니다. 

---

## 1. 담당 구현 범위

본 프로토타입은 아래 6가지를 담당합니다. 

1. **센서·공간 정보 수집** — 헤드셋/컨트롤러 움직임과 Quest Room Scene의 벽 정보를 실시간으로 가져옵니다. 
2. **Feature 계산** — 위 정보로 "벽까지 거리", "접근 속도/가속도", "TTC", "사각지대 여부" 같은 값을 계산합니다. 
3. **위험도 계산** — 이 Feature들로 환경 위험도 `R_static_head`과 손 위험도 `R_static_hand`를 구하고, `UserState`(사용자가 얼마나 움직이는지, 0~1 연속값)로 판단 민감도를 조절합니다. `Rdynamic`/`Rintent`(AI/ML 담당)는 아직 미구현이며, 구현되어도 `R_static_head`에 합쳐지지 않고 별도 경로로 추가될 예정입니다. 
4. **Passthrough 판단 및 실제 제어** — 위험도가 기준을 넘으면 실제로 Passthrough 화면을 켜고, 3단계 경고(`WarningLevel`)도 함께 표시합니다. 
5. **실험용 계수 조정 구조** — 모든 기준값·가중치를 Unity Inspector에서 바로 튜닝할 수 있게 구성했습니다.
6. **팀 연결 준비** — AI/ML,  시각화 파트가 나중에 이어붙일 수 있도록 연결 지점만 남겨뒀습니다. 
   - Visualization 파트에서 `WarningLevel.Aware`를 화살표/가장자리 표시 등으로 실제로 그려줄 수 있도록, `CurrentWarningLevel`/`CurrentCombinedRisk`/`CurrentStaticHandRisk`/`StaticHandRiskDirection` public property를 노출해 두었습니다. 이 property를 계승받아 패스스루를 제어할 수 있습니다. `Full`(Passthrough 실제 ON/OFF)은 이미 연결되어 있습니다.


---


## 2. Key Scripts

### `QuestRiskExperimentLogger.cs` 

Scene API 기반 벽 로딩, SCENE 권한 요청, 최근접 벽 탐색, `UserState` 계산, 환경 기반 충돌 위험도(`R_static_head`), 손-벽 충돌 위험도(`R_static_hand`), Passthrough ON/OFF 판단, 2단계 경고(`WarningLevel`) 산출까지 전부 이 한 스크립트에서 처리합니다.

---

## 3. Risk Calculation

현재 코드는 위험도를 하나의 `Rtotal`로 합치지 않고, 서로 다른 두 경로(환경 기반 `R_static_head`, 손 기반 `R_static_hand`)와 긴급 예외 총 3가지로 나눠 각각이 독립적으로 Passthrough를 켤 수 있도록 구성되어 있습니다. 

### 3.1 R_static_head - 머리/몸이 벽에 부딪힐 위험도

"벽이 얼마나 가까운지", "얼마나 빨리 다가가는지", "가속 중인지", "안 보고 있는지(사각지대)" 네 가지를 각각 0~1점으로 매긴 뒤 가중평균한 값입니다.

```text
collisionWeightSum = weightDistance + weightTTC + weightApproachAcceleration + weightBlind

R_static_head = (weightDistance             * Rd
            + weightTTC                  * RTTC
            + weightApproachAcceleration * Ra
            + weightBlind                * Rblind) / collisionWeightSum
```

각 항목의 의미는 다음과 같습니다.

- `Rd = 1 - clamp01(minDist / safeDistanceMeters)`: 벽까지 거리가 가까울수록 ↑
- `RTTC = 1 - clamp01(ttc / safeTimeSeconds)`: 지금 속도로 몇 초 뒤 부딪히는지(TTC)가 짧을수록 ↑
- `Ra = clamp01(towardWallAccel / maxApproachAccel)`: 벽 쪽으로 가속 중이면 ↑ (기본 가중치 0 — 등속 보행에선 항상 0이라 꺼놓음)
- `Rblind`: 벽을 안 보고 있을수록 ↑ (60°/120° 기준 3단계)



### 3.2 UserState — Passthrough를 얼마나 민감하게 켤지

기존의 R_state값입니다. 실험을 통해 **"`R_static_head`의 판단 민감도를 조절하는 Context 값"으로 바뀌었습니다.**

```text
UserState = clamp01(머리 순변위 속도 / thresholdHeadSpeedScale)
```

- **위험도 점수가 아닙니다.** 위에서 계산한 `R_static_head`를 켜는 기준선을 얼마나 낮출지만 정합니다.
- **손 속도는 반영하지 않습니다.** 반영하면 벽 등지고 앉아 손만 흔들어도 `UserState`가 올라가 오작동합니다.
- 0에 가까울수록 정적(거의 정지), 1에 가까울수록 동적(빠르게 이동 중)입니다.

```text
effectiveOnThreshold = Lerp(stableOnThreshold, rapidOnThreshold, UserState)
```

- `UserState = 0` (정적) → 기준선 `stableOnThreshold`(기본 0.65, 둔감, 잘 안 켜짐)
- `UserState = 1` (동적) → 기준선 `rapidOnThreshold`(기본 0.45, 민감, 쉽게 켜짐)
- 그 사이 값이면 두 기준선 사이를 비례 보간

히스테리시스나 최소 유지시간 같은 완충 장치는 없습니다 — `UserState` 자체가 매 프레임 그대로 반영되는 raw 값이고, 대신 이 값으로 계산된 `effectiveOnThreshold`/`effectiveOffThreshold` 사이의 간격(`hysteresisWidth`)이 Passthrough ON/OFF 전환의 깜빡임을 막습니다 (§3.5 참고).

### 3.3 `R_static_hand` — 손이 벽에 부딪힐 위험도

손 속도 자체를 위험도로 쓰지 않고, "그 손이 지금 그 벽에 실제로 닿을 수 있는가"(reach gate, 팔 길이 기준)로 걸러낸 뒤 거리/TTC 위험도에 곱합니다. 팔이 안 닿는 벽은 자동으로 위험도 0.

```text
R_static_hand = max(왼손 위험도, 오른손 위험도)
```

팔 길이는 실행 중 관측된 최대 손-머리 거리로 자동 보정됩니다.

### 3.4 긴급 거리 예외

벽이 매우 가깝고(`emergencyDistance`) **동시에** 그 방향으로 빠르게 접근 중일 때만 기준선 계산 없이 즉시 Passthrough를 켭니다. 거리만으론 발동 안 함 — 벽 옆에 가만히 서 있는 건 위험하지 않다는 전제입니다.

### 3.5 Passthrough Decision — 실제 ON/OFF

```text
passthroughOn = 긴급 경로 || R_static_head ≥ 기준선 || R_static_hand ≥ handFullThreshold
```

셋 중 하나만 만족해도 켜집니다. 꺼질 때는 기준선보다 살짝 낮은 값을 써서(히스테리시스) 경계에서 깜빡이지 않게 합니다. 

### 3.6 WarningLevel — None / Aware / Full

```text
Combined Risk = max(R_static_head, R_static_hand)
anyApproach   = 머리 또는 좌우 손 중 하나라도 벽 쪽으로 awareApproachSpeed 이상 접근 중

WarningLevel = passthroughOn                                 ? Full
             : (anyApproach && CombinedRisk >= awareThreshold) ? Aware
             : None
```
```text
Full  = Passthrough가 실제로 켜진 상태 (이때만 Full)
Aware = 아직 안 켜졌지만 위험이 쌓이고 있다는 신호 (화면은 안 바뀜)
None  = 그 외
```

`Aware`는 신호값만 있고 아직 화면에 그려주는 UI는 없습니다.




## 4. UI Output

실행 중 World-space UI에는 다음 정보가 표시됩니다.

왼쪽 패널 (`labelText`, Scene 로드 후):

- Scene Distance — HMD 좌표 / 가장 가까운 벽 번호 / 거리
- Motion Features — Head Speed/Accel/Angular, Left/Right/Avg Hand Speed, Hand/Head Ratio
- Hand-Wall Distance — 학습된 Arm Reach, 좌/우 각각 wall index/distance/toward speed/min distance
- Wall Approach — Toward Wall Speed(순변위 기준 + 순간값) / Toward Wall Accel / TTC / Approaching Wall 여부

오른쪽 패널 (`riskLabelText`):

- `[User State]` — `UserState`(0=정적, 1=동적) / Head Translation / Head Angular(로그 전용) / Head Accel(로그 전용) / Warmed Up
- `[Collision Risk]` (Scene 로드 후) — `Rd` / `RTTC` / `Ra` / Theta To Wall / `Rblind` / `R_static_head`
- `[Hand Risk]` (Scene 로드 후) — 좌/우 gate/extension/risk, `R_static_hand`(max), Combined Risk + approach → `WarningLevel`, Physical Risk Level
- `[Passthrough Decision]` (Scene 로드 후) — Effective ON/OFF Threshold, Emergency Trigger/Hold, `Adaptive Passthrough State: ON/OFF`, `Last Full cause`



---
## 5. 검증 시나리오 
아래 4가지 상황을 실제 Quest 기기에서 실행해 의도한 대로 동작하는지 확인했습니다.

| # | 상황 | 확인된 동작 | 근거 |
|---|---|---|---|
| 1 | 벽을 향해 등속으로 걸어감 | 벽에 닿기 전에 Passthrough ON (정상) | `Rd`·`RTTC` 상승 → `R_static_head ≥ effectiveOnThreshold` (§3.1, §3.5) |
| 2 | 공간 중간(벽에서 먼 곳)에서 팔을 크게 흔듦 | `WarningLevel: None` 유지, Passthrough 안 켜짐 | 벽이 `personalReachLength` 밖이라 `reachGate = 0` → `R_static_hand = 0` (§3.3). 손 속도는 `UserState`에도 반영 안 됨(§3.2) → 어느 경로도 안 걸림 |
| 3 | 벽을 향해 뒤로 이동(등지고 다가감) | 시나리오 1과 같은 접근 속도인데도 더 일찍 Passthrough ON | 등을 지고 있어 `thetaToWall > 120°` → `Rblind = 0.8`(최댓값) → `R_static_head`가 더 빨리 기준선을 넘음 (§3.1) |
| 4 | 벽에 기대 앉아 손만 움직임(벽과 가까운 상태) | Passthrough는 안 켜지고 `Aware` 신호만 발생 → 컨트롤러가 벽에 거의 닿을 때쯤 ON | 머리가 정적이라 `R_static_head`·`UserState` 낮게 유지 → `anyApproach && CombinedRisk ≥ awareThreshold`로 `Aware`만 발생(§3.6). 손이 `reachGate` 임계 부근까지 다가가야 `R_static_hand ≥ handFullThreshold`로 Full 전환(§3.3, §3.5) |


---
## 6. TODO

1. **`Aware` 신호를 실제로 보여주는 UI 필요**
   - Passthrough 실제 ON/OFF 제어는 이미 `OVRPassthroughLayer.hidden`으로 연결되어 있습니다.
   - 다만 `WarningLevel.Aware`를 화살표/가장자리 표시 등 경량 UI로 실제로 그려주는 소비자는 아직 없고, `CurrentWarningLevel` 등 public 프로퍼티만 노출되어 있습니다.

2. **AI/ML 동적 위험도 연동 필요**
   - `Rdynamic`, `Rintent`는 아직 어떤 계산에도 관여하지 않고 코드에는 주석만 남아 있습니다.
   - 추후 AI/ML 파트에서 계산된 값을 받아 `R_static_head`과 별도 경로로 결합해야 합니다. 이후 static 파트와 dynamic 파트를 input으로 받는 하나의 패스스루 제어 레이어를 구현해야합니다.

3. **개인화 모델 연동 필요**
   - 사용자의 Passthrough 작동 이력과 수동 개입 여부를 기반으로 가중치와 threshold를 조정하는 구조는 아직 없습니다.
   
4. **다양한 시각화 방식 실험 필요**
   - Directional Passthrough, Augmented Virtuality, Volumetric Cue 등 다양한 시각화 방식과 연결하여 비교 실험을 진행해야 합니다.


