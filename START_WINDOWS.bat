@echo off
chcp 65001 >nul
cd /d "%~dp0"
set "TRAVEL_DATA_DIR=%~dp0DataFiles"
set "TRAVEL_OPEN_BROWSER=1"
if exist "Runtime\win-x64\TravelAgency.exe" (
    "Runtime\win-x64\TravelAgency.exe"
) else (
    where dotnet >nul 2>nul
    if errorlevel 1 (
        echo Не найдена готовая сборка и .NET SDK. Распакуйте архив целиком.
        echo Для сборки исходников установите .NET 10 SDK.
    ) else (
        dotnet run --project TravelAgency.csproj -c Release --no-launch-profile
    )
)
pause
