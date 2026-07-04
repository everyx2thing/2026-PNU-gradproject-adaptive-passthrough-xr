# Dynamic Object Risk MVP 구현 설명

이 문서는 현재 실제로 구현되어 동작하는 기능을 설명합니다. 제안서에 포함된 5단계 상황 상태 분류, TCN-GRU, Quest Passthrough Camera API, Unity 및 Backend 연동은 아직 구현 범위에 포함되지 않습니다.

## 1. 현재 구현 범위

```text
Mock 또는 USB 카메라
→ YOLOv8n ONNX 사람 검출
→ 중심점 기반 객체 추적
→ 상대 위치 계산
→ 최근 bbox 이력 기반 접근 상태 및 TTC 추정
→ 규칙 기반 동적 위험도 계산
→ JSON/NDJSON 출력
```

현재 `risk.score`는 전체 Adaptive Passthrough 위험도 `R_total`이 아닙니다. 카메라로 관측한 사람의 위치와 움직임을 이용한 규칙 기반 동적 객체 위험도 `R_dynamic_rule`의 초기 구현에 해당합니다.

## 2. 구현된 모듈

| 모듈 | 구현체 | 역할 |
|---|---|---|
| CameraSource | `MockCameraSource` | 이미지 없이 미리 정의한 Detection 프레임 생성 |
| CameraSource | `OpenCVCameraSource` | OpenCV로 USB 카메라의 BGR 프레임 수집 |
| Detector | `FakeDetector` | Mock 프레임의 Detection 반환 |
| Detector | `OnnxPersonDetector` | YOLOv8n ONNX로 사람 전체 bbox 검출 |
| Detector | `OpenCVFaceDetector` | 얼굴 검출용 보조·테스트 구현 |
| Tracker | `SimpleTracker` | 같은 label의 bbox 중심점 거리를 이용해 track ID 유지 |
| MotionEstimator | `HistoryMotionEstimator` | 최근 이력에서 접근·정지·후퇴 및 TTC 추정 |
| LocationEstimator | `RelativeLocationEstimator` | 화면 구역, 사용자 방향, 상대 거리 구간 계산 |
| RiskEstimator | `WeightedRiskEstimator` | 거리·접근·TTC·중앙 경로를 조합해 위험도 계산 |
| RiskOutputPublisher | `JsonRiskOutputPublisher` | 마지막 결과를 터미널에 JSON으로 출력 |
| RiskOutputPublisher | `LatestJsonFilePublisher` | 최신 결과를 `latest.json`으로 교체 |
| RiskOutputPublisher | `NdjsonRiskOutputPublisher` | 프레임별 결과를 NDJSON에 누적 |

## 3. 카메라 입력

### Mock 입력

`examples/run_mock_demo.py`에는 다섯 프레임의 객체 Detection이 Python 코드로 정의되어 있습니다. 사람 bbox가 프레임마다 커지기 때문에 최종 프레임에서 접근 상태와 높은 위험도를 확인할 수 있습니다.

### USB 카메라 입력

`OpenCVCameraSource`는 기본적으로 다음 설정으로 카메라를 엽니다.

- 카메라 인덱스: `0`
- 해상도 요청값: `1280 × 720`
- FPS 요청값: `30`
- 실제 프레임 형식: OpenCV BGR 배열

카메라 장치를 열지 못하거나 연속 다섯 번 프레임을 읽지 못하면 명확한 오류를 발생시키고, 종료 시에는 `VideoCapture.release()`를 호출합니다.

## 4. 사람 검출

`OnnxPersonDetector`는 `models/yolov8n.onnx`를 ONNX Runtime CPU Provider로 실행합니다.

처리 과정:

1. 카메라 영상을 비율을 유지하며 `640 × 640`으로 letterbox 전처리
2. RGB 변환 및 `0~1` 정규화
3. YOLOv8n ONNX 추론
4. COCO class 0인 `person` 점수만 사용
5. 기본 confidence `0.4` 미만 제거
6. 기본 IoU `0.45`로 NMS 수행
7. 원본 영상 좌표로 복원 후 bbox를 `0~1` 범위로 정규화

현재 실제 카메라 경로에서는 사람만 출력합니다. 반려동물이나 차량은 모델이 인식하더라도 후처리에서 제외됩니다.

## 5. 객체 추적

`SimpleTracker`는 같은 label을 가진 Detection 중 bbox 중심점 거리가 가장 가까운 객체를 연결합니다.

- 기본 최대 중심점 거리: `0.2`
- 기본 최대 누락 프레임: `2`
- 새로운 객체는 1부터 증가하는 `trackId` 할당
- 직전 bbox와 현재 bbox의 면적 증가율 계산

한계:

- 사람이 서로 교차하면 ID가 바뀔 수 있음
- 가림이 길어지면 새로운 ID가 생성됨
- 속도 예측 및 appearance feature가 없음

따라서 최종 구현에서는 SORT 또는 ByteTrack으로 교체할 예정입니다.

## 6. 상대 위치

### 화면 위치

가로:

- `cx < 0.4`: left
- `0.4 ≤ cx ≤ 0.6`: center
- `cx > 0.6`: right

세로는 화면을 3등분하여 top, center, bottom으로 분류합니다.

예:

```text
center-left
center-center
bottom-right
```

### 사용자 기준 위치

현재 단일 전방 카메라만 사용하므로:

```text
front-left
front-center
front-right
```

중 하나로 출력됩니다.

### 상대 거리

실제 깊이 값이 없으므로 bbox 면적으로 분류합니다.

- bbox 면적 `< 0.03`: `far`
- bbox 면적 `< 0.06`: `mid`
- bbox 면적 `≥ 0.06`: `near`

이는 실제 미터 단위 거리가 아닙니다.

### 대략적인 bearing

```text
bearingDegApprox = (cx - 0.5) × 150
```

음수는 왼쪽, 양수는 오른쪽입니다.

## 7. 접근 상태와 TTC

`HistoryMotionEstimator`는 track ID별 최근 1.5초의 bbox 이력을 유지합니다.

접근 판정을 시작하기 위한 최소 조건:

- 관측 수 4개 이상
- 관측 시간 250ms 이상

bbox 크기는 다음 값으로 계산합니다.

```text
scale = sqrt(bbox width × bbox height)
```

이 방식은 사람이 화면 높이를 거의 채워 bbox 높이가 더 이상 증가하지 않을 때도 가로 폭 변화를 반영합니다.

상태 분류:

- scale rate `≥ 0.04`: `approaching`
- scale rate `≤ -0.04`: `receding`
- 그 사이: `steady`
- 이력 부족: `unknown`

TTC는 접근 중일 때만 다음처럼 근사합니다.

```text
ttcSecApprox = 1 / scaleRatePerSec
```

단안 카메라 bbox 기반 값이므로 실제 물리적 충돌 시간과 동일하지 않습니다.

## 8. 위험도 계산

현재 위험도 구성:

| 요소 | 기본 비중 |
|---|---:|
| 상대 거리 | 30% |
| 접근 속도 | 30% |
| TTC | 20% |
| 화면 중앙 충돌 경로 | 15% |
| 객체 종류 | 5% |

거리와 접근이 동시에 큰 경우 추가 상호작용 가중치가 적용됩니다. YOLO confidence가 낮으면 최종 점수가 감소하고, 객체가 멀어지는 중이면 최종 점수에 `0.55`를 곱합니다.

위험 단계:

- `score < 0.25`: safe
- `score < 0.50`: caution
- `score < 0.75`: warning
- `score ≥ 0.75`: danger

현재 점수는 `0.0~1.0` 범위입니다. UI에서 100을 곱하면 백분율처럼 표시할 수 있습니다.

## 9. JSON 출력

### 최신 결과

```text
outputs/latest.json
```

매 프레임 새 결과로 교체됩니다.

### 프레임별 기록

```text
outputs/risk.ndjson
```

JSON 객체 하나가 한 줄에 기록됩니다. 전체 파일은 JSON 배열이 아니므로 줄별로 읽어야 합니다.

### 터미널

카메라 실행을 종료하면 마지막 프레임 결과 하나를 터미널에 출력합니다.

## 10. Windows 실행 파일

### 초기 설치

`setup_windows.cmd`를 더블클릭하면 다음 작업을 수행합니다.

1. `.venv` 가상환경 생성
2. 테스트·카메라 런타임 의존성 설치
3. 모델이 없으면 YOLOv8n 다운로드 및 ONNX 변환
4. 전체 pytest 실행

모델 변환 과정에서는 PyTorch 및 Ultralytics가 설치되므로 시간이 걸릴 수 있습니다.

### USB 카메라

`run_camera.cmd`를 더블클릭합니다.

- 기본 카메라 인덱스 0
- 무제한 프레임
- Preview 창 표시
- `Q` 또는 `Esc`로 종료

명령행 옵션을 직접 전달할 수도 있습니다.

```powershell
run_camera.cmd --camera 1 --frames 300 --preview
```

### Mock 데모

`run_mock.cmd`를 더블클릭합니다.

## 11. 테스트된 항목

- bbox가 커지면 위험도가 증가하는지
- 느린 접근을 approaching으로 판단하는지
- 순간적인 bbox 변화 노이즈를 무시하는지
- 후퇴 객체의 위험도를 낮추는지
- 화면 오른쪽 객체를 front-right로 분류하는지
- Tracker가 ID를 유지하는지
- 가장 위험한 객체가 summary에 반영되는지
- Mock 입력부터 JSON 출력까지 동작하는지
- 최신 JSON 및 NDJSON 파일 기록이 동작하는지
- 카메라 장치가 정상 해제되는지

## 12. 아직 구현되지 않은 기능

- Quest Passthrough Camera API
- HMD 및 Controller 센서 입력
- Guardian 경계 거리
- 실제 거리 및 실제 m/s 접근 속도
- SORT/ByteTrack
- 사람 이외 동적 객체
- 5단계 상황 상태 분류
- pose 및 interaction-seeking 분석
- TCN-GRU
- `R_collision`, `R_static`, `R_intent`, `R_total`
- Unity Passthrough 시각화
- FastAPI/PostgreSQL/Dashboard
- 사용자 로그 기반 개인화

