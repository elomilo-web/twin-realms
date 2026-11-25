@echo off
rem Dev loop verify: build (errors/warnings only) then capture. Usage: tools\verify.bat [viewpoint,viewpoint]
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0verify.ps1" %*
exit /b %ERRORLEVEL%
