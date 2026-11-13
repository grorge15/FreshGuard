@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Export-Configs.ps1"
exit /b %ERRORLEVEL%
