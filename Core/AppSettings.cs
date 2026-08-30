using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MacroMaker;

public enum AppTheme
{
    Dark,
    Light
}

public sealed class CommandDefaultProfile
{
    public CommandType Type { get; set; }

    public int X { get; set; }
    public int Y { get; set; }
    public int EndX { get; set; }
    public int EndY { get; set; }
    public CoordinateMode CoordinateMode { get; set; } = CoordinateMode.Screen;
    public MouseMoveMode MouseMoveMode { get; set; } = MouseMoveMode.Smooth;
    public int MoveDurationMs { get; set; } = 50;
    public int ClickDelayMs { get; set; } = 100;
    public bool MoveBeforeClick { get; set; }
    public int ScrollAmount { get; set; } = -120;
    public int DragDurationMs { get; set; } = 500;
    public int HoldMs { get; set; } = 500;

    public string Key { get; set; } = "E";
    public string Text { get; set; } = "Text to type";

    public int WaitMs { get; set; } = 500;
    public int MinWaitMs { get; set; } = 250;
    public int MaxWaitMs { get; set; } = 750;

    public string RecordingStopHotkey { get; set; } = "F7";
    public bool RecordMouseMovement { get; set; } = true;
    public int RecordMouseSampleMs { get; set; } = 45;

    public int PollMs { get; set; } = 50;
    public int TimeoutMs { get; set; }

    public string ColorHex { get; set; } = "0x000000";
    public int ColorTolerance { get; set; }
    public ColorConditionSearchMode ColorSearchMode { get; set; } = ColorConditionSearchMode.Pixel;
    public int ColorSearchRadius { get; set; }
    public CompareMode CompareMode { get; set; } = CompareMode.Equals;

    public int ImageTolerance { get; set; } = 25;
    public bool ImageIncludeSubfolders { get; set; } = true;
    public string WindowTitle { get; set; } = string.Empty;
    public string ProgramPath { get; set; } = string.Empty;
    public string ProgramArguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public int SearchX { get; set; }
    public int SearchY { get; set; }
    public int SearchWidth { get; set; }
    public int SearchHeight { get; set; }
    public int ImageOffsetX { get; set; }
    public int ImageOffsetY { get; set; }

    public string VariableName { get; set; } = string.Empty;
    public string VariableValue { get; set; } = "0";
    public string VariableValue2 { get; set; } = "100";
    public VariableCompareMode VariableCompareMode { get; set; } = VariableCompareMode.Equals;
    public string StoreXVariable { get; set; } = string.Empty;
    public string StoreYVariable { get; set; } = string.Empty;
    public string StoreTextVariable { get; set; } = string.Empty;

    // Variable/formula overrides for normal command values.
    // Keys match MacroCommand property names, such as "X", "WaitMs", or "FilePath".
    public Dictionary<string, string> ValueExpressions { get; set; } = new();

    public string FilePath { get; set; } = string.Empty;
    public bool AppendFile { get; set; }
    public string PromptText { get; set; } = "Enter a value:";
    public List<string> PromptOptions { get; set; } = new() { "Option 1", "Option 2" };
    public FailureAction FailureAction { get; set; } = FailureAction.Continue;
    public int FailureRetryCount { get; set; } = 2;
    public int FailureRetryDelayMs { get; set; } = 250;

    public int RepeatCount { get; set; } = 3;
    public int LoopIntervalMs { get; set; } = 50;

    public CommandDefaultProfile DeepClone() => new()
    {
        Type = Type,
        X = X,
        Y = Y,
        EndX = EndX,
        EndY = EndY,
        CoordinateMode = CoordinateMode,
        MouseMoveMode = MouseMoveMode,
        MoveDurationMs = MoveDurationMs,
        ClickDelayMs = ClickDelayMs,
        MoveBeforeClick = MoveBeforeClick,
        ScrollAmount = ScrollAmount,
        DragDurationMs = DragDurationMs,
        HoldMs = HoldMs,
        Key = Key,
        Text = Text,
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
        ColorSearchMode = ColorSearchMode,
        ColorSearchRadius = ColorSearchRadius,
        CompareMode = CompareMode,
        ImageTolerance = ImageTolerance,
        ImageIncludeSubfolders = ImageIncludeSubfolders,
        WindowTitle = WindowTitle,
        ProgramPath = ProgramPath,
        ProgramArguments = ProgramArguments,
        WorkingDirectory = WorkingDirectory,
        SearchX = SearchX,
        SearchY = SearchY,
        SearchWidth = SearchWidth,
        SearchHeight = SearchHeight,
        ImageOffsetX = ImageOffsetX,
        ImageOffsetY = ImageOffsetY,
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
        RepeatCount = RepeatCount,
        LoopIntervalMs = LoopIntervalMs
    };
}

public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public int DefaultsRevision { get; set; } = 4;
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public DateTime LastSuccessfulUpdateCheckUtc { get; set; } = DateTime.MinValue;

    public string StopMacroHotkey { get; set; } = "F8";
    public string PauseMacroHotkey { get; set; } = "F7";
    public string RunStartHotkey { get; set; } = string.Empty;
    public string RunCurrentHotkey { get; set; } = string.Empty;
    public bool LockMouseMovementWhileRunning { get; set; } = false;
    public bool ShowRunStatusHud { get; set; } = true;
    public int PlaybackSpeedPercent { get; set; } = 100;
    public HudCorner RunHudCorner { get; set; } = HudCorner.TopLeft;
    public int RunHudOpacityPercent { get; set; } = 92;
    public bool ShowAdvancedCommands { get; set; } = true; // Legacy setting; all commands are always shown now.
    public bool AutoSaveProjectChanges { get; set; } = true;
    public bool CompactBlockLabels { get; set; }
    public List<string> RecentProjects { get; set; } = new();
    public List<CommandType> QuickAddCommands { get; set; } = new()
    {
        CommandType.MoveMouse,
        CommandType.Click,
        CommandType.Wait,
        CommandType.RecordedActions
    };

    public List<CommandDefaultProfile> CommandDefaults { get; set; } = CommandDefaultsFactory.CreateAll();

    // Kept only so V1.7 settings migrate cleanly.
    public MouseMoveMode DefaultMouseMoveMode { get; set; } = MouseMoveMode.Smooth;
    public int DefaultSmoothMoveMs { get; set; } = 50;
    public int DefaultColorTolerance { get; set; }
    public int DefaultPollMs { get; set; } = 50;

    public CommandDefaultProfile DefaultsFor(CommandType type)
    {
        var profile = CommandDefaults.FirstOrDefault(x => x.Type == type);
        if (profile is not null)
            return profile;

        profile = CommandDefaultsFactory.Create(type);
        CommandDefaults.Add(profile);
        return profile;
    }
}

internal static class CommandDefaultsFactory
{
    public static List<CommandDefaultProfile> CreateAll() =>
        CommandCatalog.AllOptions.Select(x => Create(x.Type)).ToList();

    public static CommandDefaultProfile Create(CommandType type)
    {
        return new CommandDefaultProfile { Type = type };
    }
}

internal static class AppSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MacroMaker",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            string json;
            AppSettings settings;
            try
            {
                json = File.ReadAllText(SettingsPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                           ?? throw new InvalidOperationException("Settings file was empty.");
            }
            catch when (File.Exists(SettingsPath + ".bak"))
            {
                json = File.ReadAllText(SettingsPath + ".bak");
                settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                           ?? new AppSettings();
            }

            var hadProfilesInFile = json.Contains("\"CommandDefaults\"", StringComparison.OrdinalIgnoreCase);
            var needsV141DefaultsMigration = !json.Contains("\"DefaultsRevision\"", StringComparison.OrdinalIgnoreCase);
            var needsZeroDefaultsMigration = !json.Contains("\"DefaultsRevision\"", StringComparison.OrdinalIgnoreCase)
                                            || settings.DefaultsRevision < 3;
            Repair(settings, !hadProfilesInFile, needsV141DefaultsMigration, needsZeroDefaultsMigration);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Repair(settings, false, false, false);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        AtomicFile.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions), keepBackup: true);
    }

    private static void Repair(AppSettings settings, bool migrateLegacy, bool migrateV141Defaults, bool migrateZeroDefaults)
    {
        settings.QuickAddCommands ??= new List<CommandType>();
        settings.QuickAddCommands = settings.QuickAddCommands
            .Where(CommandCatalog.CanQuickAdd)
            .Distinct()
            .ToList();

        settings.RecentProjects ??= new List<string>();
        settings.RecentProjects = settings.RecentProjects
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        settings.StopMacroHotkey = NormalizeHotkey(settings.StopMacroHotkey, "F8");
        settings.PauseMacroHotkey = NormalizeHotkey(settings.PauseMacroHotkey, "F7");
        settings.RunStartHotkey = NormalizeHotkey(settings.RunStartHotkey, string.Empty);
        settings.RunCurrentHotkey = NormalizeHotkey(settings.RunCurrentHotkey, string.Empty);
        settings.PlaybackSpeedPercent = Math.Clamp(settings.PlaybackSpeedPercent, 10, 400);
        settings.RunHudOpacityPercent = Math.Clamp(settings.RunHudOpacityPercent, 35, 100);

        settings.CommandDefaults ??= new List<CommandDefaultProfile>();

        foreach (var option in CommandCatalog.AllOptions)
        {
            if (settings.CommandDefaults.All(x => x.Type != option.Type))
                settings.CommandDefaults.Add(CommandDefaultsFactory.Create(option.Type));
        }

        settings.CommandDefaults = settings.CommandDefaults
            .Where(x => CommandCatalog.CanQuickAdd(x.Type))
            .GroupBy(x => x.Type)
            .Select(x => x.First())
            .ToList();

        // Migrate the old V1.7 global defaults the first time profiles are created.
        if (migrateLegacy)
        {
            foreach (var p in settings.CommandDefaults)
            {
                if (CommandCatalog.UsesMouseMovement(p.Type))
                {
                    p.MouseMoveMode = settings.DefaultMouseMoveMode == MouseMoveMode.Legacy
                        ? MouseMoveMode.Smooth
                        : settings.DefaultMouseMoveMode;
                    p.MoveDurationMs = settings.DefaultSmoothMoveMs;
                }
                if (CommandCatalog.UsesColor(p.Type))
                    p.ColorTolerance = settings.DefaultColorTolerance;
                if (CommandCatalog.UsesPolling(p.Type))
                    p.PollMs = settings.DefaultPollMs;
            }
        }

        // Migrate older test-build defaults from F9/250ms to the current F7/50ms defaults.
        // Only migrate old settings files that do not yet carry a defaults revision,
        // and only values that still match the old shipped defaults.
        if (migrateV141Defaults)
        {
            if (settings.PauseMacroHotkey.Equals("F9", StringComparison.OrdinalIgnoreCase))
                settings.PauseMacroHotkey = "F7";
            if (settings.DefaultSmoothMoveMs == 250)
                settings.DefaultSmoothMoveMs = 50;

            foreach (var p in settings.CommandDefaults.Where(p => CommandCatalog.UsesMouseMovement(p.Type)))
            {
                if (p.MoveDurationMs == 250)
                    p.MoveDurationMs = 50;
            }
        }

        if (migrateZeroDefaults)
        {
            if (settings.DefaultColorTolerance == 8)
                settings.DefaultColorTolerance = 0;

            foreach (var p in settings.CommandDefaults)
            {
                if (p.X == 960) p.X = 0;
                if (p.Y == 300) p.Y = 0;
                if (p.EndX == 1100) p.EndX = 0;
                if (p.EndY == 500) p.EndY = 0;
                if (string.Equals(p.ColorHex, "0xFFFFFF", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.ColorHex, "#FFFFFF", StringComparison.OrdinalIgnoreCase))
                    p.ColorHex = "0x000000";
                if (p.ColorTolerance == 8)
                    p.ColorTolerance = 0;
            }
        }

        // Rev 4 fixes the shipped default for "Repeat Until Color Matches".
        // Existing command instances are left alone; this only repairs the default profile.
        if (settings.DefaultsRevision < 4)
        {
            var untilColor = settings.CommandDefaults.FirstOrDefault(p => p.Type == CommandType.LoopUntilColor);
            if (untilColor is not null && untilColor.CompareMode == CompareMode.NotEquals)
                untilColor.CompareMode = CompareMode.Equals;
        }

        settings.DefaultsRevision = 4;

        foreach (var p in settings.CommandDefaults)
            RepairProfile(p);
    }

    private static string NormalizeHotkey(string? value, string fallback)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return fallback;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? keyPart = null;
        foreach (var part in parts)
        {
            if (part.Equals("CTRL", StringComparison.OrdinalIgnoreCase)
                || part.Equals("CONTROL", StringComparison.OrdinalIgnoreCase)
                || part.Equals("SHIFT", StringComparison.OrdinalIgnoreCase)
                || part.Equals("ALT", StringComparison.OrdinalIgnoreCase)
                || part.Equals("WIN", StringComparison.OrdinalIgnoreCase)
                || part.Equals("LWIN", StringComparison.OrdinalIgnoreCase)
                || part.Equals("RWIN", StringComparison.OrdinalIgnoreCase))
                continue;

            if (keyPart is not null)
                return fallback;
            keyPart = part;
        }

        return keyPart is not null && InputController.TryGetVirtualKey(keyPart, out _)
            ? text
            : fallback;
    }

    private static void RepairProfile(CommandDefaultProfile p)
    {
        if (p.MouseMoveMode == MouseMoveMode.Legacy)
            p.MouseMoveMode = MouseMoveMode.Smooth;
        p.MoveDurationMs = Math.Clamp(p.MoveDurationMs, 1, 60_000);
        p.ClickDelayMs = Math.Clamp(p.ClickDelayMs, 20, 1000);
        p.DragDurationMs = Math.Clamp(p.DragDurationMs, 1, 60_000);
        p.HoldMs = Math.Clamp(p.HoldMs, 1, 86_400_000);
        p.WaitMs = Math.Clamp(p.WaitMs, 0, 86_400_000);
        p.MinWaitMs = Math.Clamp(p.MinWaitMs, 0, 86_400_000);
        p.MaxWaitMs = Math.Clamp(p.MaxWaitMs, 0, 86_400_000);
        if (p.MaxWaitMs < p.MinWaitMs)
            p.MaxWaitMs = p.MinWaitMs;
        p.RecordMouseSampleMs = Math.Clamp(p.RecordMouseSampleMs, 15, 500);
        p.PollMs = Math.Clamp(p.PollMs, 10, 5000);
        p.TimeoutMs = Math.Clamp(p.TimeoutMs, 0, 86_400_000);
        p.ColorTolerance = Math.Clamp(p.ColorTolerance, 0, 255);
        p.ColorSearchRadius = Math.Clamp(p.ColorSearchRadius, 0, 5000);
        p.ImageTolerance = Math.Clamp(p.ImageTolerance, 0, 255);
        p.RepeatCount = Math.Clamp(p.RepeatCount, 0, 1_000_000);
        p.LoopIntervalMs = Math.Clamp(p.LoopIntervalMs, 0, 60_000);
        p.FailureRetryCount = Math.Clamp(p.FailureRetryCount, 0, 100);
        p.FailureRetryDelayMs = Math.Clamp(p.FailureRetryDelayMs, 0, 60_000);
        if (string.IsNullOrWhiteSpace(p.ColorHex)) p.ColorHex = "0x000000";
        if (string.IsNullOrWhiteSpace(p.Key)) p.Key = "E";
        if (string.IsNullOrWhiteSpace(p.RecordingStopHotkey)) p.RecordingStopHotkey = "F7";
        p.PromptOptions ??= new List<string>();
        p.PromptOptions = p.PromptOptions.Where(option => !string.IsNullOrWhiteSpace(option)).ToList();
        if (p.Type == CommandType.PromptSelect && p.PromptOptions.Count == 0)
            p.PromptOptions.AddRange(new[] { "Option 1", "Option 2" });
    }
}

internal static class CommandCatalog
{
    public sealed record Option(string Category, string Label, CommandType Type);

    public static readonly (string Name, Option[] Commands)[] Categories =
    {
        ("Mouse", new[]
        {
            new Option("Mouse", "Move Mouse", CommandType.MoveMouse),
            new Option("Mouse", "Click", CommandType.Click),
            new Option("Mouse", "Double Click", CommandType.DoubleClick),
            new Option("Mouse", "Right Click", CommandType.RightClick),
            new Option("Mouse", "Scroll", CommandType.Scroll),
            new Option("Mouse", "Drag Mouse", CommandType.DragMouse)
        }),
        ("Keyboard", new[]
        {
            new Option("Keyboard", "Press Key / Combo", CommandType.PressKey),
            new Option("Keyboard", "Type Text", CommandType.TypeText),
            new Option("Keyboard", "Hold Key", CommandType.HoldKey),
            new Option("Keyboard", "Repeat Key", CommandType.RepeatKey),
            new Option("Keyboard", "Wait for Key Press", CommandType.WaitUntilKeyPressed),
            new Option("Keyboard", "Wait for Key Release", CommandType.WaitUntilKeyReleased),
            new Option("Keyboard", "If Key Is Pressed", CommandType.IfKeyPressed),
            new Option("Keyboard", "Repeat While Key Is Held", CommandType.LoopWhileKeyPressed)
        }),
        ("Wait / Timing", new[]
        {
            new Option("Wait / Timing", "Wait", CommandType.Wait),
            new Option("Wait / Timing", "Random Wait", CommandType.RandomWait)
        }),
        ("Recording", new[]
        {
            new Option("Recording", "Record Actions", CommandType.RecordedActions)
        }),
        ("Screen Color", new[]
        {
            new Option("Screen Color", "If Color Matches", CommandType.IfColor),
            new Option("Screen Color", "Wait for Color", CommandType.WaitUntilColor),
            new Option("Screen Color", "Repeat While Color Matches", CommandType.LoopWhileColor),
            new Option("Screen Color", "Repeat Until Color Matches", CommandType.LoopUntilColor),
            new Option("Screen Color", "Find + Click Color", CommandType.ClickColor),
            new Option("Screen Color", "Find Color + Save Location", CommandType.FindColorToVariables),
            new Option("Screen Color", "Read Color + Save It", CommandType.SampleColorToVariable)
        }),
        ("Images", new[]
        {
            new Option("Images", "If Image Is Found", CommandType.IfImage),
            new Option("Images", "Wait for Image", CommandType.WaitUntilImage),
            new Option("Images", "Wait for Image to Disappear", CommandType.WaitUntilImageGone),
            new Option("Images", "Find + Click Image", CommandType.ClickImage),
            new Option("Images", "Find + Double Click Image", CommandType.DoubleClickImage),
            new Option("Images", "Move Mouse to Image", CommandType.MoveToImage),
            new Option("Images", "Repeat Until Image Is Found", CommandType.LoopUntilImage),
            new Option("Images", "Repeat While Image Is Visible", CommandType.LoopWhileImage),
            new Option("Images", "Find Image + Save Location", CommandType.FindImageToVariables)
        }),
        ("Windows & Apps", new[]
        {
            new Option("Windows & Apps", "If Window Is Open", CommandType.IfWindow),
            new Option("Windows & Apps", "Focus Window", CommandType.FocusWindow),
            new Option("Windows & Apps", "Wait for Window", CommandType.WaitForWindow),
            new Option("Windows & Apps", "Wait for Window to Close", CommandType.WaitForWindowGone),
            new Option("Windows & Apps", "Minimize Window", CommandType.MinimizeWindow),
            new Option("Windows & Apps", "Maximize Window", CommandType.MaximizeWindow),
            new Option("Windows & Apps", "Restore Window", CommandType.RestoreWindow),
            new Option("Windows & Apps", "Close Window", CommandType.CloseWindow),
            new Option("Windows & Apps", "Open Program / File / URL", CommandType.RunProgram)
        }),
        ("Variables", new[]
        {
            new Option("Variables", "Set Variable", CommandType.SetVariable),
            new Option("Variables", "Change Variable", CommandType.AddVariable),
            new Option("Variables", "Save Random Number", CommandType.RandomNumber),
            new Option("Variables", "If Saved Value Matches", CommandType.IfVariable),
            new Option("Variables", "Wait for Saved Value", CommandType.WaitUntilVariable),
            new Option("Variables", "Repeat While Saved Value Matches", CommandType.LoopWhileVariable),
            new Option("Variables", "Repeat Until Saved Value Matches", CommandType.LoopUntilVariable)
        }),
        ("Clipboard / Files / Questions", new[]
        {
            new Option("Clipboard / Files / Questions", "Set Clipboard", CommandType.SetClipboard),
            new Option("Clipboard / Files / Questions", "Save Clipboard as Variable", CommandType.ClipboardToVariable),
            new Option("Clipboard / Files / Questions", "Read Text File + Save It", CommandType.ReadTextFile),
            new Option("Clipboard / Files / Questions", "Write Text File", CommandType.WriteTextFile),
            new Option("Clipboard / Files / Questions", "Ask User for Text", CommandType.PromptText),
            new Option("Clipboard / Files / Questions", "Ask User from Dropdown", CommandType.PromptSelect),
            new Option("Clipboard / Files / Questions", "Ask User Yes / No", CommandType.PromptYesNo)
        }),
        ("Logic / Loops", new[]
        {
            new Option("Logic / Loops", "Group Commands", CommandType.Group),
            new Option("Logic / Loops", "Run Another Tab", CommandType.RunSequence),
            new Option("Logic / Loops", "Restart Current Tab", CommandType.RestartCurrentTab),
            new Option("Logic / Loops", "Repeat X Times", CommandType.LoopTimes),
            new Option("Logic / Loops", "Repeat Forever", CommandType.LoopForever),
            new Option("Logic / Loops", "Exit Current Repeat", CommandType.Break),
            new Option("Logic / Loops", "Stop Macro", CommandType.StopMacro)
        }),
        ("Advanced Input", new[]
        {
            new Option("Advanced Input", "Hold Left Mouse Down", CommandType.LeftMouseDown),
            new Option("Advanced Input", "Release Left Mouse", CommandType.LeftMouseUp),
            new Option("Advanced Input", "Hold Right Mouse Down", CommandType.RightMouseDown),
            new Option("Advanced Input", "Release Right Mouse", CommandType.RightMouseUp),
            new Option("Advanced Input", "Hold Key Down", CommandType.KeyDown),
            new Option("Advanced Input", "Release Key", CommandType.KeyUp)
        })
    };

    public static IReadOnlyList<Option> AllOptions => Categories.SelectMany(x => x.Commands).ToArray();
    public static bool CanQuickAdd(CommandType type) => AllOptions.Any(x => x.Type == type);
    public static string QuickLabel(CommandType type) => AllOptions.FirstOrDefault(x => x.Type == type)?.Label ?? type.ToString();
    public static string CategoryFor(CommandType type) => AllOptions.FirstOrDefault(x => x.Type == type)?.Category ?? "Other";

    public static bool UsesMouseMovement(CommandType type) => type is CommandType.MoveMouse
        or CommandType.Click or CommandType.DoubleClick or CommandType.RightClick or CommandType.Scroll
        or CommandType.DragMouse or CommandType.ClickImage or CommandType.DoubleClickImage or CommandType.MoveToImage
        or CommandType.ClickColor;

    public static bool UsesCoordinates(CommandType type) => UsesMouseMovement(type)
        || type is CommandType.IfColor or CommandType.WaitUntilColor or CommandType.LoopWhileColor
        or CommandType.LoopUntilColor or CommandType.ClickColor or CommandType.FindColorToVariables or CommandType.SampleColorToVariable
        or CommandType.IfImage or CommandType.WaitUntilImage or CommandType.WaitUntilImageGone
        or CommandType.ClickImage or CommandType.DoubleClickImage or CommandType.MoveToImage
        or CommandType.LoopUntilImage or CommandType.LoopWhileImage or CommandType.FindImageToVariables;

    public static bool UsesColor(CommandType type) => type is CommandType.IfColor or CommandType.WaitUntilColor
        or CommandType.LoopWhileColor or CommandType.LoopUntilColor or CommandType.ClickColor or CommandType.FindColorToVariables;

    public static bool UsesPolling(CommandType type) => type is CommandType.WaitUntilColor or CommandType.LoopWhileColor
        or CommandType.LoopUntilColor or CommandType.WaitUntilImage or CommandType.WaitUntilImageGone or CommandType.LoopUntilImage
        or CommandType.LoopWhileImage or CommandType.WaitUntilKeyPressed or CommandType.WaitUntilKeyReleased or CommandType.LoopWhileKeyPressed or CommandType.WaitForWindow or CommandType.WaitForWindowGone
        or CommandType.WaitUntilVariable;

    public static bool CanFail(CommandType type) => type is CommandType.ClickColor or CommandType.FindColorToVariables
        or CommandType.ClickImage or CommandType.DoubleClickImage or CommandType.MoveToImage or CommandType.FindImageToVariables
        or CommandType.FocusWindow or CommandType.MinimizeWindow or CommandType.MaximizeWindow or CommandType.RestoreWindow
        or CommandType.CloseWindow or CommandType.RunProgram or CommandType.WaitUntilKeyPressed or CommandType.WaitUntilKeyReleased or CommandType.WaitUntilColor or CommandType.WaitUntilImage
        or CommandType.WaitUntilImageGone or CommandType.WaitForWindow or CommandType.WaitForWindowGone or CommandType.WaitUntilVariable
        or CommandType.ReadTextFile or CommandType.WriteTextFile;
}

internal static class ThemeManager
{
    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public static void Apply(AppTheme theme)
    {
        CurrentTheme = theme;
        if (Application.Current is null)
            return;

        if (theme == AppTheme.Light)
        {
            Set("BgBrush", "#F4F7FB");
            Set("TopBarBrush", "#FFFFFF");
            Set("PanelBrush", "#FFFFFF");
            Set("Panel2Brush", "#F2F5FA");
            Set("Panel3Brush", "#E8EEF7");
            Set("BorderBrushDark", "#C8D2E0");
            Set("BorderBrushSoft", "#DDE4EE");
            Set("TextBrush", "#172033");
            Set("MutedTextBrush", "#64748B");
            Set("AccentBrush", "#5275E8");
            Set("AccentHoverBrush", "#4568D9");
            Set("AccentPressedBrush", "#3A5CC9");
            Set("DangerBrush", "#D24F60");
            Set("SuccessBrush", "#2E9D73");
            Set("ButtonHoverBrush", "#E8EEF8");
            Set("ButtonHoverBorderBrush", "#B8C7DB");
            Set("ButtonPressedBrush", "#DDE6F3");
            Set("QuickAddBrush", "#EEF3FB");
            Set("QuickAddBorderBrush", "#CCD8E8");
            Set("QuickAddHoverBrush", "#E1EAF7");
            Set("QuickAddPressedBrush", "#D7E2F1");
            Set("InputBrush", "#FFFFFF");
            Set("PopupBrush", "#FFFFFF");
            Set("PopupBorderBrush", "#C7D2E0");
            Set("ItemBrush", "#FAFCFF");
            Set("ItemSelectedBrush", "#E5EDFF");
            Set("ItemHoverBrush", "#EEF3FA");
            Set("ItemHoverBorderBrush", "#B8C8DF");
            Set("MenuBrush", "#FFFFFF");
            Set("ScrollThumbBrush", "#A9B8CB");
            Set("ScrollThumbHoverBrush", "#8195AE");
            Set("NestedItemBrush", "#F4F7FB");
        }
        else
        {
            Set("BgBrush", "#0A0F17");
            Set("TopBarBrush", "#0E141E");
            Set("PanelBrush", "#111925");
            Set("Panel2Brush", "#172231");
            Set("Panel3Brush", "#1D2A3B");
            Set("BorderBrushDark", "#27364A");
            Set("BorderBrushSoft", "#1D2B3D");
            Set("TextBrush", "#F4F7FB");
            Set("MutedTextBrush", "#91A0B5");
            Set("AccentBrush", "#6C8CFF");
            Set("AccentHoverBrush", "#7C99FF");
            Set("AccentPressedBrush", "#5877E8");
            Set("DangerBrush", "#E56B78");
            Set("SuccessBrush", "#58C99A");
            Set("ButtonHoverBrush", "#223249");
            Set("ButtonHoverBorderBrush", "#3A5371");
            Set("ButtonPressedBrush", "#192638");
            Set("QuickAddBrush", "#142033");
            Set("QuickAddBorderBrush", "#263B56");
            Set("QuickAddHoverBrush", "#1B2B43");
            Set("QuickAddPressedBrush", "#15243A");
            Set("InputBrush", "#0C131D");
            Set("PopupBrush", "#101925");
            Set("PopupBorderBrush", "#30435C");
            Set("ItemBrush", "#0F1722");
            Set("ItemSelectedBrush", "#172743");
            Set("ItemHoverBrush", "#152131");
            Set("ItemHoverBorderBrush", "#334A67");
            Set("MenuBrush", "#111A27");
            Set("ScrollThumbBrush", "#465A73");
            Set("ScrollThumbHoverBrush", "#607895");
            Set("NestedItemBrush", "#121D2A");
        }

        foreach (Window window in Application.Current.Windows)
            WindowTheme.Apply(window, theme);
    }

    private static void Set(string key, string hex)
    {
        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }
}

internal static class WindowTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    public static void Attach(Window window)
    {
        // WPF is PerMonitorV2 DPI-aware through app.manifest. Layout rounding keeps
        // borders/text controls stable at 125%, 150% and 200% Windows scaling.
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
        window.SourceInitialized += (_, _) => Apply(window, ThemeManager.CurrentTheme);
    }

    public static void Apply(Window window, AppTheme theme)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;

            var enabled = theme == AppTheme.Dark ? 1 : 0;
            var size = sizeof(int);

            // Attribute 20 is current on Windows 10 20H1+ and Windows 11.
            // Attribute 19 is the fallback used by older Windows 10 builds.
            var result = NativeMethods.DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, size);
            if (result != 0)
                NativeMethods.DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, size);
        }
        catch
        {
            // Title-bar theming is cosmetic and should never break the app.
        }
    }
}
