Macro Maker V1.9

Run in the MacroMaker folder:
    dotnet run

Normal app EXE build:
    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

RUNNING
- Run Start runs Starting Sequence.
- Run Current runs the selected sequence tab.
- Macro Maker hides while either run is active and returns when it finishes/stops.
- F8 stops a running macro.
- F9 pauses/resumes a running macro.

QUICK ADD
The default Quick Add stays simple:
- Move Mouse
- Click
- Wait
- Record Actions

Settings > Edit Quick Add lets you choose any other commands you want visible there.
Settings > Edit Command Defaults lets you set defaults separately for each command.

MAIN COMMANDS
Mouse:
- Move Mouse (Teleport or Smooth)
- Click / Double Click / Right Click
- Scroll
- Drag Mouse from one screen location to another
- Mouse Down / Mouse Up controls

Keyboard:
- Press Key / Combo
- Key Down / Key Up
- Type Text
- Hold Key for a duration
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
- Find + Click Color inside a selected search area

Image Detection:
- IF Image Found
- Wait Until Image
- Wait Until Image Gone
- Find + Click Image
- Find + Double Click Image
- Move To Image
- Loop Until Image
- Screen capture for reference images
- Selectable search area and tolerance

Window / App:
- Focus Window by partial window title
- Wait For Window
- Wait For Window Gone
- Open a program, file, folder, or URL

Flow:
- Run Sequence
- Loop X Times
- Loop Forever
- Break Loop
- Return
- Stop Macro

IF / LOOP BLOCKS
Select an IF, THEN/ELSE header, loop, or a command already inside a block. The add-to-block button changes to show exactly where a new command will go.
