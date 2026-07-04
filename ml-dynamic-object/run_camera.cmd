@echo off
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"

set "PYTHON_EXE=python"
if exist ".venv\Scripts\python.exe" set "PYTHON_EXE=.venv\Scripts\python.exe"

"%PYTHON_EXE%" --version >nul 2>&1
if errorlevel 1 goto :missing_python

"%PYTHON_EXE%" -c "import cv2, onnxruntime" >nul 2>&1
if errorlevel 1 goto :missing_dependencies

if not exist "models\yolov8n.onnx" goto :missing_model

if not "%~1"=="" goto :custom_run

echo USB 카메라 0번을 실행합니다.
echo Preview 창에서 Q 또는 Esc를 누르면 종료됩니다.
echo.
"%PYTHON_EXE%" "examples\run_usb_camera.py" --camera 0 --frames 0 --preview
set "EXIT_CODE=%ERRORLEVEL%"
echo.
if not "%EXIT_CODE%"=="0" echo 실행 중 오류가 발생했습니다. 코드: %EXIT_CODE%
pause
exit /b %EXIT_CODE%

:custom_run
"%PYTHON_EXE%" "examples\run_usb_camera.py" %*
exit /b %ERRORLEVEL%

:missing_python
echo Python을 찾을 수 없습니다. Python 3.10 이상을 설치해 주세요.
pause
exit /b 1

:missing_dependencies
echo 카메라 실행 의존성이 설치되지 않았습니다.
echo setup_windows.cmd를 먼저 실행해 주세요.
pause
exit /b 1

:missing_model
echo models\yolov8n.onnx 파일이 없습니다.
echo setup_windows.cmd를 실행하거나 scripts\export_person_model.py를 실행해 주세요.
pause
exit /b 1

