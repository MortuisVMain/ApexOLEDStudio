@echo off
chcp 65001 >nul
setlocal
echo ========================================================
echo   Настройка запуска Apex OLED Studio без UAC
echo   (Создание задачи с наивысшими правами в Планировщике)
echo ========================================================
echo.

:: 1. Проверка прав Администратора для единовременной регистрации задачи
net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [ЗАПРОС ПРАВ] Требуется подтвердить права Администратора ОДИН РАЗ,
    echo                чтобы зарегистрировать задачу в системе.
    echo.
    powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"
set "EXE_PATH=%~dp0ApexOLEDStudio.exe"

if not exist "%EXE_PATH%" (
    if exist "%~dp0ApexOLEDStudio.UI\bin\Debug\net10.0-windows\ApexOLEDStudio.UI.exe" (
        set "EXE_PATH=%~dp0ApexOLEDStudio.UI\bin\Debug\net10.0-windows\ApexOLEDStudio.UI.exe"
    ) else (
        echo [СБОРКА] Исполняемый файл не найден. Выполняется компиляция...
        call "%~dp0Build-EXE.bat"
    )
)

if not exist "%EXE_PATH%" (
    echo [ОШИБКА] Не удалось найти или скомпилировать ApexOLEDStudio.exe!
    pause
    exit /b 1
)

:: 2. Регистрация задачи в Планировщике Windows с наивысшими правами
echo [1/3] Регистрация задачи 'ApexOLEDStudio' в Планировщике Windows...
schtasks /create /tn "ApexOLEDStudio" /tr "\"%EXE_PATH%\"" /rl HIGHEST /sc ONLOGON /f >nul
if %ERRORLEVEL% NEQ 0 (
    echo [ОШИБКА] Не удалось создать задачу в Планировщике заданий.
    pause
    exit /b %ERRORLEVEL%
)

:: 3. Создание бесшумного скрипта запуска (без мигания черного окна cmd)
set "RUN_VBS=%~dp0launch_no_uac.vbs"
(
    echo Set WshShell = CreateObject^("WScript.Shell"^)
    echo WshShell.Run "schtasks /run /tn ""ApexOLEDStudio""", 0, False
) > "%RUN_VBS%"

:: 4. Создание ярлыка на Рабочем столе
echo [2/3] Создание ярлыка на Рабочем столе пользователя...
powershell -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\Apex OLED Studio.lnk'); $s.TargetPath = 'wscript.exe'; $s.Arguments = '\"%RUN_VBS%\"'; $s.IconLocation = '%EXE_PATH%,0'; $s.Description = 'Запуск Apex OLED Studio с правами Администратора без UAC'; $s.Save()"

echo [3/3] Запуск приложения прямо сейчас без UAC...
wscript.exe "%RUN_VBS%"

echo.
echo ========================================================
echo   ГОТОВО! 🚀
echo.
echo   1. На Рабочем столе создан ярлык 'Apex OLED Studio'.
echo   2. Приложение будет запускаться с полными правами Администратора
echo      БЕЗ единого всплывающего окна UAC!
echo   3. Оно также автоматически запускается при входе в Windows.
echo ========================================================
echo.
pause
