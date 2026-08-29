namespace MacroMaker;

internal static class CommandHelp
{
    public static string Get(CommandType type) => type switch
    {
        CommandType.MoveMouse => "Moves the mouse to a spot.",
        CommandType.Click => "Moves to a spot, then left-clicks.",
        CommandType.DoubleClick => "Double-clicks at a spot.",
        CommandType.RightClick => "Right-clicks at a spot.",
        CommandType.Scroll => "Moves to a spot, then scrolls.",
        CommandType.DragMouse => "Drags from one spot to another.",
        CommandType.LeftMouseDown => "Holds the left mouse button down.",
        CommandType.LeftMouseUp => "Releases the left mouse button.",
        CommandType.RightMouseDown => "Holds the right mouse button down.",
        CommandType.RightMouseUp => "Releases the right mouse button.",

        CommandType.PressKey => "Presses a key or key combo once.",
        CommandType.KeyDown => "Holds a key down until Key Up.",
        CommandType.KeyUp => "Releases a held key.",
        CommandType.TypeText => "Types the text you enter.",
        CommandType.HoldKey => "Holds a key for a set time.",
        CommandType.RepeatKey => "Presses the same key more than once.",
        CommandType.WaitUntilKeyPressed => "Waits until a key is pressed.",
        CommandType.WaitUntilKeyReleased => "Waits until a key is released.",
        CommandType.IfKeyPressed => "Runs THEN if the key is pressed.",
        CommandType.LoopWhileKeyPressed => "Repeats while the key is held.",

        CommandType.Wait => "Pauses the macro for a set time.",
        CommandType.RandomWait => "Waits for a random time in a range.",
        CommandType.RecordedActions => "Records actions and replays them here.",

        CommandType.IfColor => "Checks one pixel, then runs THEN if it matches.",
        CommandType.WaitUntilColor => "Waits until a pixel matches a color.",
        CommandType.LoopWhileColor => "Repeats while a pixel matches.",
        CommandType.LoopUntilColor => "Repeats until a pixel matches.",
        CommandType.ClickColor => "Finds a color in an area and clicks it.",
        CommandType.FindColorToVariables => "Finds a color and saves where it was found.",
        CommandType.SampleColorToVariable => "Reads a pixel color and saves it.",

        CommandType.IfImage => "Checks for an image, then runs THEN if found.",
        CommandType.WaitUntilImage => "Waits until an image appears.",
        CommandType.WaitUntilImageGone => "Waits until an image disappears.",
        CommandType.ClickImage => "Finds an image and clicks it.",
        CommandType.DoubleClickImage => "Finds an image and double-clicks it.",
        CommandType.MoveToImage => "Finds an image and moves the mouse to it.",
        CommandType.LoopUntilImage => "Repeats until an image appears.",
        CommandType.LoopWhileImage => "Repeats while an image is visible.",
        CommandType.FindImageToVariables => "Finds an image and saves where it was found.",

        CommandType.IfWindow => "Checks if a window is open.",
        CommandType.FocusWindow => "Brings a window to the front.",
        CommandType.WaitForWindow => "Waits until a window opens.",
        CommandType.WaitForWindowGone => "Waits until a window closes.",
        CommandType.MinimizeWindow => "Minimizes a window.",
        CommandType.MaximizeWindow => "Maximizes a window.",
        CommandType.RestoreWindow => "Returns a window to normal size.",
        CommandType.CloseWindow => "Closes a window.",
        CommandType.RunProgram => "Opens a program, file, folder, or URL.",

        CommandType.SetVariable => "Saves a value for later.",
        CommandType.AddVariable => "Changes a saved value. Use 1 to add or -1 to subtract.",
        CommandType.RandomNumber => "Picks a random number and saves it for later.",
        CommandType.IfVariable => "Runs THEN only when a saved value passes your check.",
        CommandType.WaitUntilVariable => "Waits until a saved value passes your check.",
        CommandType.LoopWhileVariable => "Repeats while a saved value matches.",
        CommandType.LoopUntilVariable => "Repeats until a saved value matches.",

        CommandType.SetClipboard => "Changes what is currently copied.",
        CommandType.ClipboardToVariable => "Saves copied text into a variable.",
        CommandType.ReadTextFile => "Reads a text file into a variable.",
        CommandType.WriteTextFile => "Writes text into a file.",
        CommandType.PromptText => "Asks a question and saves the answer.",
        CommandType.PromptYesNo => "Asks Yes or No and saves true or false.",

        CommandType.Group => "Keeps related commands together so the macro is easier to read.",
        CommandType.RunSequence => "Runs another tab, then returns here when that tab finishes.",
        CommandType.LoopTimes => "Repeats commands a set number of times.",
        CommandType.LoopForever => "Repeats until stopped or Break Loop is used.",
        CommandType.Break => "Leaves the nearest loop.",
        CommandType.Return => "Ends this tab and returns to the caller.",
        CommandType.StopMacro => "Stops the whole macro.",
        CommandType.Comment => "Adds a note that does not run.",
        _ => string.Empty
    };
}
