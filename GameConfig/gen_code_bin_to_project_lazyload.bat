@echo off
setlocal
Cd /d "%~dp0"
echo %CD%

set WORKSPACE=../..
set PROJECT_ROOT=..
set LUBAN_DLL=%WORKSPACE%\Tools\Luban\Luban.dll
set CONF_ROOT=.
set DATA_OUTPATH=%PROJECT_ROOT%/Assets/AssetRaw/Configs/bytes/
set CODE_OUTPATH=%PROJECT_ROOT%/Assets/GameScripts/HotFix/GameProto/GameConfig/

if not exist "%PROJECT_ROOT%\Assets\" (
    echo [ERROR] Move this GameConfig folder to UnityProject\GameConfig before exporting.
    exit /b 1
)

set "DOTNET=dotnet"
if exist "%WORKSPACE%\Tools\dotnet\dotnet.exe" set "DOTNET=%WORKSPACE%\Tools\dotnet\dotnet.exe"
if not exist "%LUBAN_DLL%" (
    echo [ERROR] Missing Tools\Luban\Luban.dll. See GameConfig\README.md.
    exit /b 1
)

"%DOTNET%" "%LUBAN_DLL%" ^
    -t client ^
    -c cs-bin ^
    -d bin^
    --conf %CONF_ROOT%\luban.conf ^
    --customTemplateDir %CONF_ROOT%\CustomTemplate\CustomTemplate_Client_LazyLoad ^
    -x code.lineEnding=crlf ^
    -x outputCodeDir=%CODE_OUTPATH% ^
    -x outputDataDir=%DATA_OUTPATH%
if errorlevel 1 exit /b 1

copy /y "%CONF_ROOT%\CustomTemplate\ConfigSystem.cs" "%PROJECT_ROOT%\Assets\GameScripts\HotFix\GameProto\ConfigSystem.cs" >nul
if errorlevel 1 exit /b 1
copy /y "%CONF_ROOT%\CustomTemplate\ExternalTypeUtil.cs" "%PROJECT_ROOT%\Assets\GameScripts\HotFix\GameProto\ExternalTypeUtil.cs" >nul
if errorlevel 1 exit /b 1
echo [Luban] Export succeeded.
exit /b 0
