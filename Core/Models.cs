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
    PromptSelect,
    PromptYesNo,

    Group,
    RunSequence,
    LoopTimes,
    LoopForever,
    Break,
    Return,
    RestartCurrentTab,
    StopMacro
}

public enum CompareMode
{
    Equals,
    NotEquals
}

public enum ColorConditionSearchMode
{
    Pixel,
    SearchArea,
    FullScreen
}

public enum LocationGroupMode
{
    Any,
    All
}

public enum SequenceRunMode
{
    WaitUntilFinished,
    RunSimultaneously
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

public sealed class ColorMatchOption
{
    public string ColorHex { get; set; } = "0x000000";
    public int Tolerance { get; set; }
    public string ToleranceExpression { get; set; } = string.Empty;

    public ColorMatchOption DeepClone() => new()
    {
        ColorHex = ColorHex,
        Tolerance = Tolerance,
        ToleranceExpression = ToleranceExpression
    };
}

public sealed class ColorSearchTarget
{
    public int X { get; set; }
    public int Y { get; set; }
    public string XExpression { get; set; } = string.Empty;
    public string YExpression { get; set; } = string.Empty;
    public int SearchX { get; set; }
    public int SearchY { get; set; }
    public int SearchWidth { get; set; } = 1;
    public int SearchHeight { get; set; } = 1;
    public string SearchXExpression { get; set; } = string.Empty;
    public string SearchYExpression { get; set; } = string.Empty;
    public string SearchWidthExpression { get; set; } = string.Empty;
    public string SearchHeightExpression { get; set; } = string.Empty;

    public ColorSearchTarget DeepClone() => new()
    {
        X = X,
        Y = Y,
        XExpression = XExpression,
        YExpression = YExpression,
        SearchX = SearchX,
        SearchY = SearchY,
        SearchWidth = SearchWidth,
        SearchHeight = SearchHeight,
        SearchXExpression = SearchXExpression,
        SearchYExpression = SearchYExpression,
        SearchWidthExpression = SearchWidthExpression,
        SearchHeightExpression = SearchHeightExpression
    };
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
    public bool Enabled { get; set; } = true;
    public List<MacroCommand> Commands { get; set; } = new();

    public MacroSequence DeepClone(string? newName = null) => new()
    {
        Name = newName ?? Name,
        Enabled = Enabled,
        Commands = (Commands ?? new List<MacroCommand>()).Select(command => command.DeepClone()).ToList()
    };
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
    public SequenceRunMode RunSequenceMode { get; set; } = SequenceRunMode.WaitUntilFinished;
    public bool IsElseIf { get; set; }

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
    // null means an older macro that used the legacy click-at-X/Y behavior.
    // New click commands explicitly default this to false so they click wherever
    // the mouse already is unless the user enables Move to location first.
    public bool? MoveBeforeClick { get; set; }
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
    public int CooldownMs { get; set; }

    public string ColorHex { get; set; } = "0x000000";
    public int ColorTolerance { get; set; }
    // Explicit search mode for color conditions. Null is only kept for compatibility
    // with older projects that used ColorSearchRadius around X/Y.
    public ColorConditionSearchMode? ColorSearchMode { get; set; }
    public int ColorSearchRadius { get; set; }
    // Extra colors are OR alternatives to ColorHex/ColorTolerance. Keeping the
    // original fields as the first target preserves compatibility with old macros.
    public List<ColorMatchOption> ColorAlternatives { get; set; } = new();
    public List<ColorSearchTarget> ColorSearchTargets { get; set; } = new();
    public LocationGroupMode ColorLocationGroupMode { get; set; } = LocationGroupMode.Any;
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
    // Delay between Loop Forever iterations. 0 means no added delay.
    public int LoopIntervalMs { get; set; }

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
    public List<string> PromptOptions { get; set; } = new() { "Option 1", "Option 2" };

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
    public bool IsLoop => Type is CommandType.LoopWhileKeyPressed
        or CommandType.LoopWhileColor
        or CommandType.LoopUntilColor
        or CommandType.LoopUntilImage
        or CommandType.LoopWhileImage
        or CommandType.LoopWhileVariable
        or CommandType.LoopUntilVariable
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
            RunSequenceMode = RunSequenceMode,
            IsElseIf = IsElseIf,
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
            MoveBeforeClick = MoveBeforeClick,
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
            CooldownMs = CooldownMs,
            ColorHex = ColorHex,
            ColorTolerance = ColorTolerance,
            ColorSearchMode = ColorSearchMode,
            ColorSearchRadius = ColorSearchRadius,
            ColorAlternatives = (ColorAlternatives ?? new List<ColorMatchOption>()).Select(option => option.DeepClone()).ToList(),
            ColorSearchTargets = (ColorSearchTargets ?? new List<ColorSearchTarget>()).Select(target => target.DeepClone()).ToList(),
            ColorLocationGroupMode = ColorLocationGroupMode,
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
            LoopIntervalMs = LoopIntervalMs,
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
            PromptOptions = (PromptOptions ?? new List<string>()).ToList(),
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
            CommandType.Click => ClickDisplay("Click", x, y),
            CommandType.DoubleClick => ClickDisplay("Double click", x, y),
            CommandType.RightClick => ClickDisplay("Right click", x, y),
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
            CommandType.IfColor => $"If color {ColorConditionSearchDisplay(x, y)}{ColorLocationSuffix()} {FriendlyColorCompare()} {ColorHex} ±{NumberDisplay(nameof(ColorTolerance), ColorTolerance)}{ColorOrSuffix()}",
            CommandType.WaitUntilColor => $"Wait until color {ColorConditionSearchDisplay(x, y)}{ColorLocationSuffix()} {FriendlyColorCompare()} {ColorHex} ±{NumberDisplay(nameof(ColorTolerance), ColorTolerance)}{ColorOrSuffix()}",
            CommandType.LoopWhileColor => $"Repeat while color {ColorConditionSearchDisplay(x, y)}{ColorLocationSuffix()} {FriendlyColorCompare()} {ColorHex} ±{NumberDisplay(nameof(ColorTolerance), ColorTolerance)}{ColorOrSuffix()}",
            CommandType.LoopUntilColor => $"Repeat until color {ColorConditionSearchDisplay(x, y)}{ColorLocationSuffix()} {FriendlyColorCompare()} {ColorHex} ±{NumberDisplay(nameof(ColorTolerance), ColorTolerance)}{ColorOrSuffix()}",
            CommandType.ClickColor => $"Find + click color {ColorHex} ±{NumberDisplay(nameof(ColorTolerance), ColorTolerance)}{ColorOrSuffix()}",
            CommandType.FindColorToVariables => $"Find color {ColorHex}{ColorOrSuffix()} → {StoreXVariable}, {StoreYVariable}",
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
            CommandType.SetVariable => $"Set {VariableName} = {TrimForDisplay(VariableValue, 40)}",
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
            CommandType.PromptSelect => $"Ask user to choose → {VariableName}",
            CommandType.PromptYesNo => $"Ask Yes/No → {VariableName}",
            CommandType.Group => string.IsNullOrWhiteSpace(CustomName) ? "Command group" : "Group",
            CommandType.RunSequence => RunSequenceMode == SequenceRunMode.RunSimultaneously
                ? $"Run tab \"{TargetSequence}\" simultaneously"
                : $"Run tab \"{TargetSequence}\"",
            CommandType.LoopTimes => $"Repeat {RepeatDisplay()} times",
            CommandType.LoopForever => LoopIntervalMs > 0 || (ValueExpressions?.ContainsKey(nameof(LoopIntervalMs)) ?? false)
                ? $"Repeat forever every {NumberDisplay(nameof(LoopIntervalMs), LoopIntervalMs)} ms"
                : "Repeat forever",
            CommandType.Break => "Exit current repeat",
            CommandType.Return => "Return to previous tab",
            CommandType.RestartCurrentTab => "Restart current tab",
            CommandType.StopMacro => "Stop macro",
            _ => Type.ToString()
        };
    }

    private string CompareSymbol() => CompareMode == CompareMode.Equals ? "==" : "!=";

    private string FriendlyColorCompare() => CompareMode == CompareMode.Equals ? "matches" : "does not match";

    private string ClickDisplay(string action, string x, string y)
    {
        // Old commands (null) keep their legacy behavior and display. New commands
        // with false are intentionally location-free.
        if (MoveBeforeClick == false)
            return action;
        return $"{action} {x}, {y}" + MoveSuffix() + CoordinateSuffix();
    }

    private string ColorOrSuffix()
    {
        var count = ColorAlternatives?.Count ?? 0;
        return count <= 0 ? string.Empty : $" + {count} OR";
    }

    private string ColorLocationSuffix()
    {
        var count = ColorSearchTargets?.Count ?? 0;
        if (count <= 0 || (ColorSearchMode ?? ColorConditionSearchMode.Pixel) == ColorConditionSearchMode.FullScreen)
            return string.Empty;
        var join = ColorLocationGroupMode == LocationGroupMode.All ? "AND" : "OR";
        return $" + {count} {join} location{(count == 1 ? string.Empty : "s")}";
    }

    private string ColorConditionSearchDisplay(string x, string y)
    {
        if (ColorSearchMode is null && ColorSearchRadius > 0)
            return $"near {x}, {y} within {NumberDisplay(nameof(ColorSearchRadius), ColorSearchRadius)}px{CoordinateSuffix()}";

        return (ColorSearchMode ?? ColorConditionSearchMode.Pixel) switch
        {
            ColorConditionSearchMode.FullScreen => "on full screen",
            ColorConditionSearchMode.SearchArea when SearchWidth > 0 && SearchHeight > 0
                => $"in area {SearchX}, {SearchY}, {SearchWidth}×{SearchHeight}{CoordinateSuffix()}",
            ColorConditionSearchMode.SearchArea => "in a search area",
            _ => $"at {x}, {y}{CoordinateSuffix()}"
        };
    }

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
    public string DisplayOverride { get; init; } = string.Empty;
    public bool IsHeader { get; init; }
    public bool CompactLabels { get; init; }
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
            "THEN" => CompactLabels ? "Then" : "Then — run these when true",
            "ELSE" => CompactLabels ? "Else" : "Else — run these when false",
            "DO" => CompactLabels ? string.Empty : "Repeat these commands",
            "GROUP" => CompactLabels ? "Group" : "Commands in this group",
            "END LOOP" => "End Loop",
            "EMPTY" => "No commands yet — use Quick Add or + Add Command",
            _ => Label
        }
        : !string.IsNullOrWhiteSpace(DisplayOverride)
            ? DisplayOverride
            : Command?.DisplayText() ?? string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshDisplay()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));

    private const int IndentStep = 30;
    public Thickness Indent => new(Math.Max(0, Depth) * IndentStep, 0, 0, 0);
    public Thickness NestingGuideMargin => new(Math.Max(0, Depth) * IndentStep + 10, 2, 0, 2);
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
