@echo off
REM 더블클릭용 래퍼. .ps1 은 더블클릭해도 실행되지 않고 편집기로 열리거나
REM 실행 정책에 막히므로, 이 파일을 통해 띄운다.
REM
REM 창을 끝에 열어 두는 것(pause)이 요점이다 - 더블클릭으로 띄운 창은
REM 스크립트가 끝나면 닫혀서, 실패했을 때 에러를 읽을 수가 없다.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-steamcmd.ps1" %*

echo.
pause
