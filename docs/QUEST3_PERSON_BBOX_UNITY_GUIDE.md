# Quest 3 카메라 기반 사람 Bounding Box Unity 앱 제작 가이드

문서 버전: 1.0  
작성 기준일: 2026-07-27  
대상 프로젝트: `unity-client/`  
대상 기기: Meta Quest 3

## 1. 문서 목적

이 문서는 현재 USB 카메라와 Python으로 구현된 사람 인식 경로를 Unity 앱 내부의 Quest 3 Passthrough Camera 입력으로 옮기기 위한 제작 절차를 정의합니다.

이번 단계의 최종 결과는 다음과 같습니다.

> Quest 3에 설치한 APK가 PC나 USB 카메라 없이 사람을 검출하고, 카메라 디버그 패널에 사람 bounding box와 confidence를 표시한다.

이번 단계에서는 사람의 3D 위치, 객체 추적, 접근 속도, TTC, `R_dynamic`, 최종 위험도, 개인화, 실제 위험도 기반 Passthrough 제어를 구현하지 않습니다. 해당 기능은 bbox 검출이 Quest 3에서 검증된 뒤 연결합니다.

## 2. 현재 프로젝트 상태

### 2.1 현재 구현

현재 사람 인식 MVP는 다음 경로로 동작합니다.

```text
USB 또는 Mock Camera
→ Python/OpenCV
→ YOLOv8n ONNX Runtime
→ Person Detection
→ Center-based Tracking
→ 접근 상태·TTC·동적 위험도
```

관련 구현:

- `ml-dynamic-object/src/graduate_risk_mvp/camera.py`
- `ml-dynamic-object/src/graduate_risk_mvp/detector.py`
- `ml-dynamic-object/src/graduate_risk_mvp/models.py`
- `ml-dynamic-object/src/graduate_risk_mvp/tracker.py`
- `ml-dynamic-object/src/graduate_risk_mvp/motion.py`
- `ml-dynamic-object/src/graduate_risk_mvp/risk.py`

Unity 쪽 핵심 구현은 `unity-client/Assets/Scripts/QuestRiskExperimentLogger.cs`입니다. 현재 이 스크립트의 `Rdynamic`과 `Rintent`는 `0`으로 고정되어 있습니다.

### 2.2 현재 Unity 설정에서 부족한 항목

현재 저장소를 기준으로 다음 항목이 아직 준비되지 않았습니다.

- `com.meta.xr.mrutilitykit` 패키지 미설치
- `com.unity.ai.inference` 패키지 미설치
- Android Manifest의 `horizonos.permission.HEADSET_CAMERA` 권한 없음
- `SampleScene`의 Insight Passthrough 비활성화
- `SampleScene`의 Passthrough Camera Access 권한 요청 비활성화
- `PassthroughCameraAccess` 컴포넌트 없음
- Unity용 사람 검출 모델 및 후처리 코드 없음
- bbox 디버그 UI 없음

따라서 기존 `QuestRiskExperimentLogger`의 일부 값만 수정해서는 사람 bbox가 나오지 않습니다. 카메라 입력, 모델 추론, bbox 후처리, UI 표시를 별도 컴포넌트로 추가해야 합니다.

## 3. 제작 범위

### 3.1 포함 범위

- Quest 3 카메라 권한 요청
- MRUK `PassthroughCameraAccess`를 통한 카메라 Texture 획득
- Unity Inference Engine을 통한 온디바이스 사람 검출
- confidence 필터링
- NMS 또는 NMS가 포함된 모델 출력 처리
- bbox 좌표 정규화
- 디버그 카메라 패널에 bbox와 confidence 표시
- 앱 pause/resume 시 카메라 및 추론 복구
- Quest 3 APK 빌드 및 실기기 검증

### 3.2 제외 범위

- 사람별 `trackId` 유지
- 실제 미터 단위 거리
- MRUK Depth Raycast
- 3D world-space bbox
- 접근/후퇴 상태
- TTC
- `R_dynamic`, `R_intent`, `R_total`
- 위험도 기반 실제 Passthrough 자동 제어
- Backend 및 Dashboard 연동
- 사용자 개인화 모델

## 4. 목표 데이터 흐름

```text
Quest 3 RGB Camera
→ PassthroughCameraAccess.GetTexture()
→ 입력 Texture 전처리
→ Unity Inference Engine
→ 모델별 출력 해석
→ Person class 필터
→ confidence threshold
→ NMS
→ PersonDetection 목록
→ 카메라 디버그 패널 bbox 표시
```

렌더링과 인식은 서로 다른 주기로 동작시킵니다.

```text
XR 렌더링: 72Hz 이상
카메라 스트림: 초기 30fps
사람 인식: 초기 10fps
```

매 Unity 프레임마다 추론하지 않습니다. 이전 추론이 끝나지 않았다면 새로운 추론을 시작하지 않습니다.

## 5. 개발 환경 요구사항

### 5.1 하드웨어

- Meta Quest 3
- 개발자 모드 활성화
- USB 디버깅 승인
- Passthrough 사용 가능 상태

### 5.2 소프트웨어

현재 프로젝트 기준:

- Unity `6000.4.2f1`
- Meta XR Core SDK `203.0.0`
- Meta XR Interaction SDK `203.0.0`
- Android Build Support
- OpenJDK, Android SDK, Android NDK

추가 패키지:

- Meta Mixed Reality Utility Kit(MRUK)
- Unity Inference Engine `com.unity.ai.inference`

Meta 공식 MultiObjectDetection 샘플은 Inference Engine `2.2.1`을 기준으로 안내합니다. 이 프로젝트에서는 먼저 공식 샘플과 동일한 버전 조합으로 bbox PoC를 통과시키는 것을 권장합니다. 패키지 해석 과정에서 더 높은 버전이 선택된다면 API 컴파일과 Quest 3 실행을 다시 검증한 뒤 버전을 고정합니다.

## 6. 권장 구현 전략

### 6.1 1차 모델: Meta 공식 샘플 모델 사용

첫 번째 Quest 3 검증에서는 기존 YOLOv8n 모델을 바로 연결하기보다 Meta 공식 `Unity-PassthroughCameraApiSamples`의 MultiObjectDetection 모델과 추론 구조를 기준으로 구현합니다.

이유:

- Quest 3 Passthrough Camera 입력과 연결된 사례가 이미 검증됨
- Unity Inference Engine용 모델 출력 구조가 정리되어 있음
- `person` 클래스를 포함함
- bbox, class ID, score 출력 예제가 있음
- 카메라 문제와 커스텀 모델 문제를 분리해서 확인할 수 있음

첫 PoC가 성공하면 기존 `ml-dynamic-object/models/yolov8n.onnx`를 Unity 모델로 가져오고 별도의 YOLOv8 후처리 어댑터를 구현합니다.

### 6.2 bbox 표시 방식

첫 검증에서는 실제 Passthrough 장면 위에 bbox를 직접 겹치지 않습니다. 머리 앞에 카메라 영상을 보여주는 디버그 패널을 만들고 그 패널 내부에 bbox를 표시합니다.

```text
CenterEyeAnchor
└─ PersonDetectionDebugCanvas
   ├─ CameraRawImage
   ├─ BBoxLayer
   │  ├─ BBoxItem
   │  └─ ...
   └─ StatusText
```

이 방식은 다음 문제를 첫 단계에서 제외할 수 있습니다.

- 왼쪽 RGB 카메라와 사용자의 양안 시점 차이
- 카메라 intrinsics/extrinsics
- 촬영 시점과 현재 HMD pose 차이
- Depth Raycast 실패
- spatial anchor 드리프트

## 7. 권장 폴더 구조

다음 구조를 새로 추가합니다.

```text
unity-client/Assets/
├─ Models/
│  └─ PersonDetection/
│     ├─ person_detection.sentis
│     └─ labels.txt
├─ Prefabs/
│  └─ PersonDetection/
│     ├─ PersonDetectionRuntime.prefab
│     ├─ PersonDetectionDebugCanvas.prefab
│     └─ BBoxItem.prefab
├─ Scenes/
│  ├─ SampleScene.unity
│  └─ PersonDetectionTest.unity
└─ Scripts/
   └─ Perception/
      ├─ PersonDetection.cs
      ├─ QuestCameraFrameProvider.cs
      ├─ PersonInferenceEngine.cs
      ├─ PersonDetectionCoordinator.cs
      ├─ PersonBBoxDebugView.cs
      └─ QuestPermissionCoordinator.cs
```

`SampleScene`을 처음부터 직접 수정하기보다 `PersonDetectionTest.unity`에서 카메라와 bbox만 검증한 뒤 안정화된 prefab을 `SampleScene`으로 옮기는 방식을 권장합니다.

## 8. 데이터 계약

Unity 내부 bbox 데이터 형식을 먼저 고정합니다.

```csharp
using System;
using UnityEngine;

[Serializable]
public struct PersonDetection
{
    public int ClassId;
    public float Confidence;
    public Rect BboxImageNormalized;
    public DateTime CaptureTimestamp;
    public Pose CameraPose;
}
```

`BboxImageNormalized` 규칙:

- 값 범위: `0~1`
- 원점: 이미지 좌측 상단
- X축: 오른쪽이 양수
- Y축: 아래쪽이 양수
- `Rect.x`, `Rect.y`: bbox 좌측 상단
- `Rect.width`, `Rect.height`: bbox 크기

이 규칙은 기존 Python의 이미지 기반 좌표와 연결하기 쉽습니다. Unity UI 좌표로 표시할 때만 Y축을 반전합니다.

```text
uiY = 1 - imageY
```

모델 출력이 `(cx, cy, w, h)`이면 내부 계약에 넣기 전에 `(x, y, width, height)`로 변환합니다.

```text
x = cx - w / 2
y = cy - h / 2
```

## 9. 구현 절차

### 9.1 Step 0: 기준 프로젝트 확인

1. Unity Hub에서 저장소의 `unity-client/`를 엽니다.
2. Unity 버전이 `6000.4.2f1`인지 확인합니다.
3. `Assets/Scenes/SampleScene.unity`가 기존 상태에서 컴파일되는지 확인합니다.
4. Android Build Profile을 활성화합니다.
5. Meta > Tools > Project Setup Tool을 실행합니다.
6. 기존 Scene API 거리·위험도 기능이 Quest 3에서 동작하는지 먼저 확인합니다.

완료 기준:

- Unity Console compile error 0개
- 기존 Quest 빌드 성공
- 기존 `RiskExperimentLogger` UI 정상 표시

### 9.2 Step 1: 패키지 설치

Unity Package Manager에서 다음 패키지를 설치합니다.

1. MR Utility Kit
2. Unity Inference Engine

버전 결정 원칙:

- MRUK는 현재 Meta XR SDK `203.0.0`과 호환되는 버전을 사용합니다.
- 처음에는 Meta 공식 PCA 샘플이 사용하는 Inference Engine 버전을 기준으로 맞춥니다.
- 패키지 설치 후 `Packages/manifest.json`과 `packages-lock.json`을 함께 커밋합니다.
- 버전을 임의로 올리기 전에 Quest 3 bbox 테스트를 다시 수행합니다.

완료 기준:

- `PassthroughCameraAccess` 타입을 Unity에서 찾을 수 있음
- `Unity.InferenceEngine` namespace가 컴파일됨
- Console compile error 0개

### 9.3 Step 2: Android 권한 추가

`unity-client/Assets/Plugins/Android/AndroidManifest.xml`에 다음 권한을 추가합니다.

```xml
<uses-permission android:name="horizonos.permission.HEADSET_CAMERA" />
```

기존 권한은 유지합니다.

```xml
<uses-permission android:name="com.oculus.permission.USE_SCENE" />
```

주의:

- Meta의 Android Manifest 업데이트 도구를 실행하면 `HEADSET_CAMERA` 권한이 제거될 수 있으므로 매번 다시 확인합니다.
- 카메라 권한 요청은 한 컴포넌트에서만 수행합니다.
- `OVRManager` 자동 요청과 사용자 스크립트 요청을 동시에 사용하지 않습니다.

### 9.4 Step 3: Quest 권한 관리

`QuestPermissionCoordinator`가 다음 순서로 권한을 관리합니다.

```text
앱 시작
→ HEADSET_CAMERA 권한 확인
→ 권한이 없으면 요청
→ 승인 시 PassthroughCameraAccess 활성화
→ 거부 시 상태 UI와 재시도 안내 표시
```

권한 문자열:

```text
horizonos.permission.HEADSET_CAMERA
```

권한 거부 시 앱이 추론을 계속 시도하지 않도록 해야 합니다.

### 9.5 Step 4: 테스트 Scene 구성

`PersonDetectionTest.unity`에 다음 오브젝트를 배치합니다.

```text
[BuildingBlock] Camera Rig
[BuildingBlock] Passthrough
PersonDetectionRuntime
EventSystem
Directional Light
```

`PersonDetectionRuntime` 구성:

- `PassthroughCameraAccess`
- `QuestPermissionCoordinator`
- `QuestCameraFrameProvider`
- `PersonInferenceEngine`
- `PersonDetectionCoordinator`

Camera Rig의 OVRManager 설정:

- Insight Passthrough: Enabled
- Passthrough Camera Access: Enabled
- Tracking Origin: Floor Level 또는 기존 프로젝트 설정 유지

`PassthroughCameraAccess` 초기 권장값:

- CameraPosition: Left
- RequestedResolution: `640 × 480`
- MaxFramerate: `30`

해상도를 자동 최고값으로 선택하지 않습니다. Horizon OS 버전에 따라 `1280 × 960`과 `1280 × 1280`처럼 서로 다른 화면비의 해상도가 선택될 수 있기 때문입니다.

### 9.6 Step 5: 카메라 Frame Provider

`QuestCameraFrameProvider`의 책임:

- `PassthroughCameraAccess.IsPlaying` 확인
- 현재 Texture 제공
- 촬영 시점 timestamp 제공
- 촬영 시점 camera pose 제공
- 앱 pause/resume 상태 관리

인터페이스 예시:

```csharp
public interface IQuestCameraFrameProvider
{
    bool TryGetFrame(
        out Texture texture,
        out DateTime timestamp,
        out Pose cameraPose);
}
```

Frame 획득 조건:

- 카메라 권한 승인
- `PassthroughCameraAccess` enabled
- `IsPlaying == true`
- Texture가 null이 아님
- Texture 크기가 유효함

카메라 pose는 추론이 끝난 뒤 다시 가져오지 않습니다. Texture를 가져오는 시점에 함께 저장합니다. 추론 중 사용자가 머리를 움직일 수 있기 때문입니다.

### 9.7 Step 6: 모델 준비

#### 권장 경로 A: 공식 샘플 모델

Meta 공식 MultiObjectDetection 샘플의 Inference Engine 모델과 출력 계약을 기준으로 합니다.

예상 출력:

- bbox tensor
- class ID tensor
- score tensor

후처리:

1. score threshold 적용
2. NMS 적용
3. `person` class만 남김
4. bbox를 `0~1`로 정규화
5. `PersonDetection`으로 변환

초기 권장값:

```text
confidence threshold: 0.40
IoU threshold: 0.45
최대 bbox 수: 10
```

#### 경로 B: 기존 YOLOv8n ONNX

기존 Python MVP의 모델을 사용할 경우 다음 항목을 별도로 검증해야 합니다.

- 모델 입력 shape
- RGB/BGR 순서
- 입력 정규화 범위
- NCHW/NHWC 순서
- 출력 tensor 이름과 shape
- class score 계산 방식
- bbox 좌표 형식
- letterbox padding 복원
- NMS 포함 여부

기존 Python detector가 letterbox 전처리를 사용하므로 Unity에서 입력 Texture를 단순 stretch하면 Python과 bbox 결과가 달라질 수 있습니다. Python과 동일한 letterbox 변환 및 역변환을 구현하거나, 전처리를 모델에 포함한 Unity 전용 모델을 생성해야 합니다.

### 9.8 Step 7: Unity Inference Engine 실행

`PersonInferenceEngine`의 책임:

- 모델 로드
- Worker 생성
- Texture를 Tensor로 변환
- 추론 실행
- 비동기 output readback
- bbox 후처리
- `PersonDetection` 이벤트 발행
- Tensor와 Worker 자원 해제

실행 상태:

```text
Idle
→ Capturing
→ Inferencing
→ ReadingOutput
→ Publishing
→ Idle
```

한 번에 하나의 추론만 실행합니다.

개념 코드:

```csharp
if (!_isInferenceRunning && Time.time >= _nextInferenceTime)
{
    _nextInferenceTime = Time.time + 0.1f;
    StartCoroutine(RunInference());
}
```

초기에는 CPU backend를 사용해 공식 샘플과 같은 기준선을 만든 뒤 GPU backend를 비교합니다. backend 변경 전후에 검출 결과, 프레임 유지율, 발열을 모두 측정합니다.

첫 추론은 모델 초기화 때문에 오래 걸릴 수 있습니다. 시작 화면에서 작은 빈 입력으로 모델 warm-up을 수행하거나 첫 추론 중 상태 UI를 표시합니다.

### 9.9 Step 8: 사람 class 필터와 NMS

후처리 순서:

```text
Raw model detections
→ confidence threshold
→ class == person
→ 좌표 범위 clamp
→ NMS
→ 최대 검출 수 제한
```

NMS는 confidence가 높은 bbox부터 선택하고, 이미 선택된 bbox와 IoU가 임계값보다 높으면 제거합니다.

IoU:

```text
IoU = intersection area / union area
```

테스트 항목:

- 겹치지 않는 두 사람 bbox는 모두 유지
- 같은 사람에 대한 중복 bbox는 하나만 유지
- confidence 미만 bbox 제거
- 이미지 밖 좌표 clamp
- width 또는 height가 0 이하인 bbox 제거

### 9.10 Step 9: bbox Debug UI

첫 PoC에서는 `CameraRawImage` 위에 bbox를 표시합니다.

권장 설정:

- World Space Canvas
- CenterEyeAnchor의 자식
- 사용자 전방 약 `1.5m`
- RawImage 화면비 `4:3`
- bbox layer와 RawImage의 RectTransform 크기 동일
- bbox item object pooling 사용

좌표 변환:

```text
image x1 = bbox.x
image y1 = bbox.y
image x2 = bbox.x + bbox.width
image y2 = bbox.y + bbox.height

ui x1 = image x1
ui x2 = image x2
ui y1 = 1 - image y2
ui y2 = 1 - image y1
```

표시 문자열:

```text
person 0.87
```

매 프레임 `Instantiate`와 `Destroy`를 반복하지 않습니다. 최대 10개의 bbox item을 미리 만들고 활성화 여부만 바꿉니다.

새로운 추론 결과가 없을 때 이전 bbox가 계속 남지 않도록 timeout을 둡니다.

```text
bbox stale timeout: 0.3~0.5초
```

### 9.11 Step 10: 빌드 설정

권장 Android 설정:

- Target Architecture: ARM64
- Scripting Backend: IL2CPP
- Graphics API: 현재 Meta Project Setup Tool 권장값
- Development Build: 최초 테스트 시 Enabled
- Script Debugging: 문제 추적 시에만 Enabled

Build Profile에 `PersonDetectionTest.unity`를 추가하고, 첫 테스트에서는 이 Scene만 포함해 문제 범위를 줄입니다.

## 10. 구현 순서와 중간 완료 기준

### Milestone 1: 카메라 Texture

구현:

- 패키지
- 권한
- PassthroughCameraAccess
- RawImage

완료 기준:

- Quest 3에서 권한 팝업 표시
- 승인 후 디버그 패널에 카메라 영상 표시
- 앱 pause/resume 후 영상 복구

### Milestone 2: 모델 추론

구현:

- 모델 로드
- Texture to Tensor
- 추론
- output shape 로그

완료 기준:

- Quest 3에서 모델 추론 완료
- 앱 crash 없음
- 추론 완료 횟수 확인 가능

### Milestone 3: 사람 bbox

구현:

- person 필터
- confidence
- NMS
- bbox UI

완료 기준:

- 사람을 바라보면 bbox 표시
- 사람이 사라지면 bbox 제거
- bbox와 카메라 패널 속 사람이 대체로 일치
- 여러 사람이 있으면 여러 bbox 표시

### Milestone 4: 안정화

구현:

- 추론 throttle
- 자원 해제
- pause/resume
- 오류 상태 UI

완료 기준:

- 30분 연속 실행
- 메모리의 지속적 증가 없음
- 심각한 발열 또는 앱 종료 없음
- XR 렌더링 72Hz 목표 유지

## 11. 테스트 계획

### 11.1 Editor에서 가능한 테스트

PCA 카메라 자체는 Editor/XR Simulator만으로 완전히 검증하지 않습니다. 대신 다음 순수 로직을 EditMode 테스트로 검증합니다.

- bbox center 형식에서 corner 형식으로 변환
- bbox 정규화
- Y축 반전
- IoU
- NMS
- confidence 필터
- person class 필터
- 화면비에 따른 UI Rect 계산

테스트 이미지 또는 저장된 Texture를 입력으로 사용하는 fallback provider를 만들면 모델 후처리와 UI는 Editor에서도 검증할 수 있습니다.

### 11.2 Quest 3 실기기 테스트

#### 권한

- 최초 설치 후 카메라 권한 팝업
- 승인 시 정상 시작
- 거부 시 오류 메시지
- 설정에서 권한 승인 후 앱 재실행

#### 카메라

- 카메라 Texture가 검게 유지되지 않음
- 좌우 반전 확인
- 화면 상하 반전 확인
- pause/resume 복구
- HMD sleep 후 복구

#### 사람 검출

- 한 사람 정면
- 한 사람 좌측/우측
- 가까운 사람
- 먼 사람
- 두 사람
- 사람이 없는 장면
- 일부만 보이는 사람

#### 안정성

- 5분 빠른 반복 테스트
- 30분 연속 테스트
- 머리를 빠르게 회전
- 앱 focus 전환
- Quest 재부팅 후 재실행

## 12. 로그 항목

원본 카메라 프레임은 저장하지 않습니다. 다음 메타데이터만 로그로 남깁니다.

```text
timestamp
cameraIsPlaying
cameraResolution
inferenceBackend
inferenceDurationMs
detectionCount
maxConfidence
modelVersion
permissionState
```

디버그 로그 예:

```text
[PersonDetection] camera=640x480 inference=82ms persons=1 maxConf=0.87
```

## 13. 자주 발생하는 문제

### 13.1 Texture가 null 또는 검은 화면

확인 순서:

1. Quest 3/3S인지 확인
2. Horizon OS 버전 확인
3. `HEADSET_CAMERA` 권한 확인
4. OVRManager의 Passthrough 활성화 확인
5. `PassthroughCameraAccess.IsPlaying` 확인
6. 앱 첫 실행 후 몇 프레임 대기
7. Android Logcat 확인

### 13.2 bbox가 상하 반전됨

모델 출력이 이미지 좌측 상단 원점인데 Unity UI는 좌측 하단 원점을 사용하는 경우입니다. UI 변환 단계에서 Y축을 한 번만 반전합니다.

### 13.3 bbox가 좌우 또는 크기 방향으로 어긋남

확인:

- 모델 입력 화면비
- camera Texture 화면비
- letterbox padding
- RawImage 화면비
- bbox가 pixel 단위인지 normalized 단위인지
- `(H, W)`와 `(W, H)` 순서

### 13.4 사람을 검출하지 못함

확인:

- class ID가 COCO의 `person`인지
- score threshold가 너무 높지 않은지
- RGB/BGR 순서
- 입력 정규화
- 모델 output tensor 순서
- NMS 전 detection count

### 13.5 첫 추론 때 앱이 멈춤

모델 warm-up을 수행하고, 추론을 메인 Update에서 동기적으로 완료할 때까지 기다리지 않습니다. output readback은 coroutine 또는 지원되는 비동기 API를 사용합니다.

### 13.6 시간이 지날수록 메모리가 증가함

확인:

- Tensor dispose
- Worker dispose
- 임시 Texture 재사용
- bbox GameObject pooling
- 비동기 output clone 해제
- 앱 disable/destroy 시 카메라와 추론 자원 정리

## 14. 완료 정의

다음 조건을 모두 만족하면 bbox 검출 Unity 앱 단계가 완료된 것으로 판단합니다.

- [ ] Quest 3 단독 APK로 실행
- [ ] PC Python 프로세스 불필요
- [ ] USB 카메라 불필요
- [ ] `HEADSET_CAMERA` 권한 처리
- [ ] Quest 카메라 Texture 획득
- [ ] Unity Inference Engine 모델 실행
- [ ] 사람 class만 필터링
- [ ] confidence 표시
- [ ] NMS 적용
- [ ] 카메라 디버그 패널에 bbox 표시
- [ ] 사람이 사라지면 stale bbox 제거
- [ ] pause/resume 복구
- [ ] 30분 연속 실행
- [ ] Unity Console compile error 0개
- [ ] Quest Android Logcat의 치명적 오류 없음

## 15. 다음 단계 연결

bbox 단계 완료 후 다음 순서로 확장합니다.

```text
PersonDetection
→ SimpleTracker C# 포팅
→ bbox scale history
→ approaching / steady / receding
→ TTC 근사
→ R_dynamic
→ QuestRiskExperimentLogger의 rDynamic 교체
→ R_total
→ 실제 Passthrough 제어
```

기존 Python 알고리즘을 C#으로 포팅할 때는 Python 테스트의 입력과 예상 결과를 JSON fixture로 만들고 Unity EditMode 테스트에서 동일한 결과가 나오는지 비교합니다.

## 16. 권장 작업 단위

작업을 다음 커밋 단위로 분리합니다.

1. `feat: add Quest passthrough camera permission and test scene`
2. `feat: add Quest camera frame provider`
3. `feat: run person detection with Unity Inference Engine`
4. `feat: add person bbox debug overlay`
5. `test: add bbox conversion and NMS tests`
6. `docs: record Quest 3 bbox device test results`

## 17. 공식 참고자료

- [Meta Passthrough Camera API 개요](https://developers.meta.com/horizon/documentation/unity/unity-pca-overview/)
- [Meta Unity PCA 시작 가이드](https://developers.meta.com/horizon/documentation/unity/unity-pca-documentation/)
- [Meta Unity Inference Engine 온디바이스 ML 가이드](https://developers.meta.com/horizon/documentation/unity/unity-pca-sentis/)
- [Meta WebCamTexture에서 PassthroughCameraAccess로 이전](https://developers.meta.com/horizon/documentation/unity/unity-pca-migration-from-webcamtexture/)
- [Meta 공식 Unity Passthrough Camera API Samples](https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples)

## 18. 프로젝트 내부 참고자료

- [`unity-client/README.md`](../unity-client/README.md)
- [`unity-client/PROTOTYPE_DETAILS.md`](../unity-client/PROTOTYPE_DETAILS.md)
- [`QuestRiskExperimentLogger.cs`](../unity-client/Assets/Scripts/QuestRiskExperimentLogger.cs)
- [`ml-dynamic-object/README.md`](../ml-dynamic-object/README.md)
- [`ml-dynamic-object/IMPLEMENTATION.md`](../ml-dynamic-object/IMPLEMENTATION.md)
- [`ml-dynamic-object/src/graduate_risk_mvp/detector.py`](../ml-dynamic-object/src/graduate_risk_mvp/detector.py)
- [`ml-dynamic-object/src/graduate_risk_mvp/models.py`](../ml-dynamic-object/src/graduate_risk_mvp/models.py)
