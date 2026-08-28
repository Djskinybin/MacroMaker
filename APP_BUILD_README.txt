MACROMAKER 1.0 - BUILD / RELEASE
================================

EASIEST RELEASE BUILD
1. Double-click "Make Release.bat".
2. MacroMaker builds as a self-contained Windows x64 app.
3. The dist folder opens when it is done.

OUTPUTS
- dist\MacroMaker.exe
  Portable single-file app. Users do not need VS Code or a separate .NET runtime.

- dist\MacroMaker-Portable-v1.1.1.zip
  Easy file to send/store as the portable version.

- dist\MacroMaker-Setup.exe
  Normal Windows installer. This is created when Inno Setup 6 is installed on
  the PC doing the build.

INSTALLER
The installer:
- installs per-user, so it normally does not need Administrator access
- creates a Start Menu shortcut
- can optionally create a Desktop shortcut
- adds a normal Windows uninstall entry
- uses the MacroMaker app icon
- keeps user settings under %LOCALAPPDATA%\MacroMaker

BRANDING
App icon files are in Assets\MacroMaker.ico and Assets\MacroMakerIcon.png.
The icon is embedded into MacroMaker.exe and used by the installer/shortcuts.

USER DATA
Settings and crash logs:
%LOCALAPPDATA%\MacroMaker

Macro project files stay wherever the user chooses to save them. Updating the
app does not intentionally delete projects or settings.

SMARTSCREEN / UNKNOWN PUBLISHER
The app is not digitally code-signed yet. Windows SmartScreen may warn on a
copy downloaded from the internet, especially on another person's PC. A real
code-signing certificate is the later step that removes the "Unknown publisher"
part of the install experience.

GitHub updater:
- Repository: https://github.com/Djskinybin/MacroMaker
- Releases should use tags like v1.0.1, v1.0.4, v1.1.1
- Always attach dist\MacroMaker-Setup.exe with the exact asset name MacroMaker-Setup.exe
- The built-in updater is included starting with app version 1.0.1.
