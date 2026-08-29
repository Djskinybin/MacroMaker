@echo off
setlocal
cd /d "%~dp0"

echo ========================================
echo      MacroMaker - Build Installer
echo ========================================
echo.

call "Build App.bat" --no-pause
if errorlevel 1 (
    pause
    exit /b 1
)

set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if not exist "%ISCC%" (
    echo.
    echo Inno Setup 6 was not found.
    echo Install Inno Setup 6, then run Build Installer.bat again.
    echo Your standalone MacroMaker.exe was still built successfully.
    echo.
    pause
    exit /b 1
)

echo Building MacroMaker-Setup.exe...
"%ISCC%" "installer\MacroMaker.iss"
if errorlevel 1 (
    echo.
    echo INSTALLER BUILD FAILED.
    pause
    exit /b 1
)

echo.
echo SUCCESS: %CD%\dist\MacroMaker-Setup.exe
start "" explorer.exe "%CD%\dist"
pause
