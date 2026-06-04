@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM Run from repo root no matter where this file is launched from.
cd /d "%~dp0"

:menu
cls
echo ===============================================
echo Zer0Talk Build Launcher
echo ===============================================
echo.
echo  1. Build Debug
echo  2. Build Release
echo  3. Build Debug and Release
echo.
echo ---- Handy Extras ----
echo  5. Build Debug + Release (Clean Relay Lock First)
echo  6. Restore Dependencies (dotnet restore)
echo  7. Run Tests (dotnet test)
echo  8. Pack Release (scripts\pack-release.ps1)
echo  9. Build InstallMe Lite (scripts\build-installme-lite.ps1)
echo.
echo -----------------------------------------------
echo  0. Exit
echo.
set /p choice=Select an option: 

if "%choice%"=="1" goto build_debug
if "%choice%"=="2" goto build_release
if "%choice%"=="3" goto build_both
if "%choice%"=="0" goto done
if "%choice%"=="5" goto build_both_clean_lock
if "%choice%"=="6" goto restore
if "%choice%"=="7" goto test
if "%choice%"=="8" goto pack_release
if "%choice%"=="9" goto build_lite

echo.
echo Invalid option: "%choice%"
pause
goto menu

:build_debug
call :run "dotnet build .\Zer0Talk.sln -c Debug" "Building Debug"
if errorlevel 1 goto menu
call :run "dotnet build .\Zer0Talk.RelayServer\Zer0Talk.RelayServer.csproj -c Debug" "Building Relay Server Debug"
goto menu

:build_release
call :run "dotnet build .\Zer0Talk.sln -c Release" "Building Release"
if errorlevel 1 goto menu
call :run "dotnet build .\Zer0Talk.RelayServer\Zer0Talk.RelayServer.csproj -c Release" "Building Relay Server Release"
goto menu

:build_both
call :run "dotnet build .\Zer0Talk.sln -c Debug" "Building Debug"
if errorlevel 1 goto menu
call :run "dotnet build .\Zer0Talk.RelayServer\Zer0Talk.RelayServer.csproj -c Debug" "Building Relay Server Debug"
if errorlevel 1 goto menu
call :run "dotnet build .\Zer0Talk.sln -c Release" "Building Release"
if errorlevel 1 goto menu
call :run "dotnet build .\Zer0Talk.RelayServer\Zer0Talk.RelayServer.csproj -c Release" "Building Relay Server Release"
goto menu

:build_both_clean_lock
call :run_ps1 ".\scripts\build_debug_release_clean_lock.ps1" "Building Debug + Release after relay lock cleanup"
if errorlevel 1 goto menu
call :run "dotnet build .\Zer0Talk.RelayServer\Zer0Talk.RelayServer.csproj -c Debug" "Building Relay Server Debug"
if errorlevel 1 goto menu
call :run "dotnet build .\Zer0Talk.RelayServer\Zer0Talk.RelayServer.csproj -c Release" "Building Relay Server Release"
goto menu

:restore
call :run "dotnet restore .\Zer0Talk.sln" "Restoring NuGet dependencies"
goto menu

:test
call :run "dotnet test .\Zer0Talk.sln" "Running tests"
goto menu

:pack_release
call :run_ps1 ".\scripts\pack-release.ps1" "Packing release artifacts"
goto menu

:build_lite
call :run_ps1 ".\scripts\build-installme-lite.ps1" "Building InstallMe Lite"
goto menu

:run
set "cmd=%~1"
set "label=%~2"
echo.
echo [%label%]
echo Command: %cmd%
echo.
call %cmd%
set "exitCode=%ERRORLEVEL%"
echo.
if not "%exitCode%"=="0" (
    echo FAILED with exit code %exitCode%.
) else (
    echo SUCCESS.
)
pause
exit /b %exitCode%

:run_ps1
set "scriptPath=%~1"
set "label=%~2"
echo.
echo [%label%]
echo Script: %scriptPath%
echo.
pwsh -NoProfile -ExecutionPolicy Bypass -File "%scriptPath%"
set "exitCode=%ERRORLEVEL%"
echo.
if not "%exitCode%"=="0" (
    echo FAILED with exit code %exitCode%.
) else (
    echo SUCCESS.
)
pause
exit /b %exitCode%

:done
echo Exiting build launcher.
endlocal
exit /b 0
