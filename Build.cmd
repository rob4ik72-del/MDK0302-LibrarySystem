@echo off
chcp 65001 >nul
cd /d "%~dp0"
dotnet --version
if errorlevel 1 goto fail
dotnet restore LibrarySystem.sln
if errorlevel 1 goto fail
dotnet build LibrarySystem.sln --no-restore
if errorlevel 1 goto fail
dotnet run --project src\Library.Tools --no-build -- all
if errorlevel 1 goto fail
exit /b 0
:fail
echo Ошибка. Установите .NET 8 SDK и проверьте доступ к nuget.org.
pause
exit /b 1
