@echo off
chcp 65001 >nul
title Сборка Apex OLED Studio в EXE
echo ========================================================
echo   Сборка автономного Apex OLED Studio (.exe)
echo ========================================================
echo.

echo [1/3] Завершение запущенных процессов Apex OLED Studio...
powershell -Command "Get-CimInstance Win32_Process -Filter 'Name LIKE ''ApexOLEDStudio%''' | Invoke-CimMethod -MethodName Terminate" >nul 2>&1
taskkill /F /T /IM ApexOLEDStudio.exe >nul 2>&1
taskkill /F /T /IM ApexOLEDStudio.UI.exe >nul 2>&1
timeout /t 1 /nobreak >nul 2>&1

if exist "%~dp0dist" rd /s /q "%~dp0dist"

echo [2/3] Компиляция Release win-x64 (Single-File)...
dotnet publish "%~dp0ApexOLEDStudio.UI\ApexOLEDStudio.UI.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "%~dp0dist"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [!] Ошибка компиляции Release бинарника!
    pause
    exit /b 1
)

echo [3/3] Копирование исполняемого файла в корень проекта...
copy /y "%~dp0dist\ApexOLEDStudio.UI.exe" "%~dp0ApexOLEDStudio.exe" >nul
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [!] Не удалось скопировать ApexOLEDStudio.exe!
    pause
    exit /b 1
)

echo.
echo ========================================================
echo   УСПЕШНО! Файл ApexOLEDStudio.exe готов в текущей папке.
echo ========================================================
echo.
pause
