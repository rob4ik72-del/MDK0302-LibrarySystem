@echo off
chcp 65001 >nul
cd /d "%~dp0"
title ПР 2 — Гармонизация
call Build.cmd
if errorlevel 1 exit /b 1
dotnet run --project src/Library.Tools --no-build -- harmonization
if errorlevel 1 goto fail
set "ASPNETCORE_ENVIRONMENT=Development"
start "Открытие интерфейса" /b dotnet run --project src/Library.Tools --no-build -- open-ui http://127.0.0.1:5352/#2
dotnet run --project src/Library.Api --no-build --no-launch-profile -- --urls http://127.0.0.1:5352
pause
exit /b 0
:fail
echo Операция не выполнена.
pause
exit /b 1
