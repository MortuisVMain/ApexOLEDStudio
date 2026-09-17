@echo off
setlocal
echo ========================================================
echo   Starting Apex OLED Studio...
echo ========================================================
cd /d "%~dp0"

:: 1. Force kill any running instances to release DLL locks
taskkill /F /T /IM ApexOLEDStudio.UI.exe >nul 2>&1

:: 2. Brief wait to let Windows kernel fully release file locks
timeout /t 1 /nobreak >nul 2>&1

:: 3. Clean build
dotnet build "ApexOLEDStudio.UI\ApexOLEDStudio.UI.csproj" -c Debug
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Build failed! Check errors above.
    pause
    exit /b %ERRORLEVEL%
)

:: 4. Launch the application
start "" "%~dp0ApexOLEDStudio.UI\bin\Debug\net10.0-windows\ApexOLEDStudio.UI.exe"
echo.
echo [OK] Apex OLED Studio launched!
