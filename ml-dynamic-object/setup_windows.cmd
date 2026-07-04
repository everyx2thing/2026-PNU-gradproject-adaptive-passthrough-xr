@echo off
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"

where python >nul 2>&1
if errorlevel 1 (
    echo Python을 찾을 수 없습니다. Python 3.10 이상을 먼저 설치해 주세요.
    pause
    exit /b 1
)

if not "%~1"=="--yes" (
    echo Python 가상환경과 카메라 의존성을 설치합니다.
    echo 모델이 없으면 PyTorch와 Ultralytics도 설치하므로 시간이 걸릴 수 있습니다.
    choice /M "계속하시겠습니까"
    if errorlevel 2 exit /b 0
)

if not exist ".venv\Scripts\python.exe" (
    echo [1/4] 가상환경 생성
    python -m venv .venv
    if errorlevel 1 goto :failed
)

set "PYTHON_EXE=.venv\Scripts\python.exe"

echo [2/4] 런타임 및 테스트 의존성 설치
"%PYTHON_EXE%" -m pip install -e ".[dev,camera]"
if errorlevel 1 goto :failed

if not exist "models\yolov8n.onnx" (
    echo [3/4] 모델 내보내기 의존성 설치 및 YOLOv8n ONNX 생성
    "%PYTHON_EXE%" -m pip install -e ".[model-export]"
    if errorlevel 1 goto :failed
    "%PYTHON_EXE%" "scripts\export_person_model.py"
    if errorlevel 1 goto :failed
) else (
    echo [3/4] 기존 models\yolov8n.onnx 사용
)

echo [4/4] 테스트 실행
"%PYTHON_EXE%" -m pytest
if errorlevel 1 goto :failed

echo.
echo 설치와 테스트가 완료되었습니다.
echo run_camera.cmd 또는 run_mock.cmd를 실행하세요.
if not "%~1"=="--yes" pause
exit /b 0

:failed
echo.
echo 설치 또는 테스트 중 오류가 발생했습니다.
pause
exit /b 1

