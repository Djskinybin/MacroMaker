MacroMaker v2.0.0

Run from source:
    dotnet run

Build the normal app/installer:
    Make Release.bat

RUNNING
- Run Start runs Starting Sequence.
- Run Current runs the selected sequence tab.
- MacroMaker hides while a run is active and returns when it finishes/stops.
- Settings > Controls & Running lets you change the Stop and Pause/Resume hotkeys.
- Optional Run Start / Run Current global hotkeys can also be set there.
- Optional mouse movement lock blocks physical mouse movement while a macro is actively running, but releases it while paused.
- Optional live status HUD appears in the top-left while running and shows current color/image/window waits and image-priority scans.

MOUSE MOVEMENT
- Teleport moves instantly.
- Smooth uses high-frequency Windows mouse input instead of large 10 ms coordinate jumps.
- Smooth movement respects the selected duration; the default is 50 ms and high-frequency intermediate input keeps the cursor gliding.

QUICK ADD
Default Quick Add stays simple:
- Move Mouse
- Click
- Wait
- Record Actions

Settings > Edit Quick Add lets you choose any other commands.
Settings > Edit Command Defaults lets you set defaults per command.

PROJECT FOLDERS
MacroMaker projects are folders:

My Macro\
  macro.json
  Images\

Save As creates the project folder automatically.
Open selects a project folder and loads macro.json inside it.

PROJECT IMAGE LIBRARY
- Import Image copies one image into Images.
- Import Folder copies the entire folder into Images and preserves nested folders.
- Any imported image/folder can be reused by any image-based command from Project Image Library.
- A folder can point at a nested source such as Images\upgrades\rare.
- Folder image sources have a saved priority order; the first matching image wins.
- During a run the live HUD shows which priority image is currently being tested.

MAIN COMMANDS
Mouse:
- Move Mouse (Teleport or Smooth)
- Click / Double Click / Right Click
- Scroll
- Drag Mouse
- Advanced Mouse Down / Mouse Up

Keyboard:
- Press Key / Combo
- Key Down / Key Up
- Type Text
- Hold Key
- Repeat Key
- Wait Until Key Pressed

Timing:
- Wait
- Random Wait

Recording:
- Record Actions block with per-block stop hotkey and editable recorded actions

Color / Pixel:
- IF Color
- Wait Until Color at Location
- Loop While Color
- Loop Until Color
- Find + Click Color

Image Detection:
- IF Image Found
- Wait Until Image
- Wait Until Image Gone
- Find + Click Image
- Find + Double Click Image
- Move To Image
- Loop Until Image
- Single images or priority folders
- Nested project image folders
- Search area and tolerance

Window / App:
- Focus Window
- Wait For Window
- Wait For Window Gone
- Open program/file/folder/URL

Flow:
- Run Sequence
- Loop X Times
- Loop Forever
- Break Loop
- Return
- Stop Macro

AUTO UPDATES
MacroMaker checks GitHub Releases from Djskinybin/MacroMaker.
A release tag should look like v2.0.0 and include MacroMaker-Setup.exe.


V1.4 POLISH
- Ctrl+Z / Ctrl+Y Undo/Redo
- Auto-save saved projects
- Test Selected and Check Macro buttons
- All commands are available in Add Command; Quick Add stays customizable.

V2.0.0
- All commands always visible in Add Command
- Pause/Resume default F7
- Smooth mouse default 50 ms
- Update Now asks about unsaved macro changes before installing
