@echo off
chcp 65001 >nul
cd /d "%~dp0"
call Build.cmd
if errorlevel 1 exit /b 1
dotnet test tests\Library.Tests --no-build --logger "trx;LogFileName=library-tests.trx" --results-directory verification
if errorlevel 1 goto fail
dotnet run --project src\Library.Tools --no-build -- parse-tests verification\library-tests.trx
if errorlevel 1 goto fail
echo Результаты обновлены в verification.
pause
exit /b 0
:fail
echo Есть ошибки. Изучите вывод и журнал TRX.
pause
exit /b 1
