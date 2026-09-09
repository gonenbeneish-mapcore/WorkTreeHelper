@echo off
setlocal
cd /d "%~dp0"
dotnet publish WorktreeHelper.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
if errorlevel 1 exit /b %errorlevel%
echo.
echo Built: %~dp0dist\WorktreeHelper.exe
