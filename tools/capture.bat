@echo off
rem Dev loop capture. Usage: tools\capture.bat [viewpoint,viewpoint]   (see tools/capture.ps1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0capture.ps1" %*
exit /b %ERRORLEVEL%
