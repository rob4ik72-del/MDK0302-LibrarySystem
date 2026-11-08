@echo off
chcp 65001 >nul
cd /d "%~dp0"
title ПР 6 — Тестирование
call Build.cmd
if errorlevel 1 exit /b 1
dotnet test tests/Library.Tests --no-build --logger "trx;LogFileName=library-tests.trx" --results-directory verification
if errorlevel 1 goto fail
dotnet run --project src/Library.Tools --no-build -- parse-tests verification/library-tests.trx
if errorlevel 1 goto fail
dotnet run --project src/Library.Tools --no-build -- all
if errorlevel 1 goto fail
set "ASPNETCORE_ENVIRONMENT=Development"
start "Открытие интерфейса" /b dotnet run --project src/Library.Tools --no-build -- open-ui http://127.0.0.1:5352/#6
dotnet run --project src/Library.Api --no-build --no-launch-profile -- --urls http://127.0.0.1:5352
pause
exit /b 0
:fail
echo Операция не выполнена.
pause
exit /b 1
