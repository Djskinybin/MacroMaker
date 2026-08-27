@echo off
setlocal
cd /d "%~dp0"

echo ========================================
echo       MacroMaker - MAKE RELEASE
echo ========================================
echo.

call "Build App.bat" --no-pause
if errorlevel 1 (
    echo Release stopped because the app build failed.
    pause
    exit /b 1
)

if exist "dist\MacroMaker-Portable-v1.0.0.zip" del /q "dist\MacroMaker-Portable-v1.0.0.zip"
echo Creating portable ZIP...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path 'dist\MacroMaker.exe' -DestinationPath 'dist\MacroMaker-Portable-v1.0.0.zip' -Force"
if errorlevel 1 (
    echo WARNING: Portable ZIP could not be created.
)

set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if exist "%ISCC%" (
    echo Building installer...
    "%ISCC%" "installer\MacroMaker.iss"
    if errorlevel 1 (
        echo WARNING: Installer build failed, but the portable app is ready.
    )
) else (
    echo.
    echo Inno Setup 6 is not installed, so the installer was skipped.
    echo Install Inno Setup 6 whenever you want MacroMaker-Setup.exe.
)

echo.
echo ========================================
echo              RELEASE READY
echo ========================================
echo dist\MacroMaker.exe
echo dist\MacroMaker-Portable-v1.0.0.zip
if exist "dist\MacroMaker-Setup.exe" echo dist\MacroMaker-Setup.exe

echo.
start "" explorer.exe "%CD%\dist"
pause
