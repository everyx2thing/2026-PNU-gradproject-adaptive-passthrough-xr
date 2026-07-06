# Sensor Feature & Risk Calculation Prototype for Adaptive Passthrough XR

Meta Quest 기반 **센서 Feature 계산 및 정적 경계 위험도 산출 로직**을 검증하기 위한 Unity 프로토타입 상세 문서입니다.

본 문서는 졸업과제 전체 시스템인 **Context-aware Adaptive Passthrough Framework** 중, 사용자가 담당하는 Unity Client 측 구현 범위를 정리합니다. 현재 구현은 전체 시스템을 완성한 것이 아니라, Quest에서 직접 수집 가능한 HMD/컨트롤러 움직임과 Scene API 기반 벽 정보를 이용해 `Rcollision`, `Rstate`, `Rtotal`을 계산하고 Passthrough 활성화 판단값을 UI에 표시하는 단계입니다.

---

## 1. 담당 구현 범위

본 프로토타입에서 담당하는 핵심 범위는 다음과 같습니다.

1. **Meta Quest 기반 센서 및 공간 정보 수집**
   - HMD 위치/회전, 컨트롤러 위치 데이터를 실시간으로 수집합니다.
   - Quest의 Space Setup으로 생성된 Room Scene 정보를 Scene API로 불러옵니다.
   - `WallFace`, `InvisibleWallFace` 앵커를 필터링하여 실제 공간의 벽 위치와 법선 벡터를 사용합니다.

2. **위험도 산출용 Feature 계산**
   - HMD 속도, 가속도, 각속도를 계산합니다.
   - 좌우 컨트롤러 속도, 손 평균 속도, Hand/Head Ratio를 계산합니다.
   - 가장 가까운 벽까지의 거리, 벽 방향 접근 속도, 접근 가속도, TTC를 계산합니다.
   - HMD 시선 방향과 벽 방향 사이의 각도를 이용해 사각지대 위험도 `Rblind`를 계산합니다.

3. **정적 경계 기반 위험도 계산**
   - 거리 기반 위험도 `Rd`, TTC 기반 위험도 `RTTC`, 접근 가속도 기반 위험도 `Ra`, 사각지대 위험도 `Rblind`를 결합하여 `Rcollision`을 계산합니다.
   - 사용자 움직임 상태를 `Static`, `Dynamic`, `Agitated`로 분류하고 이를 `Rstate`로 수치화합니다.
   - 현재는 `Rdynamic`, `Rintent`를 0으로 고정하고, `Rcollision`과 `Rstate` 중심으로 `Rtotal`을 계산합니다.

4. **Passthrough 판단 결과 표시**
   - `Rtotal`이 설정된 threshold 이상이면 Passthrough가 필요하다고 판단합니다.
   - 현재 단계에서는 실제 Passthrough Layer를 직접 제어하지 않고, `Passthrough Decision: ON/OFF`를 UI에 표시합니다.

5. **실험용 계수 조정 구조 구성**
   - `safeDistance`, `safeTime`, `maxApproachAccel`, 위험도 가중치, `passthroughOnThreshold` 등을 Unity Inspector에서 수정할 수 있도록 구성합니다.
   - 실제 Quest 실험을 통해 위험도 계산식이 너무 민감하거나 둔감하지 않도록 계수 튜닝을 진행할 예정입니다.

6. **팀원 파트와의 연결 준비**
   - AI/ML 파트에서 추후 계산할 `Rdynamic`, `Rintent`가 `Rtotal`에 들어올 수 있도록 구조를 유지합니다.
   - Backend/Data/Dashboard 파트에서 사용할 수 있도록 주요 Feature, 위험도 값, Passthrough 판단 결과를 로그로 남길 수 있는 형태로 확장할 예정입니다.
   - Visualization 파트에서는 현재의 `Passthrough Decision` 값을 실제 Passthrough ON/OFF 또는 시각화 방식 선택 로직과 연결할 예정입니다.

---

## 2. Requirements

- Unity **6000.4.2f1** (Unity 6)
- Android Build Support 모듈
- Meta Quest 계열 헤드셋
  - 현재 개발 및 테스트 기준: Meta Quest 환경
  - 기기별 Scene API / Passthrough 동작은 SDK 및 기기 버전에 따라 추가 검증 필요
- 헤드셋 개발자 모드 활성화
- 헤드셋에서 **Space Setup(Room Setup)** 완료 필요
  - Scene API가 Room Scene의 벽 정보를 가져오기 위해 필요합니다.

---

## 3. Packages / Dependencies

`Packages/manifest.json` 기준 주요 패키지는 다음과 같습니다.

- `com.meta.xr.sdk.core` — OVRManager, OVRCameraRig, Scene API 관련 기능
- `com.meta.xr.sdk.interaction.ovr` — Meta XR Interaction SDK
- `com.unity.xr.management`, `com.unity.xr.openxr` — XR Plug-in Management 및 OpenXR
- `com.unity.render-pipelines.universal` — URP
- `com.unity.inputsystem` — Unity Input System
- `com.coplaydev.unity-mcp`, `com.meta.xr.unity-mcp.extension` — 개발 편의용 AI/MCP 툴링

전체 버전 정보는 `Packages/manifest.json`, `Packages/packages-lock.json`을 참고합니다.

---

## 4. Project Structure

Unity 프로젝트는 저장소 내 `unity-client/` 폴더에 위치하는 것을 기준으로 합니다.

```text
unity-client/
├─ Assets/
│  ├─ Scenes/
│  │  └─ SampleScene.unity
│  ├─ Scripts/
│  │  ├─ QuestBoundaryLogger.cs
│  │  ├─ QuestSceneDistanceLogger.cs
│  │  └─ QuestRiskExperimentLogger.cs
│  ├─ Settings/
│  ├─ Resources/
│  └─ Plugins/Android/
├─ Packages/
├─ ProjectSettings/
├─ README.md
└─ PROTOTYPE_DETAILS.md
```

저장소에는 Unity 프로젝트 실행에 필요한 `Assets/`, `Packages/`, `ProjectSettings/`를 포함합니다.  
`Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`, `Builds/`, `*.apk`, `*.csproj`, `*.sln` 등은 로컬 생성 파일이므로 커밋하지 않습니다.

---

## 5. Key Scripts

### `QuestBoundaryLogger.cs`

Guardian Boundary API를 검토하기 위해 작성한 초기 실험 스크립트입니다.

- Guardian 설정 여부 확인
- Play Area 크기 확인
- Boundary geometry 로깅
- 최신 SDK 환경에서 Boundary API 사용의 제약을 확인하기 위한 기준 코드

현재 핵심 구현에서는 Scene API 기반 구조를 사용하므로 이 스크립트는 보존용 또는 비교용에 가깝습니다.

### `QuestSceneDistanceLogger.cs`

Scene API 기반 벽 거리 및 모션 Feature 계산을 검증하기 위한 기준선 스크립트입니다.

- `com.oculus.permission.USE_SCENE` 권한 요청
- Room Anchor 및 Wall Anchor 조회
- `WallFace`, `InvisibleWallFace` 필터링
- HMD와 가장 가까운 벽 사이의 거리 계산
- HMD 속도, 가속도, 각속도 계산
- 손 속도, Hand/Head Ratio 계산
- 벽 방향 접근 속도와 TTC 계산

이 파일은 위험도 계산을 붙이기 전 단계의 기준선 역할을 합니다.

### `QuestRiskExperimentLogger.cs`

현재 핵심 스크립트입니다.  
`QuestSceneDistanceLogger.cs`의 벽 거리 및 모션 Feature 계산 구조를 기반으로, 위험도 계산과 Passthrough 판단 로직을 추가했습니다.

주요 기능은 다음과 같습니다.

- Scene API 기반 벽 정보 수집
- HMD/컨트롤러 움직임 Feature 계산
- `Rd`, `RTTC`, `Ra`, `Rblind` 계산
- `Rcollision`, `Rstate`, `Rtotal` 계산
- `Rtotal` 기반 Passthrough 판단
- World-space UI에 Feature, 위험도, 판단 결과 표시
- Inspector 기반 실험용 계수 수정

---

## 6. Risk Calculation

현재 코드는 보고서의 최종 위험도 구조를 유지하되, 구현 범위를 `Rcollision`과 `Rstate`로 제한합니다.  
`Rdynamic`, `Rintent`는 AI/ML 파트 구현 전 단계이므로 0으로 고정합니다.

### 6.1 Collision Risk

코드는 가중합을 그대로 사용하지 않고, 가중치 합으로 나누어 정규화합니다.

```text
collisionWeightSum = weightDistance + weightTTC + weightApproachAccel + weightBlind

Rcollision = (weightDistance      * Rd
            + weightTTC           * RTTC
            + weightApproachAccel * Ra
            + weightBlind         * Rblind) / collisionWeightSum
```

각 항목의 의미는 다음과 같습니다.

- `Rd`: 벽까지의 최소 거리 기반 위험도
- `RTTC`: Time-To-Collision 기반 위험도
- `Ra`: 벽 방향 접근 가속도 기반 위험도
- `Rblind`: 시선 방향과 벽 방향 사이 각도 기반 사각지대 위험도

### 6.2 User State Risk

코드의 `Rstate`는 보고서의 `Rstatic`에 해당하는 구현 변수명입니다.

```text
Static   → Rstate = 0.0
Dynamic  → Rstate = 0.5
Agitated → Rstate = 1.0
```

현재 상태 분류는 HMD 속도, 가속도, 각속도의 순간값을 기준으로 동작합니다.  
보고서에서 제안한 이동 지속 시간 조건과 최근 n프레임 평균/최대값 기반 안정화 로직은 아직 구현 전입니다.

### 6.3 Total Risk

```text
totalWeightSum = weightCollisionTotal + weightStateTotal + weightDynamicTotal + weightIntentTotal

Rtotal = (weightCollisionTotal * Rcollision
        + weightStateTotal     * Rstate
        + weightDynamicTotal   * Rdynamic
        + weightIntentTotal    * Rintent) / totalWeightSum
```

현재는 다음과 같이 동작합니다.

```text
Rdynamic = 0
Rintent  = 0
```

즉 현재 프로토타입의 `Rtotal`은 정적 경계 위험도와 사용자 상태 위험도를 중심으로 계산됩니다.

### 6.4 Passthrough Decision

```text
shouldEnablePassthrough = Rtotal >= passthroughOnThreshold
```

현재는 실제 Passthrough Layer를 제어하지 않고, 판단 결과만 UI에 표시합니다.

```text
Passthrough Decision: ON
Passthrough Decision: OFF
```

---

## 7. Inspector Parameters

위험도 계산에 필요한 주요 계수는 Unity Inspector에서 수정할 수 있습니다.  
이를 통해 실제 헤드셋 실험 중 계수를 바꿔가며 위험도 반응을 확인할 수 있습니다.

| Header | Field |
|---|---|
| User State Thresholds | `staticHeadSpeedThreshold`, `staticHeadAccelThreshold`, `staticHeadAngularThreshold`, `agitatedHeadSpeedThreshold`, `agitatedHeadAccelThreshold`, `agitatedHeadAngularThreshold` |
| Risk Parameters | `safeDistance`, `safeTime`, `maxApproachAccel` |
| Collision Risk Weights | `weightDistance`, `weightTTC`, `weightApproachAccel`, `weightBlind` |
| Total Risk Weights | `weightCollisionTotal`, `weightStateTotal`, `weightDynamicTotal`, `weightIntentTotal` |
| Passthrough Decision | `passthroughOnThreshold` |

Inspector에서 조정 가능한 값들은 두 종류로 나뉩니다.

- 가중치와 Passthrough 판단 기준값은 0~1 사이에서 조정하는 값이므로 Unity Inspector에서 슬라이더로 표시됩니다.
- 거리, 시간, 속도, 가속도 기준값은 실험 상황에 따라 1보다 큰 값도 필요하므로 일반 숫자 입력칸으로 표시됩니다.

---

## 8. UI Output

실행 중 World-space UI에는 다음 정보가 표시됩니다.

왼쪽 패널:

- Scene Distance
- HMD 위치
- 가장 가까운 벽 번호
- 벽까지의 거리
- Head Speed / Accel / Angular Speed
- Left / Right Hand Speed
- Hand Avg Speed
- Hand/Head Ratio
- Toward Wall Speed
- Toward Wall Accel
- TTC
- Approaching Wall 여부

오른쪽 패널:

- User State
- `Rstate`
- `Rd`, `RTTC`, `Ra`, `Rblind`
- `Theta To Wall`
- `Rcollision`
- `Rdynamic`, `Rintent`
- `Rtotal`
- Risk Level
- Passthrough Threshold
- Passthrough Decision

---

## 9. Setup

1. Unity Hub에서 `unity-client/` 폴더를 엽니다.
2. `Assets/Scenes/SampleScene.unity`를 엽니다.
3. Build Settings에서 Android 플랫폼으로 전환합니다.
4. Quest 기기를 연결하고 Build & Run을 실행합니다.
5. 헤드셋에서 Space Setup이 되어 있지 않다면 먼저 진행합니다.
6. 최초 실행 시 Scene 권한 요청을 승인합니다.
7. 실행 후 UI에서 거리, 움직임 Feature, 위험도, Passthrough 판단 결과를 확인합니다.

---

## 10. Known Limitations / TODO

현재 구현의 한계와 앞으로 해야 할 일은 다음과 같습니다.

1. **실험용 계수 튜닝 필요**
   - 실제 Quest 헤드셋 실험을 통해 `safeDistance`, `safeTime`, `maxApproachAccel`, 위험도 가중치, `passthroughOnThreshold`를 조정해야 합니다.
   - 정면/측면/후방 접근, 빠른 이동/느린 이동, 정지 상태에서 벽과 가까운 상황 등을 나누어 검증합니다.

2. **실제 Passthrough 제어 연결 필요**
   - 현재는 `Passthrough Decision`을 UI에만 표시합니다.
   - 이후 실제 Passthrough Layer ON/OFF 또는 시각화 방식 선택 로직과 연결해야 합니다.

3. **사용자 상태 분류 안정화 필요**
   - 현재는 순간값 기반으로 상태를 분류합니다.
   - 이동 지속 시간 조건, 최근 n프레임 평균/최대값 기반 smoothing을 추가해야 합니다.

4. **AI/ML 동적 위험도 연동 필요**
   - `Rdynamic`, `Rintent`는 현재 0으로 고정되어 있습니다.
   - 추후 AI/ML 파트에서 계산된 값을 받아 `Rtotal`에 반영해야 합니다.

5. **로그 및 대시보드 연동 필요**
   - Feature 값, 위험도 값, Passthrough 판단 결과를 세션 로그로 저장할 필요가 있습니다.
   - Backend/Data/Dashboard 파트에서 분석할 수 있도록 출력 형식을 정리해야 합니다.

6. **개인화 모델 연동 필요**
   - 사용자의 Passthrough 작동 이력과 수동 개입 여부를 기반으로 가중치와 threshold를 조정하는 구조가 필요합니다.

7. **다양한 시각화 방식 실험 필요**
   - Directional Passthrough, Augmented Virtuality, Volumetric Cue 등 다양한 시각화 방식과 연결하여 비교 실험을 진행해야 합니다.

---

## 11. Team Integration Points

현재 구현은 독립적인 실험용 스크립트이지만, 최종 시스템에서는 다른 팀원 파트와 다음과 같이 연결됩니다.

| 연결 대상 | 필요한 연결 내용 |
|---|---|
| AI/ML Layer | `Rdynamic`, `Rintent` 값을 받아 `Rtotal`에 반영 |
| Passthrough Visualization | `shouldEnablePassthrough` 값을 실제 Passthrough 제어 또는 시각화 방식 선택에 사용 |
| Backend / Data Layer | Feature, 위험도, Passthrough 판단 결과를 세션 로그로 저장 |
| Dashboard | 시간에 따른 위험도 변화, Passthrough 판단 시점, 실험 결과를 시각화 |
| Personalization Model | 사용자별 로그를 기반으로 가중치와 threshold를 조정 |

