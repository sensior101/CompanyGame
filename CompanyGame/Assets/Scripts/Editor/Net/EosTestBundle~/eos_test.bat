@echo off
chcp 65001 >nul
cd /d "%~dp0"
set PUID=
set /p PUID=서버 PUID를 붙여 넣고 Enter: 
if "%PUID%"=="" (echo PUID가 비어 있습니다. & pause & exit /b 1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0eos_test.ps1" client %PUID%
echo.
type eos_test_log.txt
echo.
echo 결과 파일: %~dp0eos_test_log.txt
echo 이 파일을 보내 주세요.
pause
