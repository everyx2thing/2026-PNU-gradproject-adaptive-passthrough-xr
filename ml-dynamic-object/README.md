# Dynamic Object Risk MVP

카메라 영상에서 사람을 검출하고 추적하여 상대 위치, 접근 상태, TTC 기반 위험도를 계산하는 Python MVP입니다.

## 담당 범위

```text
USB/Mock Camera
→ YOLOv8n ONNX Person Detection
→ Center-based Tracking
→ Relative Motion and TTC
→ Location and Risk Estimation
→ JSON/NDJSON Output
```

최종 Quest 런타임과 Backend 연동 전, 동적 객체 위험 분석의 기준 구현 및 테스트 도구로 사용합니다.

현재 구현의 모듈, 계산 방식, 한계는 [IMPLEMENTATION.md](IMPLEMENTATION.md)에 정리되어 있습니다.

## 가장 빠른 Windows 실행

처음 한 번:

```text
setup_windows.cmd 더블클릭
```

이후 실행:

```text
run_camera.cmd  USB 카메라 실행
run_mock.cmd    하드웨어 없는 Mock 데모
```

## 설치

저장소 루트에서:

```powershell
cd ml-dynamic-object
python -m pip install -e ".[dev,camera]"
```

모델을 새로 생성해야 하는 경우:

```powershell
python -m pip install -e ".[model-export]"
python scripts/export_person_model.py
```

## 실행

Mock 접근 시나리오:

```powershell
python examples/run_mock_demo.py
```

USB 카메라:

```powershell
python examples/run_usb_camera.py --camera 0 --frames 0 --preview
```

기본 출력 위치:

- `outputs/latest.json`: 최신 프레임 결과
- `outputs/risk.ndjson`: 프레임별 누적 결과

## 테스트

```powershell
python -m pytest
```

모델 바이너리와 실제 실행 결과는 Git에서 제외되며, 빈 디렉터리만 유지합니다.
