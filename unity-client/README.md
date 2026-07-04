# Unity Client - feature 수집 및 거리 계산

Meta Quest 기반 Adaptive Passthrough XR 프로젝트의 Unity 클라이언트 프로토타입입니다.
본 프로젝트는 사용자가 실제 공간 안에서 움직일 때 **HMD와 실제 벽 사이의 거리**, **벽 방향 접근 속도**, **HMD 및 컨트롤러 움직임 feature**를 실시간으로 확인하는 것을 목적으로 합니다.

현재 구현은 최종 Passthrough 제어 이전 단계로, 향후 위험도 계산 및 Adaptive Passthrough 전환 로직에 사용될 입력 feature를 검증하기 위한 프로토타입입니다.

---

## Requirements

* Unity **6000.4.2f1** (Unity 6)
* Meta Quest 계열 헤드셋

  * Quest 2 / Quest 3 / Quest 3S / Quest Pro
* Meta Quest 개발자 모드 활성화
* Android Build Support 모듈
* 헤드셋에서 **Space Setup(Room Setup)** 완료 필요

  * Scene API를 통해 실제 방의 벽 정보를 가져오기 위함

---

## Packages / Dependencies

`Packages/manifest.json` 기준 주요 패키지는 다음과 같습니다.

* `com.meta.xr.sdk.core`

  * OVRManager, OVRCameraRig, Scene API, Boundary API 사용
* `com.meta.xr.sdk.interaction.ovr`

  * Meta XR Interaction SDK
* `com.unity.xr.management`
* `com.unity.xr.openxr`
* `com.unity.render-pipelines.universal`

  * URP 기반 렌더링
* `com.unity.inputsystem`

  * Unity Input System
* `com.coplaydev.unity-mcp`, `com.meta.xr.unity-mcp.extension`

  * Unity 내 AI 에디팅 및 개발 보조용 패키지

---

## Project Structure

```text
unity-client/
├─ Assets/
│  ├─ Scenes/
│  │  └─ SampleScene.unity
│  │
│  ├─ Scripts/
│  │  ├─ QuestBoundaryLogger.cs
│  │  └─ QuestSceneDistanceLogger.cs
│  │
│  ├─ Settings/
│  │  └─ URP 및 렌더링 관련 설정
│  │
│  ├─ Resources/
│  │  └─ OVR/XR 관련 설정 에셋
│  │
│  └─ Plugins/
│     └─ Android/
│        └─ AndroidManifest.xml
│
├─ Packages/
│  ├─ manifest.json
│  └─ packages-lock.json
│
├─ ProjectSettings/
│  └─ Unity 프로젝트 설정, XR 설정, 빌드 설정 등
│
└─ README.md
```



---
## Main Modules



| Module | Script / Object | Description |
|---|---|---|
| Scene Permission Module | `QuestSceneDistanceLogger.cs` | Quest의 Scene API 사용을 위해 `USE_SCENE` 권한을 요청하고, 권한 승인 후 Room Scene 데이터를 불러옵니다. |
| Room Scene Loading Module | `QuestSceneDistanceLogger.cs` | Space Setup을 통해 등록된 Room Anchor와 하위 Scene Anchor를 조회합니다. |
| Wall Filtering Module | `QuestSceneDistanceLogger.cs` | Scene Anchor 중 `WallFace`, `InvisibleWallFace`만 필터링하여 실제 방의 벽 경계로 사용합니다. |
| Wall Distance Module | `QuestSceneDistanceLogger.cs` | HMD 위치와 각 벽 평면 사이의 수직 거리를 계산하고, 가장 가까운 벽까지의 거리를 산출합니다. |
| Motion Feature Module | `QuestSceneDistanceLogger.cs` | HMD 속도, 가속도, 각속도와 좌우 컨트롤러 속도를 계산합니다. |
| Wall Approach Module | `QuestSceneDistanceLogger.cs` | HMD 속도 벡터를 가장 가까운 벽 방향으로 투영하여 벽 방향 접근 속도와 TTC를 계산합니다. |
| Debug UI Module | `DistanceCanvas`, `QuestSceneDistanceLogger.cs` | 계산된 거리, 모션 feature, 접근 속도, TTC를 HMD 전방의 world-space UI에 실시간으로 출력합니다. |
| Boundary Debug Module | `QuestBoundaryLogger.cs` | Guardian Boundary의 설정 여부, 크기, 경계 좌표를 확인하기 위한 보조 디버깅 모듈입니다. 현재 핵심 거리 계산에는 사용하지 않습니다. |

### Module Flow

Quest Space Setup  
→ Scene Permission Request  
→ Room Anchor Loading  
→ WallFace / InvisibleWallFace Filtering  
→ HMD Position Tracking  
→ Wall Distance Calculation  
→ Motion Feature Calculation  
→ Wall Approach Speed & TTC Calculation  
→ DistanceCanvas Debug UI Output

### Feature Output

현재 프로토타입에서 실시간으로 확인 가능한 주요 출력값은 다음과 같습니다.

| Feature | Description |
|---|---|
| Wall Distance | HMD와 가장 가까운 실제 벽 사이의 거리 |
| Head Speed | HMD 위치 변화량 기반 머리 이동 속도 |
| Head Acceleration | HMD 속도 변화량 기반 머리 가속도 |
| Head Angular Speed | HMD 회전 변화량 기반 머리 각속도 |
| Left Hand Speed | 왼쪽 컨트롤러 이동 속도 |
| Right Hand Speed | 오른쪽 컨트롤러 이동 속도 |
| Hand Avg Speed | 좌우 컨트롤러 속도의 평균 |
| Hand/Head Ratio | 손 움직임 속도와 HMD 움직임 속도의 비율 |
| Toward Wall Speed | HMD가 가장 가까운 벽 방향으로 접근하는 속도 |
| TTC | 현재 접근 속도가 유지될 경우 벽에 도달하기까지의 예상 시간 |

---
## Scene Configuration

현재 `SampleScene`은 Quest 실기기 테스트를 위한 단일 테스트 씬입니다.

주요 구성은 다음과 같습니다.

* `OVRCameraRig`

  * HMD 및 컨트롤러 위치 추적
* `QuestSceneDistanceLogger`

  * 현재 활성화된 핵심 로직
  * Scene API 기반 벽 거리 및 움직임 feature 계산
* `QuestBoundaryLogger`

  * Guardian Boundary 디버깅용 스크립트
  * 현재 핵심 실험에서는 보조 기능으로 사용
* `DistanceCanvas`

  * HMD 전방 약 2m 위치에 고정되는 world-space UI
  * 실시간 거리 및 motion feature 출력

---

## Key Scripts

### `QuestSceneDistanceLogger.cs`

현재 프로토타입의 핵심 실행 스크립트입니다.

주요 기능은 다음과 같습니다.

1. **Scene Permission 요청**

   * `com.oculus.permission.USE_SCENE` 권한을 요청합니다.
   * 권한이 승인되면 Quest의 Space Setup으로 등록된 Room 정보를 조회합니다.

2. **실제 벽 Anchor 로드**

   * `OVRAnchor` 및 `OVRSemanticLabels`를 사용하여 Room 내 Scene Anchor를 탐색합니다.
   * 이 중 `WallFace`, `InvisibleWallFace`만 필터링하여 실제 방의 벽 경계로 사용합니다.
   * Floor, Ceiling, Table, Couch 등 기타 Scene Object는 현재 거리 계산 대상에서 제외합니다.

3. **HMD와 벽 사이 거리 계산**

   * 매 프레임 HMD 위치를 기준으로 각 벽 평면까지의 수직 거리를 계산합니다.
   * 가장 가까운 벽과 그 거리를 실시간으로 산출합니다.

4. **Motion Feature 계산**

   * HMD 위치 변화량을 기반으로 HMD 속도를 계산합니다.
   * HMD 속도 변화량을 기반으로 HMD 가속도를 계산합니다.
   * HMD 회전 변화량을 기반으로 머리 각속도를 계산합니다.
   * 좌우 컨트롤러 위치 변화량을 기반으로 컨트롤러 속도를 계산합니다.
   * 양손 평균 속도와 HMD 속도의 비율을 계산하여 손 중심 움직임 여부를 확인할 수 있도록 합니다.

5. **벽 접근 정보 계산**

   * HMD 속도 벡터를 가장 가까운 벽 방향으로 투영하여 벽 방향 접근 속도를 계산합니다.
   * 접근 속도와 벽까지의 거리를 이용하여 Time-To-Collision(TTC)을 계산합니다.
   * 벽에서 멀어지거나 벽과 평행하게 움직이는 경우 접근 속도는 0에 가깝게 처리됩니다.

6. **실시간 UI 출력**

   * 계산된 값들은 `DistanceCanvas`의 텍스트 UI에 출력됩니다.
   * 캔버스는 사용자의 카메라 전방 약 2m 위치에 고정됩니다.

---

### `QuestBoundaryLogger.cs`

Guardian Boundary 디버깅을 위한 보조 스크립트입니다.

주요 기능은 다음과 같습니다.

* `OVRManager.boundary.GetConfigured()`를 통해 Guardian 설정 여부 확인
* `GetDimensions()`를 통해 Play Area 크기 확인
* `GetGeometry()`를 통해 Guardian 경계 좌표 확인
* 콘솔 및 화면 UI에 Boundary 정보 출력

현재 프로젝트의 핵심 거리 계산은 Guardian Boundary가 아니라 **Scene API 기반 실제 벽 정보**를 사용합니다.
따라서 `QuestBoundaryLogger.cs`는 비교 및 디버깅 목적의 보조 기능으로 유지합니다.

---

## Runtime Output

앱 실행 후 DistanceCanvas에서 다음 정보를 실시간으로 확인할 수 있습니다.

```text
[Scene Distance]
Head Position
Closest Wall
Wall Distance

[Motion Features]
Head Speed
Head Acceleration
Head Angular Speed
Left Hand Speed
Right Hand Speed
Hand Avg Speed
Hand/Head Ratio

[Wall Approach]
Toward Wall Speed
TTC
Approaching Wall
```

---

## Setup & Build

1. Unity Hub에서 프로젝트를 엽니다.
2. `Assets/Scenes/SampleScene.unity`를 엽니다.
3. Build Settings에서 Platform을 Android로 전환합니다.
4. XR Plug-in Management에서 OpenXR 및 Meta Quest 관련 설정을 확인합니다.
5. Quest 기기에서 Space Setup을 먼저 완료합니다.
6. Quest를 USB로 연결한 뒤 Build & Run을 실행합니다.
7. 앱 실행 시 Scene 권한 요청이 나타나면 허용합니다.
8. DistanceCanvas에 벽 거리 및 Motion Feature 값이 표시되는지 확인합니다.

---

## Test Checklist

실기기에서 다음 항목을 확인합니다.

* Space Setup이 완료되지 않은 경우 Room 정보를 불러오지 못하고 안내 메시지가 표시되는지 확인
* 벽 쪽으로 이동할 때 Wall Distance 값이 감소하는지 확인
* 벽에서 멀어질 때 Wall Distance 값이 증가하는지 확인
* 벽 쪽으로 이동할 때 Toward Wall Speed가 양수로 증가하는지 확인
* 벽과 평행하게 이동할 때 Toward Wall Speed가 0에 가깝게 유지되는지 확인
* 고개를 회전할 때 Head Angular Speed가 증가하는지 확인
* 손을 움직일 때 Left/Right Hand Speed가 증가하는지 확인

---

## Current Status

현재 구현된 기능은 다음과 같습니다.

* Scene API 기반 실제 벽 정보 로드
* HMD와 가장 가까운 벽 사이의 거리 계산
* HMD 속도, 가속도, 각속도 계산
* 좌우 컨트롤러 속도 계산
* 손/머리 움직임 비율 계산
* 벽 방향 접근 속도 계산
* TTC 계산
* World-space UI를 통한 실시간 디버그 출력

---


## Next Steps

다음 단계에서는 현재 수집된 feature를 기반으로 위험 판단 로직을 추가할 예정입니다.

1. 거리와 TTC 기반 위험 단계 분류
2. HMD 움직임 상태 분류

   * Static
   * Dynamic
   * Agitated
3. 벽 접근 상황에서의 Warning / Danger UI 표시
4. 위험 단계에 따른 Passthrough 또는 시각적 cue 제어
5. CSV 또는 JSON 기반 실험 로그 저장
6. 사용자 테스트를 위한 실험 시나리오 구성

