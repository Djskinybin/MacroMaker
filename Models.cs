using System.IO;
using System.Windows;
using System.Text.Json.Serialization;

namespace MacroMaker;

public enum CommandType
{
    Comment,

    MoveMouse,
    Click,
    DoubleClick,
    RightClick,
    Scroll,
    DragMouse,
    LeftMouseDown,
    LeftMouseUp,
    RightMouseDown,
    RightMouseUp,

    PressKey,
    KeyDown,
    KeyUp,
    TypeText,
    HoldKey,
    RepeatKey,
    WaitUntilKeyPressed,

    Wait,
    RandomWait,

    RecordedActions,

    IfColor,
    WaitUntilColor,
    LoopWhileColor,
    LoopUntilColor,
    ClickColor,

    IfImage,
    WaitUntilImage,
    WaitUntilImageGone,
    ClickImage,
    DoubleClickImage,
    MoveToImage,
    LoopUntilImage,

    FocusWindow,
    WaitForWindow,
    WaitForWindowGone,
    RunProgram,

    RunSequence,
    LoopTimes,
    LoopForever,
    Break,
    Return,
    StopMacro
}

public enum CompareMode
{
    Equals,
    NotEquals
}

public enum MouseMoveMode
{
    Legacy,
    Teleport,
    Smooth
}

public sealed class RecorderSettings
{
    public string StopHotkey { get; set; } = "F7";
    public bool RecordMouseMovement { get; set; } = true;
    public int MouseSampleMs { get; set; } = 45;
}

public sealed class MacroProject
{
    public string Name { get; set; } = "Untitled Macro";
    public List<MacroSequence> Sequences { get; set; } = new();
    public RecorderSettings RecorderSettings { get; set; } = new();
}

public sealed class MacroSequence
{
    public MacroSequence()
    {
    }

    public MacroSequence(string name)
    {
        Name = name;
    }

    public string Name { get; set; } = "Sequence";
    public List<MacroCommand> Commands { get; set; } = new();
}

public sealed class MacroCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CommandType Type { get; set; }

    public string Text { get; set; } = string.Empty;
    public string Key { get; set; } = "E";
    public string TargetSequence { get; set; } = "Starting Sequence";

    public int X { get; set; } = 960;
    public int Y { get; set; } = 300;
    public int EndX { get; set; } = 1100;
    public int EndY { get; set; } = 500;
    public MouseMoveMode MouseMoveMode { get; set; } = MouseMoveMode.Legacy;
    public int MoveDurationMs { get; set; }
    public int ClickDelayMs { get; set; } = 100;
    public int ScrollAmount { get; set; } = -120;
    public int DragDurationMs { get; set; } = 500;
    public int HoldMs { get; set; } = 500;

    public int WaitMs { get; set; } = 500;
    public int MinWaitMs { get; set; } = 250;
    public int MaxWaitMs { get; set; } = 750;

    // Recorder settings live on each Recorded Actions command.
    public string RecordingStopHotkey { get; set; } = "F7";
    public bool RecordMouseMovement { get; set; } = true;
    public int RecordMouseSampleMs { get; set; } = 45;

    public int PollMs { get; set; } = 50;
    public int TimeoutMs { get; set; }

    public string ColorHex { get; set; } = "0xFFFFFF";
    public int ColorTolerance { get; set; } = 8;
    public CompareMode CompareMode { get; set; } = CompareMode.Equals;

    public string ImagePath { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public string ProgramPath { get; set; } = string.Empty;
    public int ImageTolerance { get; set; } = 25;
    public int SearchX { get; set; }
    public int SearchY { get; set; }
    public int SearchWidth { get; set; }
    public int SearchHeight { get; set; }
    public int ImageOffsetX { get; set; }
    public int ImageOffsetY { get; set; }

    public int RepeatCount { get; set; } = 3;

    public List<MacroCommand> Children { get; set; } = new();
    public List<MacroCommand> ElseChildren { get; set; } = new();

    [JsonIgnore]
    public bool HasBody => Type is CommandType.RecordedActions
        or CommandType.IfColor
        or CommandType.LoopWhileColor
        or CommandType.LoopUntilColor
        or CommandType.IfImage
        or CommandType.LoopUntilImage
        or CommandType.LoopTimes
        or CommandType.LoopForever;

    [JsonIgnore]
    public bool HasElse => Type is CommandType.IfColor or CommandType.IfImage;

    public MacroCommand DeepClone()
    {
        return new MacroCommand
        {
            Id = Guid.NewGuid(),
            Type = Type,
            Text = Text,
            Key = Key,
            TargetSequence = TargetSequence,
            X = X,
            Y = Y,
            EndX = EndX,
            EndY = EndY,
            MouseMoveMode = MouseMoveMode,
            MoveDurationMs = MoveDurationMs,
            ClickDelayMs = ClickDelayMs,
            ScrollAmount = ScrollAmount,
            DragDurationMs = DragDurationMs,
            HoldMs = HoldMs,
            WaitMs = WaitMs,
            MinWaitMs = MinWaitMs,
            MaxWaitMs = MaxWaitMs,
            RecordingStopHotkey = RecordingStopHotkey,
            RecordMouseMovement = RecordMouseMovement,
            RecordMouseSampleMs = RecordMouseSampleMs,
            PollMs = PollMs,
            TimeoutMs = TimeoutMs,
            ColorHex = ColorHex,
            ColorTolerance = ColorTolerance,
            CompareMode = CompareMode,
            ImagePath = ImagePath,
            WindowTitle = WindowTitle,
            ProgramPath = ProgramPath,
            ImageTolerance = ImageTolerance,
            SearchX = SearchX,
            SearchY = SearchY,
            SearchWidth = SearchWidth,
            SearchHeight = SearchHeight,
            ImageOffsetX = ImageOffsetX,
            ImageOffsetY = ImageOffsetY,
            RepeatCount = RepeatCount,
            Children = Children.Select(c => c.DeepClone()).ToList(),
            ElseChildren = ElseChildren.Select(c => c.DeepClone()).ToList()
        };
    }

    public string DisplayText()
    {
        return Type switch
        {
            CommandType.Comment => $"// {Text}",
            CommandType.MoveMouse => EffectiveMoveMode() == MouseMoveMode.Teleport
                ? $"Move mouse to {X}, {Y} (teleport)"
                : $"Move mouse to {X}, {Y} smoothly over {MoveDurationMs} ms",
            CommandType.Click => $"Click {X}, {Y}" + MoveSuffix(),
            CommandType.DoubleClick => $"Double click {X}, {Y}" + MoveSuffix(),
            CommandType.RightClick => $"Right click {X}, {Y}" + MoveSuffix(),
            CommandType.Scroll => $"Scroll {ScrollAmount} at {X}, {Y}" + MoveSuffix(),
            CommandType.DragMouse => $"Drag {X}, {Y} → {EndX}, {EndY} over {DragDurationMs} ms",
            CommandType.LeftMouseDown => "Left mouse down",
            CommandType.LeftMouseUp => "Left mouse up",
            CommandType.RightMouseDown => "Right mouse down",
            CommandType.RightMouseUp => "Right mouse up",
            CommandType.PressKey => $"Press key {Key}",
            CommandType.KeyDown => $"Key down {Key}",
            CommandType.KeyUp => $"Key up {Key}",
            CommandType.TypeText => $"Type \"{TrimForDisplay(Text, 48)}\"",
            CommandType.HoldKey => $"Hold {Key} for {HoldMs} ms",
            CommandType.RepeatKey => $"Press {Key} × {RepeatCount} ({WaitMs} ms apart)",
            CommandType.WaitUntilKeyPressed => $"Wait until {Key} is pressed",
            CommandType.Wait => $"Wait {WaitMs} ms",
            CommandType.RandomWait => $"Wait random {MinWaitMs}-{MaxWaitMs} ms",
            CommandType.RecordedActions => $"Record / recorded actions ({Children.Count} commands, stop {RecordingStopHotkey})",
            CommandType.IfColor => $"IF color at {X}, {Y} {CompareSymbol()} {ColorHex} ±{ColorTolerance}",
            CommandType.WaitUntilColor => $"Wait until color at {X}, {Y} {CompareSymbol()} {ColorHex} ±{ColorTolerance}",
            CommandType.LoopWhileColor => $"Loop WHILE color at {X}, {Y} {CompareSymbol()} {ColorHex} ±{ColorTolerance}",
            CommandType.LoopUntilColor => $"Loop UNTIL color at {X}, {Y} {CompareSymbol()} {ColorHex} ±{ColorTolerance}",
            CommandType.ClickColor => $"Find + click color {ColorHex} ±{ColorTolerance}",
            CommandType.IfImage => $"IF image found: {ImageName()}",
            CommandType.WaitUntilImage => $"Wait until image appears: {ImageName()}",
            CommandType.WaitUntilImageGone => $"Wait until image disappears: {ImageName()}",
            CommandType.ClickImage => $"Find + click image: {ImageName()}" + MoveSuffix(),
            CommandType.DoubleClickImage => $"Find + double click image: {ImageName()}" + MoveSuffix(),
            CommandType.MoveToImage => $"Move to image: {ImageName()}" + MoveSuffix(),
            CommandType.LoopUntilImage => $"Loop UNTIL image found: {ImageName()}",
            CommandType.FocusWindow => $"Focus window containing \"{TrimForDisplay(WindowTitle, 36)}\"",
            CommandType.WaitForWindow => $"Wait for window \"{TrimForDisplay(WindowTitle, 36)}\"",
            CommandType.WaitForWindowGone => $"Wait for window to close \"{TrimForDisplay(WindowTitle, 36)}\"",
            CommandType.RunProgram => $"Open \"{TrimForDisplay(ProgramPath, 42)}\"",
            CommandType.RunSequence => $"Run \"{TargetSequence}\"",
            CommandType.LoopTimes => $"Loop {RepeatCount} times",
            CommandType.LoopForever => "Loop forever",
            CommandType.Break => "Break loop",
            CommandType.Return => "Return from sequence",
            CommandType.StopMacro => "Stop macro",
            _ => Type.ToString()
        };
    }

    private string CompareSymbol() => CompareMode == CompareMode.Equals ? "==" : "!=";

    private MouseMoveMode EffectiveMoveMode() => MouseMoveMode == MouseMoveMode.Legacy
        ? (MoveDurationMs > 0 ? MouseMoveMode.Smooth : MouseMoveMode.Teleport)
        : MouseMoveMode;

    private string MoveSuffix() => EffectiveMoveMode() == MouseMoveMode.Smooth
        ? $" (smooth {MoveDurationMs} ms)"
        : " (teleport)";

    private string ImageName()
    {
        if (string.IsNullOrWhiteSpace(ImagePath))
            return "<not selected>";

        try
        {
            return Path.GetFileName(ImagePath);
        }
        catch
        {
            return ImagePath;
        }
    }

    private static string TrimForDisplay(string value, int max)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var singleLine = value.Replace("\r", " ").Replace("\n", " ");
        return singleLine.Length <= max ? singleLine : singleLine[..max] + "…";
    }
}

public enum CommandBranch
{
    Root,
    Body,
    Else
}

public sealed class CommandRow
{
    public MacroCommand? Command { get; init; }
    public List<MacroCommand>? Owner { get; init; }
    public MacroCommand? ParentCommand { get; init; }
    public int Depth { get; init; }
    public string Label { get; init; } = string.Empty;
    public bool IsHeader { get; init; }
    public CommandBranch Branch { get; init; }

    public string Display => IsHeader ? Label : Command?.DisplayText() ?? string.Empty;
    public Thickness Indent => new(Math.Max(0, Depth) * 22, 0, 0, 0);
}

public readonly record struct ScreenRegion(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct ImageMatch(int X, int Y, int Width, int Height)
{
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
}
