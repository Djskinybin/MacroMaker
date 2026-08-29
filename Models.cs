using System.IO;
using System.ComponentModel;
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
    WaitUntilKeyReleased,
    IfKeyPressed,
    LoopWhileKeyPressed,

    Wait,
    RandomWait,

    RecordedActions,

    IfColor,
    WaitUntilColor,
    LoopWhileColor,
    LoopUntilColor,
    ClickColor,
    FindColorToVariables,
    SampleColorToVariable,

    IfImage,
    WaitUntilImage,
    WaitUntilImageGone,
    ClickImage,
    DoubleClickImage,
    MoveToImage,
    LoopUntilImage,
    LoopWhileImage,
    FindImageToVariables,

    IfWindow,
    FocusWindow,
    WaitForWindow,
    WaitForWindowGone,
    MinimizeWindow,
    MaximizeWindow,
    RestoreWindow,
    CloseWindow,
    RunProgram,

    SetVariable,
    AddVariable,
    RandomNumber,
    IfVariable,
    WaitUntilVariable,
    LoopWhileVariable,
    LoopUntilVariable,

    SetClipboard,
    ClipboardToVariable,
    ReadTextFile,
    WriteTextFile,
    PromptText,
    PromptYesNo,

    Group,
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

public enum VariableCompareMode
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Contains,
    StartsWith,
    EndsWith
}

public enum MouseMoveMode
{
    Legacy,
    Teleport,
    Smooth
}

public enum CoordinateMode
{
    Screen,
    ActiveWindow,
    RelativeToMouse
}

public enum FailureAction
{
    Continue,
    StopMacro,
    Retry,
    RunSequence
}

public enum HudCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public sealed class RecorderSettings
{
    public string StopHotkey { get; set; } = "F7";
    public bool RecordMouseMovement { get; set; } = true;
    public int MouseSampleMs { get; set; } = 45;
}

public sealed class ProjectVariable
{
    public string Name { get; set; } = "Variable";
    public string Value { get; set; } = "0";
    public string Description { get; set; } = string.Empty;
    public bool UserEditable { get; set; } = true;

    public ProjectVariable DeepClone() => new()
    {
        Name = Name,
        Value = Value,
        Description = Description,
        UserEditable = UserEditable
    };
}

public sealed class MacroRuntimeSettings
{
    public bool UseProjectRuntimeSettings { get; set; }
    public string StartupSequence { get; set; } = "Starting Sequence";
    public bool LockMouseMovementWhileRunning { get; set; }
    public bool ShowRunStatusHud { get; set; } = true;
    public int PlaybackSpeedPercent { get; set; } = 100;
    public HudCorner HudCorner { get; set; } = HudCorner.TopLeft;
    public int HudOpacityPercent { get; set; } = 92;
    public bool PromptForVariablesOnRun { get; set; }
}

public sealed class MacroProject
{
    public string Name { get; set; } = "Untitled Macro";
    public List<MacroSequence> Sequences { get; set; } = new();
    public RecorderSettings RecorderSettings { get; set; } = new();
    public List<ProjectVariable> Variables { get; set; } = new();
    public MacroRuntimeSettings RuntimeSettings { get; set; } = new();
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
    public bool Enabled { get; set; } = true;
    public string CustomName { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
    public string Key { get; set; } = "E";
    public string TargetSequence { get; set; } = "Starting Sequence";

    public int X { get; set; }
    public int Y { get; set; }
    public int EndX { get; set; }
    public int EndY { get; set; }
    public string XExpression { get; set; } = string.Empty;
    public string YExpression { get; set; } = string.Empty;
    public string EndXExpression { get; set; } = string.Empty;
    public string EndYExpression { get; set; } = string.Empty;
    public CoordinateMode CoordinateMode { get; set; } = CoordinateMode.Screen;
    public MouseMoveMode MouseMoveMode { get; set; } = MouseMoveMode.Legacy;
    public int MoveDurationMs { get; set; }
    public int ClickDelayMs { get; set; } = 100;
    public int ScrollAmount { get; set; } = -120;
    public int DragDurationMs { get; set; } = 500;
    public int HoldMs { get; set; } = 500;

    public int WaitMs { get; set; } = 500;
    public string WaitExpression { get; set; } = string.Empty;
    public int MinWaitMs { get; set; } = 250;
    public int MaxWaitMs { get; set; } = 750;

    // Recorder settings live on each Recorded Actions command.
    public string RecordingStopHotkey { get; set; } = "F7";
    public bool RecordMouseMovement { get; set; } = true;
    public int RecordMouseSampleMs { get; set; } = 45;

    public int PollMs { get; set; } = 50;
    public int TimeoutMs { get; set; }

    public string ColorHex { get; set; } = "0x000000";
    public int ColorTolerance { get; set; }
    public CompareMode CompareMode { get; set; } = CompareMode.Equals;

    public string ImagePath { get; set; } = string.Empty;
    public string ImageFolder { get; set; } = string.Empty;
    public List<string> ImagePriority { get; set; } = new();
    public bool ImageIncludeSubfolders { get; set; } = true;
    public string WindowTitle { get; set; } = string.Empty;
    public string ProgramPath { get; set; } = string.Empty;
    public string ProgramArguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public int ImageTolerance { get; set; } = 25;
    public int SearchX { get; set; }
    public int SearchY { get; set; }
    public int SearchWidth { get; set; }
    public int SearchHeight { get; set; }
    public int ImageOffsetX { get; set; }
    public int ImageOffsetY { get; set; }

    public int RepeatCount { get; set; } = 3;
    public string RepeatExpression { get; set; } = string.Empty;

    // Variables / data.
    public string VariableName { get; set; } = string.Empty;
    public string VariableValue { get; set; } = "0";
    public string VariableValue2 { get; set; } = "100";
    public VariableCompareMode VariableCompareMode { get; set; } = VariableCompareMode.Equals;
    public string StoreXVariable { get; set; } = string.Empty;
    public string StoreYVariable { get; set; } = string.Empty;
    public string StoreTextVariable { get; set; } = string.Empty;

    // Numeric property overrides. These let normal number fields use saved variables
    // or formulas without changing the underlying fallback number. Keys are MacroCommand
    // property names, for example "WaitMs" or "ScrollAmount".
    public Dictionary<string, string> ValueExpressions { get; set; } = new();

    // File / prompt behavior.
    public string FilePath { get; set; } = string.Empty;
    public bool AppendFile { get; set; }
    public string PromptText { get; set; } = "Enter a value:";

    // Failure / retry behavior for commands that can fail or time out.
    public FailureAction FailureAction { get; set; } = FailureAction.Continue;
    public int FailureRetryCount { get; set; } = 2;
    public int FailureRetryDelayMs { get; set; } = 250;
    public string FailureSequence { get; set; } = "Starting Sequence";

    public List<MacroCommand> Children { get; set; } = new();
    public List<MacroCommand> ElseChildren { get; set; } = new();

    [JsonIgnore]
    public bool HasBody => Type is CommandType.RecordedActions
        or CommandType.IfKeyPressed
        or CommandType.LoopWhileKeyPressed
        or CommandType.IfColor
        or CommandType.LoopWhileColor
        or CommandType.LoopUntilColor
        or CommandType.IfImage
        or CommandType.LoopUntilImage
        or CommandType.LoopWhileImage
        or CommandType.IfWindow
        or CommandType.IfVariable
        or CommandType.LoopWhileVariable
        or CommandType.LoopUntilVariable
        or CommandType.Group
        or CommandType.LoopTimes
        or CommandType.LoopForever;

    [JsonIgnore]
    public bool HasElse => Type is CommandType.IfKeyPressed
        or CommandType.IfColor
        or CommandType.IfImage
        or CommandType.IfWindow
        or CommandType.IfVariable;

    public MacroCommand DeepClone()
    {
        return new MacroCommand
        {
            Id = Guid.NewGuid(),
            Type = Type,
            Enabled = Enabled,
            CustomName = CustomName,
            Text = Text,
            Key = Key,
            TargetSequence = TargetSequence,
            X = X,
            Y = Y,
            EndX = EndX,
            EndY = EndY,
            XExpression = XExpression,
            YExpression = YExpression,
            EndXExpression = EndXExpression,
            EndYExpression = EndYExpression,
            CoordinateMode = CoordinateMode,
            MouseMoveMode = MouseMoveMode,
            MoveDurationMs = MoveDurationMs,
            ClickDelayMs = ClickDelayMs,
            ScrollAmount = ScrollAmount,
            DragDurationMs = DragDurationMs,
            HoldMs = HoldMs,
            WaitMs = WaitMs,
            WaitExpression = WaitExpression,
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
            ImageFolder = ImageFolder,
            ImagePriority = ImagePriority.ToList(),
            ImageIncludeSubfolders = ImageIncludeSubfolders,
            WindowTitle = WindowTitle,
            ProgramPath = ProgramPath,
            ProgramArguments = ProgramArguments,
            WorkingDirectory = WorkingDirectory,
            ImageTolerance = ImageTolerance,
            SearchX = SearchX,
            SearchY = SearchY,
            SearchWidth = SearchWidth,
            SearchHeight = SearchHeight,
            ImageOffsetX = ImageOffsetX,
            ImageOffsetY = ImageOffsetY,
            RepeatCount = RepeatCount,
            RepeatExpression = RepeatExpression,
            VariableName = VariableName,
            VariableValue = VariableValue,
            VariableValue2 = VariableValue2,
            VariableCompareMode = VariableCompareMode,
            StoreXVariable = StoreXVariable,
            StoreYVariable = StoreYVariable,
            StoreTextVariable = StoreTextVariable,
            ValueExpressions = ValueExpressions is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(ValueExpressions, StringComparer.OrdinalIgnoreCase),
            FilePath = FilePath,
            AppendFile = AppendFile,
            PromptText = PromptText,
            FailureAction = FailureAction,
            FailureRetryCount = FailureRetryCount,
            FailureRetryDelayMs = FailureRetryDelayMs,
            FailureSequence = FailureSequence,
            Children = Children.Select(c => c.DeepClone()).ToList(),
            ElseChildren = ElseChildren.Select(c => c.DeepClone()).ToList()
        };
    }

    internal MacroCommand ShallowCloneForExecution() => (MacroCommand)MemberwiseClone();

    public string DisplayText()
    {
        var body = BaseDisplayText();
        if (!string.IsNullOrWhiteSpace(CustomName))
            body = $"{CustomName}  —  {body}";
        if (!Enabled)
            body = $"[OFF] {body}";
        return body;
    }

    private string BaseDisplayText()
    {
        var x = CoordinateDisplay(XExpression, X);
        var y = CoordinateDisplay(YExpression, Y);
        var ex = CoordinateDisplay(EndXExpression, EndX);
        var ey = CoordinateDisplay(EndYExpression, EndY);

        return Type switch
        {
            CommandType.Comment => $"// {Text}",
            CommandType.MoveMouse => EffectiveMoveMode() == MouseMoveMode.Teleport
                ? $"Move mouse to {x}, {y} (teleport){CoordinateSuffix()}"
                : $"Move mouse to {x}, {y} smoothly over {NumberDisplay(nameof(MoveDurationMs), MoveDurationMs)} ms{CoordinateSuffix()}",
            CommandType.Click => $"Click {x}, {y}" + MoveSuffix() + CoordinateSuffix(),
            CommandType.DoubleClick => $"Double click {x}, {y}" + MoveSuffix() + CoordinateSuffix(),
            CommandType.RightClick => $"Right click {x}, {y}" + MoveSuffix() + CoordinateSuffix(),
            CommandType.Scroll => $"Scroll {NumberDisplay(nameof(ScrollAmount), ScrollAmount)} at {x}, {y}" + MoveSuffix() + CoordinateSuffix(),
            CommandType.DragMouse => $"Drag {x}, {y} → {ex}, {ey} over {NumberDisplay(nameof(DragDurationMs), DragDurationMs)} ms{CoordinateSuffix()}",
            CommandType.LeftMouseDown => "Left mouse down",
            CommandType.LeftMouseUp => "Left mouse up",
            CommandType.RightMouseDown => "Right mouse down",
            CommandType.RightMouseUp => "Right mouse up",
            CommandType.PressKey => $"Press key {Key}",
            CommandType.KeyDown => $"Key down {Key}",
            CommandType.KeyUp => $"Key up {Key}",
            CommandType.TypeText => $"Type \"{TrimForDisplay(Text, 48)}\"",
            CommandType.HoldKey => $"Hold {Key} for {NumberDisplay(nameof(HoldMs), HoldMs)} ms",
            CommandType.RepeatKey => $"Press {Key} × {RepeatDisplay()} ({NumberDisplay(nameof(WaitMs), WaitMs)} ms apart)",
            CommandType.WaitUntilKeyPressed => $"Wait until {Key} is pressed",
            CommandType.WaitUntilKeyReleased => $"Wait until {Key} is released",
            CommandType.IfKeyPressed => $"If key {Key} is pressed",
            CommandType.LoopWhileKeyPressed => $"Repeat while key {Key} is pressed",
            CommandType.Wait => $"Wait {WaitDisplay()} ms",
            CommandType.RandomWait => $"Wait random {NumberDisplay(nameof(MinWaitMs), MinWaitMs)}-{NumberDisplay(nameof(MaxWaitMs), MaxWaitMs)} ms",
            CommandType.RecordedActions => $"Recorded actions ({Children.Count} commands, stop {RecordingStopHotkey})",
            CommandType.IfColor => $"If color at {x}, {y} {FriendlyColorCompare()} {ColorHex} ±{NumberDisplay(nameof(ColorTolerance), ColorTolerance)}{CoordinateSuffix()}",
            CommandType.WaitUntilColor => $"Wait until color at {x}, {y} {FriendlyColorCompare()} {ColorHex} ±{NumberDisplay(nameof(ColorTolerance), ColorTolerance)}{CoordinateSuffix()}",
            CommandType.LoopWhileColor => $"Repeat while color at {x}, {y} {FriendlyColorCompare()} {ColorHex} ±{NumberDisplay(nameof(ColorTolerance), ColorTolerance)}{CoordinateSuffix()}",
            CommandType.LoopUntilColor => $"Repeat until color at {x}, {y} {FriendlyColorCompare()} {ColorHex} ±{NumberDisplay(nameof(ColorTolerance), ColorTolerance)}{CoordinateSuffix()}",
            CommandType.ClickColor => $"Find + click color {ColorHex} ±{NumberDisplay(nameof(ColorTolerance), ColorTolerance)}",
            CommandType.FindColorToVariables => $"Find color {ColorHex} → {StoreXVariable}, {StoreYVariable}",
            CommandType.SampleColorToVariable => $"Read color at {x}, {y} → {StoreTextVariable}{CoordinateSuffix()}",
            CommandType.IfImage => $"If image is found: {ImageName()}",
            CommandType.WaitUntilImage => $"Wait until image appears: {ImageName()}",
            CommandType.WaitUntilImageGone => $"Wait until image disappears: {ImageName()}",
            CommandType.ClickImage => $"Find + click image: {ImageName()}" + MoveSuffix(),
            CommandType.DoubleClickImage => $"Find + double click image: {ImageName()}" + MoveSuffix(),
            CommandType.MoveToImage => $"Move to image: {ImageName()}" + MoveSuffix(),
            CommandType.LoopUntilImage => $"Repeat until image is found: {ImageName()}",
            CommandType.LoopWhileImage => $"Repeat while image is visible: {ImageName()}",
            CommandType.FindImageToVariables => $"Find image {ImageName()} → {StoreXVariable}, {StoreYVariable}",
            CommandType.IfWindow => $"If window is open \"{TrimForDisplay(WindowTitle, 36)}\"",
            CommandType.FocusWindow => $"Focus window containing \"{TrimForDisplay(WindowTitle, 36)}\"",
            CommandType.WaitForWindow => $"Wait for window \"{TrimForDisplay(WindowTitle, 36)}\"",
            CommandType.WaitForWindowGone => $"Wait for window to close \"{TrimForDisplay(WindowTitle, 36)}\"",
            CommandType.MinimizeWindow => $"Minimize window \"{TrimForDisplay(WindowTitle, 36)}\"",
            CommandType.MaximizeWindow => $"Maximize window \"{TrimForDisplay(WindowTitle, 36)}\"",
            CommandType.RestoreWindow => $"Restore window \"{TrimForDisplay(WindowTitle, 36)}\"",
            CommandType.CloseWindow => $"Close window \"{TrimForDisplay(WindowTitle, 36)}\"",
            CommandType.RunProgram => $"Open \"{TrimForDisplay(ProgramPath, 42)}\"",
            CommandType.SetVariable => $"Save {VariableName} = {TrimForDisplay(VariableValue, 40)}",
            CommandType.AddVariable => $"Change {VariableName} by {TrimForDisplay(VariableValue, 28)}",
            CommandType.RandomNumber => $"Random {VariableName} = {TrimForDisplay(VariableValue, 18)}..{TrimForDisplay(VariableValue2, 18)}",
            CommandType.IfVariable => $"If {VariableName} {FriendlyVariableCompare()} {TrimForDisplay(VariableValue, 30)}",
            CommandType.WaitUntilVariable => $"Wait until {VariableName} {FriendlyVariableCompare()} {TrimForDisplay(VariableValue, 30)}",
            CommandType.LoopWhileVariable => $"Repeat while {VariableName} {FriendlyVariableCompare()} {TrimForDisplay(VariableValue, 30)}",
            CommandType.LoopUntilVariable => $"Repeat until {VariableName} {FriendlyVariableCompare()} {TrimForDisplay(VariableValue, 30)}",
            CommandType.SetClipboard => $"Set clipboard to \"{TrimForDisplay(Text, 38)}\"",
            CommandType.ClipboardToVariable => $"Clipboard → {VariableName}",
            CommandType.ReadTextFile => $"Read file → {VariableName}",
            CommandType.WriteTextFile => $"{(AppendFile ? "Append" : "Write")} text file",
            CommandType.PromptText => $"Ask user → {VariableName}",
            CommandType.PromptYesNo => $"Ask Yes/No → {VariableName}",
            CommandType.Group => string.IsNullOrWhiteSpace(CustomName) ? "Command group" : "Group",
            CommandType.RunSequence => $"Run tab \"{TargetSequence}\"",
            CommandType.LoopTimes => $"Repeat {RepeatDisplay()} times",
            CommandType.LoopForever => "Repeat forever",
            CommandType.Break => "Exit current repeat",
            CommandType.Return => "Return to previous tab",
            CommandType.StopMacro => "Stop macro",
            _ => Type.ToString()
        };
    }

    private string CompareSymbol() => CompareMode == CompareMode.Equals ? "==" : "!=";

    private string FriendlyColorCompare() => CompareMode == CompareMode.Equals ? "matches" : "does not match";

    private string FriendlyVariableCompare() => VariableCompareMode switch
    {
        VariableCompareMode.Equals => "equals",
        VariableCompareMode.NotEquals => "does not equal",
        VariableCompareMode.GreaterThan => "is greater than",
        VariableCompareMode.GreaterThanOrEqual => "is at least",
        VariableCompareMode.LessThan => "is less than",
        VariableCompareMode.LessThanOrEqual => "is at most",
        VariableCompareMode.Contains => "contains",
        VariableCompareMode.StartsWith => "starts with",
        VariableCompareMode.EndsWith => "ends with",
        _ => "equals"
    };

    private string VariableCompareSymbol() => VariableCompareMode switch
    {
        VariableCompareMode.Equals => "==",
        VariableCompareMode.NotEquals => "!=",
        VariableCompareMode.GreaterThan => ">",
        VariableCompareMode.GreaterThanOrEqual => ">=",
        VariableCompareMode.LessThan => "<",
        VariableCompareMode.LessThanOrEqual => "<=",
        VariableCompareMode.Contains => "contains",
        VariableCompareMode.StartsWith => "starts with",
        VariableCompareMode.EndsWith => "ends with",
        _ => "=="
    };

    private MouseMoveMode EffectiveMoveMode() => MouseMoveMode == MouseMoveMode.Legacy
        ? (MoveDurationMs > 0 ? MouseMoveMode.Smooth : MouseMoveMode.Teleport)
        : MouseMoveMode;

    private string MoveSuffix() => EffectiveMoveMode() == MouseMoveMode.Smooth
        ? $" (smooth {NumberDisplay(nameof(MoveDurationMs), MoveDurationMs)} ms)"
        : " (teleport)";

    private string CoordinateSuffix() => CoordinateMode switch
    {
        CoordinateMode.ActiveWindow => " [window]",
        CoordinateMode.RelativeToMouse => " [relative]",
        _ => string.Empty
    };

    private string RepeatDisplay() => !string.IsNullOrWhiteSpace(RepeatExpression)
        ? RepeatExpression
        : NumberDisplay(nameof(RepeatCount), RepeatCount);

    private string WaitDisplay() => !string.IsNullOrWhiteSpace(WaitExpression)
        ? WaitExpression
        : NumberDisplay(nameof(WaitMs), WaitMs);

    private string NumberDisplay(string propertyName, int fallback)
    {
        if (ValueExpressions is not null
            && ValueExpressions.TryGetValue(propertyName, out var expression)
            && !string.IsNullOrWhiteSpace(expression))
            return expression;
        return fallback.ToString();
    }

    private static string CoordinateDisplay(string expression, int fallback) => string.IsNullOrWhiteSpace(expression) ? fallback.ToString() : expression;

    private string ImageName()
    {
        if (!string.IsNullOrWhiteSpace(ImageFolder))
        {
            try
            {
                var folderName = Path.GetFileName(ImageFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return $"folder: {folderName} ({ImagePriority.Count} priority images)";
            }
            catch
            {
                return $"folder: {ImageFolder}";
            }
        }

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

public sealed class CommandRow : INotifyPropertyChanged
{
    public MacroCommand? Command { get; init; }
    public List<MacroCommand>? Owner { get; init; }
    public MacroCommand? ParentCommand { get; init; }
    public int Depth { get; init; }
    public string Label { get; init; } = string.Empty;
    public bool IsHeader { get; init; }
    public CommandBranch Branch { get; init; }
    public bool IsCollapsed { get; init; }

    public bool IsCollapsible => Command is { HasBody: true } && Command.Type != CommandType.RecordedActions;
    public bool IsNested => Depth > 0;
    public Visibility CollapseVisibility => IsCollapsible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NestingGuideVisibility => IsNested ? Visibility.Visible : Visibility.Collapsed;
    public string CollapseGlyph => IsCollapsible ? (IsCollapsed ? "▶" : "▼") : string.Empty;

    public string Display => IsHeader
        ? Label switch
        {
            "THEN" => "Then — run these when true",
            "ELSE" => "Else — run these when false",
            "DO" => "Repeat these commands",
            "GROUP" => "Commands in this group",
            "EMPTY" => "No commands yet — use Quick Add or + Add Command",
            _ => Label
        }
        : Command?.DisplayText() ?? string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshDisplay()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));

    public Thickness Indent => new(Math.Max(0, Depth) * 22, 0, 0, 0);
    public Thickness NestingGuideMargin => new(Math.Max(0, Depth - 1) * 22 + 8, 2, 0, 2);
}

public readonly record struct ScreenRegion(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct ImageMatch(int X, int Y, int Width, int Height)
{
    public string SourcePath { get; init; } = string.Empty;
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
}

internal static class ProjectPaths
{
    public static string? CurrentFolder { get; set; }

    public static string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        if (Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(CurrentFolder))
            return path;
        return Path.GetFullPath(Path.Combine(CurrentFolder, path));
    }

    public static string MakeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(CurrentFolder))
            return path;
        try
        {
            return Path.GetRelativePath(CurrentFolder, path);
        }
        catch
        {
            return path;
        }
    }
}
