@echo off
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"

set "PYTHON_EXE=python"
if exist ".venv\Scripts\python.exe" set "PYTHON_EXE=.venv\Scripts\python.exe"

"%PYTHON_EXE%" --version >nul 2>&1
if errorlevel 1 (
    echo Python을 찾을 수 없습니다. Python 3.10 이상을 설치해 주세요.
    pause
    exit /b 1
)

"%PYTHON_EXE%" "examples\run_mock_demo.py"
set "EXIT_CODE=%ERRORLEVEL%"

if "%~1"=="" pause
exit /b %EXIT_CODE%

