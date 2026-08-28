@echo off
setlocal
cd /d "%~dp0"
set "NOPAUSE="
if /I "%~1"=="--no-pause" set "NOPAUSE=1"

echo ========================================
echo        MacroMaker - Build App 2.0.0
echo ========================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: .NET SDK was not found.
    echo Install the .NET SDK, then run this file again.
    echo.
    if not defined NOPAUSE pause
    exit /b 1
)

if exist "dist\MacroMaker.exe" del /q "dist\MacroMaker.exe"
if not exist "dist" mkdir "dist"

echo Building self-contained Windows app...
dotnet publish "MacroMaker.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if errorlevel 1 (
    echo.
    echo BUILD FAILED.
    if not defined NOPAUSE pause
    exit /b 1
)

copy /y "bin\Release\net10.0-windows\win-x64\publish\MacroMaker.exe" "dist\MacroMaker.exe" >nul
if errorlevel 1 (
    echo.
    echo ERROR: MacroMaker.exe was not found after publishing.
    if not defined NOPAUSE pause
    exit /b 1
)

echo.
echo SUCCESS: %CD%\dist\MacroMaker.exe
if not defined NOPAUSE (
    start "" explorer.exe "%CD%\dist"
    pause
)
exit /b 0
