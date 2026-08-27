using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
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

    public int X { get; set; } = 960;
    public int Y { get; set; } = 300;
    public int EndX { get; set; } = 1100;
    public int EndY { get; set; } = 500;
    public MouseMoveMode MouseMoveMode { get; set; } = MouseMoveMode.Smooth;
    public int MoveDurationMs { get; set; } = 250;
    public int ClickDelayMs { get; set; } = 100;
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

    public string ColorHex { get; set; } = "0xFFFFFF";
    public int ColorTolerance { get; set; } = 8;
    public CompareMode CompareMode { get; set; } = CompareMode.Equals;

    public int ImageTolerance { get; set; } = 25;
    public string WindowTitle { get; set; } = string.Empty;
    public string ProgramPath { get; set; } = string.Empty;
    public int SearchX { get; set; }
    public int SearchY { get; set; }
    public int SearchWidth { get; set; }
    public int SearchHeight { get; set; }
    public int ImageOffsetX { get; set; }
    public int ImageOffsetY { get; set; }

    public int RepeatCount { get; set; } = 3;

    public CommandDefaultProfile DeepClone() => new()
    {
        Type = Type,
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
        CompareMode = CompareMode,
        ImageTolerance = ImageTolerance,
        WindowTitle = WindowTitle,
        ProgramPath = ProgramPath,
        SearchX = SearchX,
        SearchY = SearchY,
        SearchWidth = SearchWidth,
        SearchHeight = SearchHeight,
        ImageOffsetX = ImageOffsetX,
        ImageOffsetY = ImageOffsetY,
        RepeatCount = RepeatCount
    };
}

public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;
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
    public int DefaultSmoothMoveMs { get; set; } = 250;
    public int DefaultColorTolerance { get; set; } = 8;
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
        var p = new CommandDefaultProfile { Type = type };
        if (type == CommandType.LoopUntilColor)
            p.CompareMode = CompareMode.NotEquals;
        return p;
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

            var json = File.ReadAllText(SettingsPath);
            var hadProfilesInFile = json.Contains("\"CommandDefaults\"", StringComparison.OrdinalIgnoreCase);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                           ?? new AppSettings();
            Repair(settings, !hadProfilesInFile);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Repair(settings, false);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static void Repair(AppSettings settings, bool migrateLegacy)
    {
        settings.QuickAddCommands ??= new List<CommandType>();
        settings.QuickAddCommands = settings.QuickAddCommands
            .Where(CommandCatalog.CanQuickAdd)
            .Distinct()
            .ToList();

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

        foreach (var p in settings.CommandDefaults)
            RepairProfile(p);
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
        p.ImageTolerance = Math.Clamp(p.ImageTolerance, 0, 255);
        p.RepeatCount = Math.Clamp(p.RepeatCount, 0, 1_000_000);
        if (string.IsNullOrWhiteSpace(p.ColorHex)) p.ColorHex = "0xFFFFFF";
        if (string.IsNullOrWhiteSpace(p.Key)) p.Key = "E";
        if (string.IsNullOrWhiteSpace(p.RecordingStopHotkey)) p.RecordingStopHotkey = "F7";
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
            new Option("Keyboard", "Wait Until Key Pressed", CommandType.WaitUntilKeyPressed)
        }),
        ("Advanced Input", new[]
        {
            new Option("Advanced Input", "Left Mouse Down", CommandType.LeftMouseDown),
            new Option("Advanced Input", "Left Mouse Up", CommandType.LeftMouseUp),
            new Option("Advanced Input", "Right Mouse Down", CommandType.RightMouseDown),
            new Option("Advanced Input", "Right Mouse Up", CommandType.RightMouseUp),
            new Option("Advanced Input", "Key Down", CommandType.KeyDown),
            new Option("Advanced Input", "Key Up", CommandType.KeyUp)
        }),
        ("Timing", new[]
        {
            new Option("Timing", "Wait", CommandType.Wait),
            new Option("Timing", "Random Wait", CommandType.RandomWait)
        }),
        ("Recording", new[]
        {
            new Option("Recording", "Record Actions", CommandType.RecordedActions)
        }),
        ("Color / Pixel", new[]
        {
            new Option("Color / Pixel", "IF Color", CommandType.IfColor),
            new Option("Color / Pixel", "Wait Until Color at Location", CommandType.WaitUntilColor),
            new Option("Color / Pixel", "Loop While Color", CommandType.LoopWhileColor),
            new Option("Color / Pixel", "Loop Until Color", CommandType.LoopUntilColor),
            new Option("Color / Pixel", "Find + Click Color", CommandType.ClickColor)
        }),
        ("Image Detection", new[]
        {
            new Option("Image Detection", "IF Image Found", CommandType.IfImage),
            new Option("Image Detection", "Wait Until Image", CommandType.WaitUntilImage),
            new Option("Image Detection", "Wait Until Image Gone", CommandType.WaitUntilImageGone),
            new Option("Image Detection", "Find + Click Image", CommandType.ClickImage),
            new Option("Image Detection", "Find + Double Click Image", CommandType.DoubleClickImage),
            new Option("Image Detection", "Move To Image", CommandType.MoveToImage),
            new Option("Image Detection", "Loop Until Image", CommandType.LoopUntilImage)
        }),
        ("Window / App", new[]
        {
            new Option("Window / App", "Focus Window", CommandType.FocusWindow),
            new Option("Window / App", "Wait For Window", CommandType.WaitForWindow),
            new Option("Window / App", "Wait For Window Gone", CommandType.WaitForWindowGone),
            new Option("Window / App", "Open Program / File / URL", CommandType.RunProgram)
        }),
        ("Flow", new[]
        {
            new Option("Flow", "Run Sequence", CommandType.RunSequence),
            new Option("Flow", "Loop X Times", CommandType.LoopTimes),
            new Option("Flow", "Loop Forever", CommandType.LoopForever),
            new Option("Flow", "Break Loop", CommandType.Break),
            new Option("Flow", "Return", CommandType.Return),
            new Option("Flow", "Stop Macro", CommandType.StopMacro)
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

    public static bool UsesColor(CommandType type) => type is CommandType.IfColor or CommandType.WaitUntilColor
        or CommandType.LoopWhileColor or CommandType.LoopUntilColor or CommandType.ClickColor;

    public static bool UsesPolling(CommandType type) => type is CommandType.WaitUntilColor or CommandType.LoopWhileColor
        or CommandType.LoopUntilColor or CommandType.WaitUntilImage or CommandType.WaitUntilImageGone or CommandType.LoopUntilImage
        or CommandType.WaitUntilKeyPressed or CommandType.WaitForWindow or CommandType.WaitForWindowGone;
}

internal static class ThemeManager
{
    public static void Apply(AppTheme theme)
    {
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
        }
    }

    private static void Set(string key, string hex)
    {
        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }
}
