@echo off
chcp 65001 >nul
cd /d "%~dp0"
set "TRAVEL_DATA_DIR=%~dp0DataFiles"
set "TRAVEL_OPEN_BROWSER=0"
echo 1 - Администратор (admin)
echo 2 - Менеджер (manager)
echo 3 - Оба пользователя
echo 0 - Выход
choice /c 1230 /n /m "Выберите пользователя: "
if errorlevel 4 exit /b
if errorlevel 3 (set "TRAVEL_ACCOUNT=both") else if errorlevel 2 (set "TRAVEL_ACCOUNT=manager") else (set "TRAVEL_ACCOUNT=admin")
if exist "Runtime\win-x64\TravelAgency.exe" (
    "Runtime\win-x64\TravelAgency.exe" --change-password %TRAVEL_ACCOUNT%
) else (
    where dotnet >nul 2>nul
    if errorlevel 1 (
        echo Не найдена готовая сборка и .NET SDK. Распакуйте обновление целиком.
    ) else (
        dotnet run --project TravelAgency.csproj -c Release --no-launch-profile -- --change-password %TRAVEL_ACCOUNT%
    )
)
pause
