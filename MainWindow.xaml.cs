using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MacroMaker;

public partial class MainWindow : Window
{
    private MacroProject _project = new();
    private MacroSequence? _currentSequence;
    private CommandRow? _selectedRow;
    private MacroEngine? _engine;
    private string? _projectPath;
    private bool _dirty;
    private int _sequenceCounter = 1;
    private bool _isRecording;
    private AppSettings _appSettings = new();

    private IntPtr _keyboardHook;
    private NativeMethods.LowLevelKeyboardProc? _keyboardProc;
    private readonly DispatcherTimer _foregroundTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private IntPtr _mainWindowHandle;
    private IntPtr _lastExternalForeground;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public MainWindow()
    {
        _appSettings = AppSettingsStore.Load();
        ThemeManager.Apply(_appSettings.Theme);
        InitializeComponent();
        NewProjectCore();
        RefreshQuickAdd();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        InstallKeyboardHook();
        _mainWindowHandle = new WindowInteropHelper(this).Handle;
        _foregroundTimer.Tick += (_, _) => TrackExternalForegroundWindow();
        _foregroundTimer.Start();
    }

    private void TrackExternalForegroundWindow()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == _mainWindowHandle || !NativeMethods.IsWindow(hwnd))
            return;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == (uint)Environment.ProcessId)
            return;

        _lastExternalForeground = hwnd;
    }

    private void FocusLastExternalWindow()
    {
        if (_lastExternalForeground == IntPtr.Zero || !NativeMethods.IsWindow(_lastExternalForeground))
            return;

        NativeMethods.SetForegroundWindow(_lastExternalForeground);
    }

    // ---------------- PROJECT ----------------

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges())
            return;

        NewProjectCore();
    }

    private void NewProjectCore()
    {
        _project = new MacroProject
        {
            Name = "Untitled Macro",
            Sequences = new List<MacroSequence>
            {
                new("Starting Sequence")
            }
        };

        _projectPath = null;
        _dirty = false;
        _sequenceCounter = 1;
        RebuildEngine();
        SelectSequence(_project.Sequences[0]);
        RefreshTabs();
        UpdateProjectTitle();
        StatusText.Text = "Ready";
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges())
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Open Macro Maker Project",
            Filter = "Macro Maker project (*.macro.json)|*.macro.json|JSON (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var project = JsonSerializer.Deserialize<MacroProject>(json, JsonOptions)
                          ?? throw new InvalidOperationException("The project file was empty.");

            project.Sequences ??= new List<MacroSequence>();
            project.RecorderSettings ??= new RecorderSettings();
            if (project.Sequences.Count == 0)
                project.Sequences.Add(new MacroSequence("Starting Sequence"));

            var starting = project.Sequences.FirstOrDefault(s =>
                s.Name.Equals("Starting Sequence", StringComparison.OrdinalIgnoreCase));
            if (starting is null)
                project.Sequences.Insert(0, new MacroSequence("Starting Sequence"));

            foreach (var sequence in project.Sequences)
            {
                sequence.Commands ??= new List<MacroCommand>();
                RepairCommandLists(sequence.Commands);
            }

            _project = project;
            _projectPath = dialog.FileName;
            _dirty = false;
            RebuildEngine();
            SelectSequence(_project.Sequences.First(s => s.Name.Equals("Starting Sequence", StringComparison.OrdinalIgnoreCase)));
            RefreshTabs();
                UpdateProjectTitle();
            StatusText.Text = "Project loaded";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void RepairCommandLists(List<MacroCommand> commands)
    {
        foreach (var command in commands)
        {
            // V1/V1.2 stored smooth movement only as a non-zero duration.
            if (command.MouseMoveMode == MouseMoveMode.Legacy)
                command.MouseMoveMode = command.MoveDurationMs > 0 ? MouseMoveMode.Smooth : MouseMoveMode.Teleport;

            command.Children ??= new List<MacroCommand>();
            command.ElseChildren ??= new List<MacroCommand>();
            RepairCommandLists(command.Children);
            RepairCommandLists(command.ElseChildren);
        }
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        SaveProject(false);
    }

    private void SaveProjectAs_Click(object sender, RoutedEventArgs e)
    {
        SaveProject(true);
    }

    private bool SaveProject(bool forceDialog)
    {
        if (forceDialog || string.IsNullOrWhiteSpace(_projectPath))
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save Macro Maker Project",
                Filter = "Macro Maker project (*.macro.json)|*.macro.json|JSON (*.json)|*.json",
                FileName = string.IsNullOrWhiteSpace(_project.Name) ? "MyMacro.macro.json" : SanitizeFileName(_project.Name) + ".macro.json"
            };

            if (dialog.ShowDialog(this) != true)
                return false;

            _projectPath = dialog.FileName;
            if (_project.Name == "Untitled Macro")
                _project.Name = Path.GetFileName(dialog.FileName).Replace(".macro.json", "", StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            var json = JsonSerializer.Serialize(_project, JsonOptions);
            File.WriteAllText(_projectPath!, json);
            _dirty = false;
            UpdateProjectTitle();
            StatusText.Text = "Saved";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save project", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(name) ? "MyMacro" : name;
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_dirty)
            return true;

        var result = MessageBox.Show(this,
            "Save changes to this macro first?",
            "Macro Maker",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => SaveProject(false),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void MarkDirty()
    {
        if (_dirty)
            return;

        _dirty = true;
        UpdateProjectTitle();
    }

    private void UpdateProjectTitle()
    {
        var star = _dirty ? " *" : string.Empty;
        ProjectTitleText.Text = _project.Name + star;
        Title = $"Macro Maker — {_project.Name}{star}";
    }

    // ---------------- SEQUENCES ----------------

    private void RefreshTabs()
    {
        TabPanel.Children.Clear();

        foreach (var sequence in _project.Sequences)
        {
            var button = new Button
            {
                Content = sequence.Name,
                Tag = sequence,
                Margin = new Thickness(0, 0, 7, 0),
                MinWidth = 125,
                Height = 38,
                Background = sequence == _currentSequence
                    ? (Brush)FindResource("Panel3Brush")
                    : (Brush)FindResource("Panel2Brush"),
                BorderBrush = sequence == _currentSequence
                    ? (Brush)FindResource("AccentBrush")
                    : (Brush)FindResource("BorderBrushDark")
            };

            button.Click += SequenceTab_Click;
            button.MouseDoubleClick += SequenceTab_MouseDoubleClick;
            TabPanel.Children.Add(button);
        }

        DeleteTabButton.IsEnabled = _currentSequence is not null && !IsStartingSequence(_currentSequence);
    }

    private void AddTabButton_Click(object sender, RoutedEventArgs e)
    {
        var defaultName = $"Sequence {_sequenceCounter++}";
        var dialog = new TextPromptWindow("Create Sequence", "Sequence name:", defaultName) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var name = dialog.Value.Trim();
        if (!ValidateNewSequenceName(name, null))
            return;

        var sequence = new MacroSequence(name);
        _project.Sequences.Add(sequence);
        MarkDirty();
        SelectSequence(sequence);
    }

    private void DeleteTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSequence is null || IsStartingSequence(_currentSequence))
            return;

        var name = _currentSequence.Name;
        if (MessageBox.Show(this,
                $"Delete sequence '{name}'?",
                "Macro Maker",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _project.Sequences.Remove(_currentSequence);
        foreach (var command in EnumerateAllCommands())
        {
            if (command.Type == CommandType.RunSequence && command.TargetSequence.Equals(name, StringComparison.OrdinalIgnoreCase))
                command.TargetSequence = "Starting Sequence";
        }

        MarkDirty();
        SelectSequence(_project.Sequences.First(s => IsStartingSequence(s)));
    }

    private void SequenceTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MacroSequence sequence })
            SelectSequence(sequence);
    }

    private void SequenceTab_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: MacroSequence sequence })
            return;

        if (IsStartingSequence(sequence))
        {
            StatusText.Text = "Starting Sequence keeps its fixed name";
            return;
        }

        var dialog = new TextPromptWindow("Rename Sequence", "Sequence name:", sequence.Name) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var name = dialog.Value.Trim();
        if (!ValidateNewSequenceName(name, sequence))
            return;

        var old = sequence.Name;
        sequence.Name = name;
        foreach (var command in EnumerateAllCommands())
        {
            if (command.Type == CommandType.RunSequence && command.TargetSequence.Equals(old, StringComparison.OrdinalIgnoreCase))
                command.TargetSequence = name;
        }

        MarkDirty();
        SequenceTitleText.Text = name;
        RefreshTabs();
        RefreshCommandList();
    }

    private bool ValidateNewSequenceName(string name, MacroSequence? ignore)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (_project.Sequences.Any(s => s != ignore && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "A sequence with that name already exists.", "Macro Maker",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        return true;
    }

    private static bool IsStartingSequence(MacroSequence sequence) =>
        sequence.Name.Equals("Starting Sequence", StringComparison.OrdinalIgnoreCase);

    private void SelectSequence(MacroSequence sequence)
    {
        _currentSequence = sequence;
        _selectedRow = null;
        SequenceTitleText.Text = sequence.Name;
        ClearProperties("Select a command to edit it.");
        RefreshTabs();
        RefreshCommandList();
    }

    // ---------------- RECORDER ----------------

    private async Task StartRecordingIntoBlockAsync(MacroCommand block)
    {
        if (_isRecording)
            return;

        if (_engine?.IsRunning == true)
        {
            StatusText.Text = "Stop the macro before recording actions";
            return;
        }

        var stopHotkey = string.IsNullOrWhiteSpace(block.RecordingStopHotkey)
            ? "F7"
            : block.RecordingStopHotkey.Trim();

        if (!GlobalInputRecorder.IsValidHotkey(stopHotkey))
        {
            MessageBox.Show(this,
                "Set a valid stop-recording hotkey in this command first. Examples: F7, Ctrl+F7, Shift+F6.",
                "Recorder", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        block.RecordingStopHotkey = stopHotkey;
        block.RecordMouseSampleMs = Math.Clamp(block.RecordMouseSampleMs, 15, 500);

        var settings = new RecorderSettings
        {
            StopHotkey = block.RecordingStopHotkey,
            RecordMouseMovement = block.RecordMouseMovement,
            MouseSampleMs = block.RecordMouseSampleMs
        };

        _isRecording = true;
        UpdateRunButtons();
        StatusText.Text = $"Recording — press {stopHotkey} to stop";

        RecordingHudWindow? hud = null;
        try
        {
            Hide();
            await Task.Delay(50);
            FocusLastExternalWindow();

            hud = new RecordingHudWindow(stopHotkey);
            hud.Show();
            FocusLastExternalWindow();

            // Let the Start Recording click fully release before input capture begins.
            await Task.Delay(220);
            FocusLastExternalWindow();

            using var recorder = new GlobalInputRecorder(settings);
            recorder.CommandCountChanged += count =>
            {
                // The recorder runs on a background task. Never read or write WPF UI
                // properties until we are back on the Dispatcher thread.
                _ = Dispatcher.BeginInvoke(() =>
                {
                    if (hud is not null && hud.IsVisible)
                        hud.SetCount(count);
                });
            };

            var captured = await recorder.StartAsync();

            block.Children.Clear();
            block.Children.AddRange(captured);

            MarkDirty();
            RefreshCommandList(block.Id);
            BuildProperties(block);
            StatusText.Text = $"Recorded {captured.Count} commands";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Recording failed";
            MessageBox.Show(ex.Message, "Recorder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (hud is not null)
            {
                try { hud.Close(); } catch { }
            }

            Show();
            WindowState = System.Windows.WindowState.Normal;
            Activate();
            _isRecording = false;
            UpdateRunButtons();
        }
    }

    // ---------------- COMMAND EDITOR ----------------

    private void QuickAdd_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CommandType type })
            return;

        InsertCommandAfterSelection(CreateCommand(type));
    }

    private void RefreshQuickAdd()
    {
        if (QuickAddPanel is null)
            return;

        QuickAddPanel.Children.Clear();
        foreach (var type in _appSettings.QuickAddCommands.Where(CommandCatalog.CanQuickAdd))
        {
            var button = new Button
            {
                Content = CommandCatalog.QuickLabel(type),
                Tag = type,
                Style = (Style)FindResource("QuickAddButtonStyle"),
                Margin = new Thickness(0, 0, 7, 6)
            };
            button.Click += QuickAdd_Click;
            QuickAddPanel.Children.Add(button);
        }

        if (QuickAddPanel.Children.Count == 0)
        {
            var button = new Button
            {
                Content = "Choose Quick Add shortcuts",
                Style = (Style)FindResource("QuickAddButtonStyle"),
                Margin = new Thickness(0, 0, 7, 6)
            };
            button.Click += SettingsButton_Click;
            QuickAddPanel.Children.Add(button);
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_appSettings) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _appSettings = dialog.Settings;
        AppSettingsStore.Save(_appSettings);
        ThemeManager.Apply(_appSettings.Theme);
        RefreshQuickAdd();
        RefreshTabs();
        RefreshCommandList(_selectedRow?.Command?.Id);
        if (_selectedRow?.Command is { } command)
            BuildProperties(command);
        StatusText.Text = "Settings saved";
    }

    private void InsertCommandAfterSelection(MacroCommand command)
    {
        if (_currentSequence is null)
            return;

        var target = _selectedRow?.Owner ?? _currentSequence.Commands;
        var insertIndex = target.Count;
        if (_selectedRow?.Command is not null && _selectedRow.Owner == target)
            insertIndex = Math.Clamp(target.IndexOf(_selectedRow.Command) + 1, 0, target.Count);

        target.Insert(insertIndex, command);
        MarkDirty();
        RefreshCommandList(command.Id);
    }

    private MacroCommand? ParseTypedCommand(string raw, out string error)
    {
        error = "Could not read that command. Try: click 960,300 | wait 500ms | record actions | run \"Upgrade Check\" | if color 0xFFFFFF at 960,300";
        var text = Regex.Replace(raw.Trim(), @"\s+", " ");

        static bool TryPoint(Match match, int xGroup, int yGroup, out int x, out int y)
        {
            var xOk = int.TryParse(match.Groups[xGroup].Value, out x);
            var yOk = int.TryParse(match.Groups[yGroup].Value, out y);
            return xOk && yOk;
        }

        Match m;

        if (Regex.IsMatch(text, @"^(?:record|record actions|recorded actions)$", RegexOptions.IgnoreCase))
            return CreateCommand(CommandType.RecordedActions);

        // Handy one-line forms for the code-like style shown in the editor idea.
        m = Regex.Match(text, "^if color\\s+(0x[0-9a-f]{6}|#[0-9a-f]{6})\\s+at\\s+(-?\\d+)\\s*[, ]\\s*(-?\\d+)\\s+then\\s+run\\s+[\\\"']?(.+?)[\\\"']?$", RegexOptions.IgnoreCase);
        if (m.Success && TryPoint(m, 2, 3, out var icx, out var icy))
        {
            var condition = new MacroCommand
            {
                Type = CommandType.IfColor,
                ColorHex = NormalizeColor(m.Groups[1].Value),
                X = icx,
                Y = icy,
                CompareMode = CompareMode.Equals
            };
            condition.Children.Add(new MacroCommand { Type = CommandType.RunSequence, TargetSequence = m.Groups[4].Value.Trim() });
            return condition;
        }

        m = Regex.Match(text, @"^loop until(?: color at)?\s+(-?\d+)\s*[, ]\s*(-?\d+)\s+(?:!=|not)\s+(?:color\s+)?(0x[0-9a-f]{6}|#[0-9a-f]{6})\s*\{\s*click\s+(-?\d+)\s*[, ]\s*(-?\d+)\s*\}$", RegexOptions.IgnoreCase);
        if (m.Success && TryPoint(m, 1, 2, out var lcx, out var lcy) && TryPoint(m, 4, 5, out var clickX, out var clickY))
        {
            var loop = new MacroCommand
            {
                Type = CommandType.LoopUntilColor,
                X = lcx,
                Y = lcy,
                ColorHex = NormalizeColor(m.Groups[3].Value),
                CompareMode = CompareMode.NotEquals
            };
            loop.Children.Add(new MacroCommand { Type = CommandType.Click, X = clickX, Y = clickY });
            return loop;
        }

        m = Regex.Match(text, @"^click(?: at)?\s+(-?\d+)\s*[, ]\s*(-?\d+)$", RegexOptions.IgnoreCase);
        if (m.Success && TryPoint(m, 1, 2, out var cx, out var cy))
            return new MacroCommand { Type = CommandType.Click, X = cx, Y = cy };

        m = Regex.Match(text, @"^double click(?: at)?\s+(-?\d+)\s*[, ]\s*(-?\d+)$", RegexOptions.IgnoreCase);
        if (m.Success && TryPoint(m, 1, 2, out var dcx, out var dcy))
            return new MacroCommand { Type = CommandType.DoubleClick, X = dcx, Y = dcy };

        m = Regex.Match(text, @"^right click(?: at)?\s+(-?\d+)\s*[, ]\s*(-?\d+)$", RegexOptions.IgnoreCase);
        if (m.Success && TryPoint(m, 1, 2, out var rcx, out var rcy))
            return new MacroCommand { Type = CommandType.RightClick, X = rcx, Y = rcy };

        m = Regex.Match(text, @"^move(?: mouse)?(?: to)?\s+(-?\d+)\s*[, ]\s*(-?\d+)(?:\s+over\s+(\d+)\s*ms)?$", RegexOptions.IgnoreCase);
        if (m.Success && TryPoint(m, 1, 2, out var mx, out var my))
        {
            var duration = 250;
            var smooth = m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out duration);
            return new MacroCommand
            {
                Type = CommandType.MoveMouse,
                X = mx,
                Y = my,
                MouseMoveMode = smooth ? MouseMoveMode.Smooth : MouseMoveMode.Teleport,
                MoveDurationMs = duration
            };
        }

        m = Regex.Match(text, @"^wait\s+(\d+)\s*(?:ms|milliseconds?)?$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var wait))
            return new MacroCommand { Type = CommandType.Wait, WaitMs = wait };

        m = Regex.Match(text, @"^random wait\s+(\d+)\s*(?:ms)?\s*(?:-|to)\s*(\d+)\s*(?:ms)?$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var minWait) && int.TryParse(m.Groups[2].Value, out var maxWait))
            return new MacroCommand { Type = CommandType.RandomWait, MinWaitMs = minWait, MaxWaitMs = maxWait };

        m = Regex.Match(text, "^run\\s+[\\\"']?(.+?)[\\\"']?$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new MacroCommand { Type = CommandType.RunSequence, TargetSequence = m.Groups[1].Value.Trim() };

        m = Regex.Match(text, @"^press\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new MacroCommand { Type = CommandType.PressKey, Key = m.Groups[1].Value.Trim() };

        m = Regex.Match(text, @"^key down\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new MacroCommand { Type = CommandType.KeyDown, Key = m.Groups[1].Value.Trim() };

        m = Regex.Match(text, @"^key up\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new MacroCommand { Type = CommandType.KeyUp, Key = m.Groups[1].Value.Trim() };

        m = Regex.Match(text, @"^type\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new MacroCommand { Type = CommandType.TypeText, Text = m.Groups[1].Value };

        m = Regex.Match(text, @"^if color(?:\s+(0x[0-9a-f]{6}|#[0-9a-f]{6}))?(?:\s+at\s+(-?\d+)\s*[, ]\s*(-?\d+)|\s+at location)?$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new MacroCommand
            {
                Type = CommandType.IfColor,
                ColorHex = m.Groups[1].Success ? NormalizeColor(m.Groups[1].Value) : "0xFFFFFF",
                X = m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var ifx) ? ifx : 960,
                Y = m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out var ify) ? ify : 300,
                CompareMode = CompareMode.Equals
            };

        m = Regex.Match(text, @"^wait until(?: color)?\s+(0x[0-9a-f]{6}|#[0-9a-f]{6})\s+at\s+(-?\d+)\s*[, ]\s*(-?\d+)$", RegexOptions.IgnoreCase);
        if (m.Success && TryPoint(m, 2, 3, out var wux, out var wuy))
            return new MacroCommand
            {
                Type = CommandType.WaitUntilColor,
                ColorHex = NormalizeColor(m.Groups[1].Value),
                X = wux,
                Y = wuy,
                CompareMode = CompareMode.Equals
            };

        m = Regex.Match(text, @"^loop until(?: color)?(?: at)?\s+(-?\d+)\s*[, ]\s*(-?\d+)\s+(?:!=|not)\s+(?:color\s+)?(0x[0-9a-f]{6}|#[0-9a-f]{6})$", RegexOptions.IgnoreCase);
        if (m.Success && TryPoint(m, 1, 2, out var lux, out var luy))
            return new MacroCommand
            {
                Type = CommandType.LoopUntilColor,
                X = lux,
                Y = luy,
                ColorHex = NormalizeColor(m.Groups[3].Value),
                CompareMode = CompareMode.NotEquals
            };

        m = Regex.Match(text, @"^loop until color\s+(0x[0-9a-f]{6}|#[0-9a-f]{6})\s+at\s+(-?\d+)\s*[, ]\s*(-?\d+)$", RegexOptions.IgnoreCase);
        if (m.Success && TryPoint(m, 2, 3, out var luex, out var luey))
            return new MacroCommand
            {
                Type = CommandType.LoopUntilColor,
                X = luex,
                Y = luey,
                ColorHex = NormalizeColor(m.Groups[1].Value),
                CompareMode = CompareMode.Equals
            };

        m = Regex.Match(text, @"^loop while color\s+(0x[0-9a-f]{6}|#[0-9a-f]{6})\s+at\s+(-?\d+)\s*[, ]\s*(-?\d+)$", RegexOptions.IgnoreCase);
        if (m.Success && TryPoint(m, 2, 3, out var lwx, out var lwy))
            return new MacroCommand
            {
                Type = CommandType.LoopWhileColor,
                X = lwx,
                Y = lwy,
                ColorHex = NormalizeColor(m.Groups[1].Value),
                CompareMode = CompareMode.Equals
            };

        m = Regex.Match(text, @"^loop\s+(\d+)\s+times?$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var times))
            return new MacroCommand { Type = CommandType.LoopTimes, RepeatCount = times };

        if (Regex.IsMatch(text, @"^loop forever$", RegexOptions.IgnoreCase))
            return new MacroCommand { Type = CommandType.LoopForever };

        if (Regex.IsMatch(text, @"^break$|^break loop$", RegexOptions.IgnoreCase))
            return new MacroCommand { Type = CommandType.Break };

        if (Regex.IsMatch(text, @"^return$", RegexOptions.IgnoreCase))
            return new MacroCommand { Type = CommandType.Return };


        if (Regex.IsMatch(text, @"^then$|^else$|^[{}]$", RegexOptions.IgnoreCase))
        {
            error = "THEN/ELSE/braces are visual here: select the IF/Loop and use + Add Inside or + Add Else.";
            return null;
        }

        m = Regex.Match(text, "^if image(?: found)?(?:\\s+[\\\"'](.+?)[\\\"'])?$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new MacroCommand { Type = CommandType.IfImage, ImagePath = m.Groups[1].Success ? m.Groups[1].Value : string.Empty };

        m = Regex.Match(text, "^wait until image(?:\\s+[\\\"'](.+?)[\\\"'])?$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new MacroCommand { Type = CommandType.WaitUntilImage, ImagePath = m.Groups[1].Success ? m.Groups[1].Value : string.Empty };

        m = Regex.Match(text, "^(?:click image|find \\+ click image)(?:\\s+[\\\"'](.+?)[\\\"'])?$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new MacroCommand { Type = CommandType.ClickImage, ImagePath = m.Groups[1].Success ? m.Groups[1].Value : string.Empty };

        return null;
    }

    private void AddCommandButton_Click(object sender, RoutedEventArgs e)
    {
        OpenCommandMenu(sender as FrameworkElement, type =>
        {
            if (_currentSequence is null)
                return;

            var command = CreateCommand(type);
            InsertCommandAfterSelection(command);
        });
    }

    private void AddInsideButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveBlockTarget(out var parent, out var target, out _))
        {
            StatusText.Text = "Select an IF, Loop, THEN/ELSE row, or a command already inside one";
            return;
        }

        OpenCommandMenu(sender as FrameworkElement, type =>
        {
            var command = CreateCommand(type);
            target.Add(command);
            MarkDirty();
            RefreshCommandList(command.Id);
            StatusText.Text = target == parent.ElseChildren ? "Added to ELSE" : parent.HasElse ? "Added to THEN" : "Added to loop";
        });
    }

    private void AddElseButton_Click(object sender, RoutedEventArgs e)
    {
        var parent = ResolveIfParent();
        if (parent is null)
        {
            StatusText.Text = "ELSE is available on IF Color and IF Image";
            return;
        }

        OpenCommandMenu(sender as FrameworkElement, type =>
        {
            var command = CreateCommand(type);
            parent.ElseChildren.Add(command);
            MarkDirty();
            RefreshCommandList(command.Id);
            StatusText.Text = "Added to ELSE";
        });
    }

    private bool TryResolveBlockTarget(out MacroCommand parent, out List<MacroCommand> target, out string label)
    {
        parent = null!;
        target = null!;
        label = string.Empty;

        var row = _selectedRow;
        if (row is null)
            return false;

        if (row.IsHeader && row.ParentCommand is { } headerParent)
        {
            parent = headerParent;
            if (row.Branch == CommandBranch.Else)
            {
                target = parent.ElseChildren;
                label = "ELSE";
            }
            else
            {
                target = parent.Children;
                label = parent.HasElse ? "THEN" : "Loop";
            }
            return true;
        }

        if (row.Command is { } selected && selected.HasBody && selected.Type != CommandType.RecordedActions)
        {
            parent = selected;
            target = selected.Children;
            label = selected.HasElse ? "THEN" : "Loop";
            return true;
        }

        if (row.ParentCommand is { } containingParent)
        {
            parent = containingParent;
            if (row.Branch == CommandBranch.Else)
            {
                target = parent.ElseChildren;
                label = "ELSE";
            }
            else
            {
                target = parent.Children;
                label = parent.HasElse ? "THEN" : "Loop";
            }
            return true;
        }

        return false;
    }

    private MacroCommand? ResolveIfParent()
    {
        if (_selectedRow?.Command is { HasElse: true } selected)
            return selected;
        if (_selectedRow?.ParentCommand is { HasElse: true } parent)
            return parent;
        return null;
    }

    private void UpdateBlockButtonState()
    {
        if (AddBlockButton is null || AddElseButton is null)
            return;

        if (TryResolveBlockTarget(out _, out _, out var label))
        {
            AddBlockButton.IsEnabled = true;
            AddBlockButton.Content = label switch
            {
                "THEN" => "+ Add to THEN",
                "ELSE" => "+ Add to ELSE",
                _ => "+ Add to Loop"
            };
        }
        else
        {
            AddBlockButton.IsEnabled = false;
            AddBlockButton.Content = "+ Add to Block";
        }

        AddElseButton.IsEnabled = ResolveIfParent() is not null;
    }

    private void OpenCommandMenu(FrameworkElement? target, Action<CommandType> onSelected)
    {
        var picker = new CommandPickerWindow
        {
            Owner = this
        };

        if (picker.ShowDialog() == true && picker.SelectedType is CommandType type)
            onSelected(type);
    }

    private MacroCommand CreateCommand(CommandType type)
    {
        var defaults = _appSettings.DefaultsFor(type);
        var command = new MacroCommand
        {
            Type = type,
            X = defaults.X,
            Y = defaults.Y,
            EndX = defaults.EndX,
            EndY = defaults.EndY,
            MouseMoveMode = defaults.MouseMoveMode == MouseMoveMode.Legacy ? MouseMoveMode.Smooth : defaults.MouseMoveMode,
            MoveDurationMs = defaults.MoveDurationMs,
            ClickDelayMs = defaults.ClickDelayMs,
            ScrollAmount = defaults.ScrollAmount,
            DragDurationMs = defaults.DragDurationMs,
            HoldMs = defaults.HoldMs,
            Key = defaults.Key,
            Text = defaults.Text,
            WaitMs = defaults.WaitMs,
            MinWaitMs = defaults.MinWaitMs,
            MaxWaitMs = defaults.MaxWaitMs,
            RecordingStopHotkey = defaults.RecordingStopHotkey,
            RecordMouseMovement = defaults.RecordMouseMovement,
            RecordMouseSampleMs = defaults.RecordMouseSampleMs,
            PollMs = defaults.PollMs,
            TimeoutMs = defaults.TimeoutMs,
            ColorHex = defaults.ColorHex,
            ColorTolerance = defaults.ColorTolerance,
            CompareMode = defaults.CompareMode,
            ImageTolerance = defaults.ImageTolerance,
            WindowTitle = defaults.WindowTitle,
            ProgramPath = defaults.ProgramPath,
            SearchX = defaults.SearchX,
            SearchY = defaults.SearchY,
            SearchWidth = defaults.SearchWidth,
            SearchHeight = defaults.SearchHeight,
            ImageOffsetX = defaults.ImageOffsetX,
            ImageOffsetY = defaults.ImageOffsetY,
            RepeatCount = defaults.RepeatCount
        };

        switch (type)
        {
            case CommandType.RecordedActions:
                command.Text = "Record Actions";
                break;
            case CommandType.RunSequence:
                command.TargetSequence = _project.Sequences.FirstOrDefault(s => s != _currentSequence)?.Name
                                         ?? "Starting Sequence";
                break;
        }

        return command;
    }

    private void DuplicateCommandButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRow?.Command is not { } command || _selectedRow.Owner is null)
            return;

        var owner = _selectedRow.Owner;
        var index = owner.IndexOf(command);
        var clone = command.DeepClone();
        owner.Insert(Math.Clamp(index + 1, 0, owner.Count), clone);
        MarkDirty();
        RefreshCommandList(clone.Id);
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDownButton_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int direction)
    {
        if (_selectedRow?.Command is not { } command || _selectedRow.Owner is null || direction == 0)
            return;

        var row = _selectedRow;
        var owner = row.Owner;
        var index = owner.IndexOf(command);
        if (index < 0)
            return;

        // Normal move between commands in the same block.
        var siblingIndex = index + Math.Sign(direction);
        if (siblingIndex >= 0 && siblingIndex < owner.Count)
        {
            owner.RemoveAt(index);
            owner.Insert(siblingIndex, command);
            MarkDirty();
            RefreshCommandList(command.Id);
            return;
        }

        // At a block edge, cross the structural THEN / ELSE / DO boundary.
        if (row.ParentCommand is not { } parent)
            return;

        if (direction < 0)
        {
            if (row.Branch == CommandBranch.Else)
            {
                owner.Remove(command);
                parent.Children.Add(command);
            }
            else if (TryFindCommandLocation(parent, out var parentOwner, out _, out _))
            {
                owner.Remove(command);
                var parentIndex = parentOwner.IndexOf(parent);
                parentOwner.Insert(Math.Max(0, parentIndex), command);
            }
            else
            {
                return;
            }
        }
        else
        {
            if (row.Branch == CommandBranch.Body && parent.HasElse && parent.ElseChildren.Count > 0)
            {
                owner.Remove(command);
                parent.ElseChildren.Insert(0, command);
            }
            else if (TryFindCommandLocation(parent, out var parentOwner, out _, out _))
            {
                owner.Remove(command);
                var parentIndex = parentOwner.IndexOf(parent);
                parentOwner.Insert(Math.Clamp(parentIndex + 1, 0, parentOwner.Count), command);
            }
            else
            {
                return;
            }
        }

        MarkDirty();
        RefreshCommandList(command.Id);
    }

    private bool TryFindCommandLocation(MacroCommand target, out List<MacroCommand> owner, out MacroCommand? parent, out CommandBranch branch)
    {
        if (_currentSequence is null)
        {
            owner = null!;
            parent = null;
            branch = CommandBranch.Root;
            return false;
        }

        return FindCommandLocationRecursive(
            _currentSequence.Commands,
            target,
            null,
            CommandBranch.Root,
            out owner,
            out parent,
            out branch);
    }

    private static bool FindCommandLocationRecursive(
        List<MacroCommand> list,
        MacroCommand target,
        MacroCommand? containingParent,
        CommandBranch containingBranch,
        out List<MacroCommand> owner,
        out MacroCommand? parent,
        out CommandBranch branch)
    {
        foreach (var item in list)
        {
            if (ReferenceEquals(item, target) || item.Id == target.Id)
            {
                owner = list;
                parent = containingParent;
                branch = containingBranch;
                return true;
            }

            if (FindCommandLocationRecursive(item.Children, target, item, CommandBranch.Body, out owner, out parent, out branch))
                return true;
            if (FindCommandLocationRecursive(item.ElseChildren, target, item, CommandBranch.Else, out owner, out parent, out branch))
                return true;
        }

        owner = null!;
        parent = null;
        branch = CommandBranch.Root;
        return false;
    }

    private void DeleteCommandButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRow?.Command is not { } command || _selectedRow.Owner is null)
            return;

        _selectedRow.Owner.Remove(command);
        _selectedRow = null;
        MarkDirty();
        RefreshCommandList();
        ClearProperties("Select a command to edit it.");
    }

    private void CommandList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CommandList.SelectedItem is not CommandRow row)
        {
            _selectedRow = null;
            UpdateBlockButtonState();
            ClearProperties("Select a command to edit it.");
            return;
        }

        _selectedRow = row;
        UpdateBlockButtonState();

        if (row.IsHeader)
        {
            ClearProperties(row.Label + " block selected");
            return;
        }

        if (row.Command is null)
        {
            ClearProperties("Select a command to edit it.");
            return;
        }

        BuildProperties(row.Command);
    }

    private void CommandList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_selectedRow?.Command is { } command)
            BuildProperties(command);
    }

    private void RefreshCommandList(Guid? selectId = null)
    {
        if (_currentSequence is null)
            return;

        var rows = new List<CommandRow>();
        FlattenCommands(_currentSequence.Commands, rows, 0, CommandBranch.Root);
        CommandList.ItemsSource = rows;

        if (selectId.HasValue)
        {
            var row = rows.FirstOrDefault(r => r.Command?.Id == selectId.Value);
            if (row is not null)
            {
                CommandList.SelectedItem = row;
                CommandList.ScrollIntoView(row);
                _selectedRow = row;
            }
        }
        else if (_selectedRow?.Command is { } selected)
        {
            var row = rows.FirstOrDefault(r => r.Command?.Id == selected.Id);
            if (row is not null)
            {
                CommandList.SelectedItem = row;
                _selectedRow = row;
            }
        }

        UpdateBlockButtonState();
    }

    private static void FlattenCommands(List<MacroCommand> source, List<CommandRow> rows, int depth, CommandBranch branch, MacroCommand? parentCommand = null)
    {
        foreach (var command in source)
        {
            if (command.Type == CommandType.Comment)
                continue;

            rows.Add(new CommandRow
            {
                Command = command,
                Owner = source,
                ParentCommand = parentCommand,
                Depth = depth,
                Branch = branch
            });

            if (!command.HasBody || command.Type == CommandType.RecordedActions)
                continue;

            var bodyLabel = command.HasElse ? "THEN" : "DO";
            rows.Add(new CommandRow
            {
                IsHeader = true,
                Label = bodyLabel,
                Owner = command.Children,
                ParentCommand = command,
                Depth = depth + 1,
                Branch = CommandBranch.Body
            });
            FlattenCommands(command.Children, rows, depth + 2, CommandBranch.Body, command);

            if (command.HasElse && command.ElseChildren.Count > 0)
            {
                rows.Add(new CommandRow
                {
                    IsHeader = true,
                    Label = "ELSE",
                    Owner = command.ElseChildren,
                    ParentCommand = command,
                    Depth = depth + 1,
                    Branch = CommandBranch.Else
                });
                FlattenCommands(command.ElseChildren, rows, depth + 2, CommandBranch.Else, command);
            }
        }
    }

    // ---------------- PROPERTIES ----------------

    private void BuildProperties(MacroCommand command)
    {
        PropertiesPanel.Children.Clear();
        PropertiesHintText.Text = FriendlyName(command.Type);
        AddReadOnlyField("Command", FriendlyName(command.Type));

        switch (command.Type)
        {
            case CommandType.Comment:
                AddTextField("Comment", command.Text, value => command.Text = value, true);
                break;

            case CommandType.MoveMouse:
                AddLocationFields(command);
                AddMouseMovementFields(command);
                break;

            case CommandType.Click:
            case CommandType.RightClick:
                AddLocationFields(command);
                AddMouseMovementFields(command);
                break;

            case CommandType.DoubleClick:
                AddLocationFields(command);
                AddMouseMovementFields(command);
                AddNumberField("Delay between clicks (ms)", command.ClickDelayMs, 20, 1000, value => command.ClickDelayMs = value);
                break;

            case CommandType.Scroll:
                AddLocationFields(command);
                AddNumberField("Wheel amount (120 = up, -120 = down)", command.ScrollAmount, -12000, 12000, value => command.ScrollAmount = value);
                AddMouseMovementFields(command);
                break;

            case CommandType.DragMouse:
                AddLocationFields(command, "Start location");
                AddEndLocationFields(command);
                AddMouseMovementFields(command);
                AddNumberField("Drag duration (ms)", command.DragDurationMs, 1, 60000, value => command.DragDurationMs = value);
                break;

            case CommandType.LeftMouseDown:
            case CommandType.LeftMouseUp:
            case CommandType.RightMouseDown:
            case CommandType.RightMouseUp:
                AddInfo("Use these with Move Mouse to build click-and-drag or hold actions.");
                break;

            case CommandType.PressKey:
                AddTextField("Key or combo", command.Key, value => command.Key = value);
                AddInfo("Examples: E, Space, Enter, F5, Ctrl+C, Ctrl+Shift+S");
                break;

            case CommandType.KeyDown:
            case CommandType.KeyUp:
                AddTextField("Key", command.Key, value => command.Key = value);
                AddInfo("Use Key Down + Key Up when you need to hold a key across other commands.");
                break;

            case CommandType.TypeText:
                AddTextField("Text", command.Text, value => command.Text = value, true);
                break;

            case CommandType.HoldKey:
                AddTextField("Key", command.Key, value => command.Key = value);
                AddNumberField("Hold for (ms)", command.HoldMs, 1, 86_400_000, value => command.HoldMs = value);
                break;

            case CommandType.RepeatKey:
                AddTextField("Key or combo", command.Key, value => command.Key = value);
                AddNumberField("Repeat count", command.RepeatCount, 1, 1_000_000, value => command.RepeatCount = value);
                AddNumberField("Delay between presses (ms)", command.WaitMs, 0, 86_400_000, value => command.WaitMs = value);
                break;

            case CommandType.WaitUntilKeyPressed:
                AddTextField("Key", command.Key, value => command.Key = value);
                AddPollingFields(command);
                break;

            case CommandType.Wait:
                AddNumberField("Milliseconds", command.WaitMs, 0, 86_400_000, value => command.WaitMs = value);
                break;

            case CommandType.RandomWait:
                AddNumberField("Minimum ms", command.MinWaitMs, 0, 86_400_000, value => command.MinWaitMs = value);
                AddNumberField("Maximum ms", command.MaxWaitMs, 0, 86_400_000, value => command.MaxWaitMs = value);
                break;

            case CommandType.RecordedActions:
                AddRecordedActionsProperties(command);
                break;

            case CommandType.IfColor:
            case CommandType.WaitUntilColor:
            case CommandType.LoopWhileColor:
            case CommandType.LoopUntilColor:
                AddColorConditionFields(command);
                if (command.Type == CommandType.WaitUntilColor)
                    AddPollingFields(command);
                else if (command.Type is CommandType.LoopWhileColor or CommandType.LoopUntilColor)
                    AddNumberField("Idle poll ms (used if body is empty)", command.PollMs, 10, 5000, value => command.PollMs = value);
                if (command.HasBody)
                    AddBlockInfo(command);
                break;

            case CommandType.ClickColor:
                AddColorField(command);
                AddNumberField("Color tolerance (0-255 per channel)", command.ColorTolerance, 0, 255, value => command.ColorTolerance = value);
                AddSearchAreaFields(command);
                AddMouseMovementFields(command);
                AddColorSearchTest(command);
                break;

            case CommandType.IfImage:
            case CommandType.WaitUntilImage:
            case CommandType.WaitUntilImageGone:
            case CommandType.ClickImage:
            case CommandType.DoubleClickImage:
            case CommandType.MoveToImage:
            case CommandType.LoopUntilImage:
                AddImageFields(command);
                if (command.Type is CommandType.WaitUntilImage or CommandType.WaitUntilImageGone)
                    AddPollingFields(command);
                if (command.Type is CommandType.ClickImage or CommandType.DoubleClickImage or CommandType.MoveToImage)
                {
                    AddNumberField("Offset X from image center", command.ImageOffsetX, -10000, 10000, value => command.ImageOffsetX = value);
                    AddNumberField("Offset Y from image center", command.ImageOffsetY, -10000, 10000, value => command.ImageOffsetY = value);
                    AddMouseMovementFields(command);
                    if (command.Type == CommandType.DoubleClickImage)
                        AddNumberField("Delay between clicks (ms)", command.ClickDelayMs, 20, 1000, value => command.ClickDelayMs = value);
                }
                if (command.Type == CommandType.LoopUntilImage)
                    AddNumberField("Idle poll ms (used if body is empty)", command.PollMs, 10, 5000, value => command.PollMs = value);
                if (command.HasBody)
                    AddBlockInfo(command);
                break;

            case CommandType.FocusWindow:
                AddTextField("Window title contains", command.WindowTitle, value => command.WindowTitle = value);
                break;

            case CommandType.WaitForWindow:
            case CommandType.WaitForWindowGone:
                AddTextField("Window title contains", command.WindowTitle, value => command.WindowTitle = value);
                AddPollingFields(command);
                break;

            case CommandType.RunProgram:
                AddProgramField(command);
                break;

            case CommandType.RunSequence:
                AddSequencePicker(command);
                break;

            case CommandType.LoopTimes:
                AddNumberField("Repeat count", command.RepeatCount, 0, 1_000_000, value => command.RepeatCount = value);
                AddBlockInfo(command);
                break;

            case CommandType.LoopForever:
                AddBlockInfo(command);
                AddInfo("Use Break Loop inside this block, or F8 at any time, to stop it.");
                break;

            case CommandType.Break:
                AddInfo("Stops the nearest Loop block and continues after that loop.");
                break;

            case CommandType.Return:
                AddInfo("Ends the current sequence and returns to the sequence that called it.");
                break;

            case CommandType.StopMacro:
                AddInfo("Stops the entire macro immediately, the same as pressing F8.");
                break;
        }
    }

    private void AddMouseMovementFields(MacroCommand command)
    {
        if (command.MouseMoveMode == MouseMoveMode.Legacy)
            command.MouseMoveMode = command.MoveDurationMs > 0 ? MouseMoveMode.Smooth : MouseMoveMode.Teleport;

        AddLabel("Mouse movement");
        var mode = new ComboBox
        {
            ItemsSource = new[] { MouseMoveMode.Teleport, MouseMoveMode.Smooth },
            SelectedItem = command.MouseMoveMode,
            Margin = new Thickness(0, 0, 0, 10)
        };
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedItem is not MouseMoveMode selected || selected == command.MouseMoveMode)
                return;

            command.MouseMoveMode = selected;
            if (selected == MouseMoveMode.Smooth && command.MoveDurationMs <= 0)
                command.MoveDurationMs = 250;
            MarkDirtyAndRefresh(command.Id);
            BuildProperties(command);
        };
        PropertiesPanel.Children.Add(mode);

        if (command.MouseMoveMode == MouseMoveMode.Smooth)
            AddNumberField("Smooth move duration (ms)", command.MoveDurationMs, 1, 60000, value => command.MoveDurationMs = value);
    }

    private void AddRecordedActionsProperties(MacroCommand command)
    {
        AddLabel("Stop recording hotkey");
        var hotkeyBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(command.RecordingStopHotkey) ? "F7" : command.RecordingStopHotkey,
            Margin = new Thickness(0, 0, 0, 8)
        };
        hotkeyBox.LostFocus += (_, _) =>
        {
            var value = hotkeyBox.Text.Trim();
            if (!GlobalInputRecorder.IsValidHotkey(value))
            {
                MessageBox.Show(this,
                    "Invalid hotkey. Examples: F7, F10, Ctrl+F7, Shift+F6.",
                    "Recorder Hotkey", MessageBoxButton.OK, MessageBoxImage.Information);
                hotkeyBox.Text = string.IsNullOrWhiteSpace(command.RecordingStopHotkey) ? "F7" : command.RecordingStopHotkey;
                return;
            }

            command.RecordingStopHotkey = value;
            hotkeyBox.Text = value;
            MarkDirtyAndRefresh(command.Id);
        };
        PropertiesPanel.Children.Add(hotkeyBox);

        var mouseMovement = new CheckBox
        {
            Content = "Record mouse movement",
            IsChecked = command.RecordMouseMovement,
            Margin = new Thickness(0, 0, 0, 8)
        };
        mouseMovement.Checked += (_, _) =>
        {
            command.RecordMouseMovement = true;
            MarkDirtyAndRefresh(command.Id);
            BuildProperties(command);
        };
        mouseMovement.Unchecked += (_, _) =>
        {
            command.RecordMouseMovement = false;
            MarkDirtyAndRefresh(command.Id);
            BuildProperties(command);
        };
        PropertiesPanel.Children.Add(mouseMovement);

        if (command.RecordMouseMovement)
            AddNumberField("Mouse sample interval (ms)", command.RecordMouseSampleMs, 15, 500, value => command.RecordMouseSampleMs = value);

        var recordButton = new Button
        {
            Content = command.Children.Count == 0 ? "● Start Recording" : "● Re-record",
            Margin = new Thickness(0, 0, 0, 14)
        };
        recordButton.Click += async (_, _) =>
        {
            var value = hotkeyBox.Text.Trim();
            if (!GlobalInputRecorder.IsValidHotkey(value))
            {
                MessageBox.Show(this,
                    "Set a valid stop hotkey first. Examples: F7, F10, Ctrl+F7.",
                    "Recorder", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            command.RecordingStopHotkey = value;
            await StartRecordingIntoBlockAsync(command);
        };
        PropertiesPanel.Children.Add(recordButton);

        AddLabel($"Recorded actions ({command.Children.Count})");

        if (command.Children.Count == 0)
        {
            PropertiesPanel.Children.Add(new TextBlock
            {
                Text = "No actions recorded",
                Foreground = (Brush)FindResource("MutedTextBrush"),
                Margin = new Thickness(0, 2, 0, 8)
            });
            return;
        }

        for (var i = 0; i < command.Children.Count; i++)
        {
            var index = i;
            var action = command.Children[index];
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new TextBlock
            {
                Text = action.DisplayText(),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 6, 8, 6)
            };

            var up = new Button { Content = "↑", Width = 34, Height = 30, Padding = new Thickness(0), Margin = new Thickness(3, 0, 0, 0), IsEnabled = index > 0 };
            var down = new Button { Content = "↓", Width = 34, Height = 30, Padding = new Thickness(0), Margin = new Thickness(3, 0, 0, 0), IsEnabled = index < command.Children.Count - 1 };
            var remove = new Button { Content = "×", Width = 34, Height = 30, Padding = new Thickness(0), Margin = new Thickness(3, 0, 0, 0) };

            up.Click += (_, _) =>
            {
                if (index <= 0 || index >= command.Children.Count) return;
                var item = command.Children[index];
                command.Children.RemoveAt(index);
                command.Children.Insert(index - 1, item);
                MarkDirty();
                RefreshCommandList(command.Id);
                BuildProperties(command);
            };
            down.Click += (_, _) =>
            {
                if (index < 0 || index >= command.Children.Count - 1) return;
                var item = command.Children[index];
                command.Children.RemoveAt(index);
                command.Children.Insert(index + 1, item);
                MarkDirty();
                RefreshCommandList(command.Id);
                BuildProperties(command);
            };
            remove.Click += (_, _) =>
            {
                if (index < 0 || index >= command.Children.Count) return;
                command.Children.RemoveAt(index);
                MarkDirty();
                RefreshCommandList(command.Id);
                BuildProperties(command);
            };

            Grid.SetColumn(text, 0);
            Grid.SetColumn(up, 1);
            Grid.SetColumn(down, 2);
            Grid.SetColumn(remove, 3);
            row.Children.Add(text);
            row.Children.Add(up);
            row.Children.Add(down);
            row.Children.Add(remove);

            PropertiesPanel.Children.Add(new Border
            {
                Background = (Brush)FindResource("Panel2Brush"),
                BorderBrush = (Brush)FindResource("BorderBrushDark"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = row
            });
        }
    }

    private void AddColorConditionFields(MacroCommand command)
    {
        AddComparePicker(command);
        AddColorField(command);
        AddLocationFields(command);
        AddNumberField("Color tolerance (0-255 per channel)", command.ColorTolerance, 0, 255, value => command.ColorTolerance = value);

        var test = new Button { Content = "Test Pixel Now", Margin = new Thickness(0, 0, 0, 12) };
        test.Click += (_, _) =>
        {
            var current = ScreenTools.GetPixelHex(command.X, command.Y);
            var match = ScreenTools.ColorMatches(command.X, command.Y, command.ColorHex, command.ColorTolerance);
            MessageBox.Show(this,
                $"Current color: {current}\nTarget: {command.ColorHex}\nWithin tolerance: {(match ? "YES" : "NO")}",
                "Pixel Test", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        PropertiesPanel.Children.Add(test);
    }

    private void AddComparePicker(MacroCommand command)
    {
        AddLabel("Comparison");
        var combo = new ComboBox
        {
            ItemsSource = Enum.GetValues<CompareMode>(),
            SelectedItem = command.CompareMode,
            Margin = new Thickness(0, 0, 0, 12)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is CompareMode mode)
            {
                command.CompareMode = mode;
                MarkDirtyAndRefresh(command.Id);
            }
        };
        PropertiesPanel.Children.Add(combo);
    }

    private void AddColorField(MacroCommand command)
    {
        AddLabel("Target color");
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new TextBox { Text = command.ColorHex, Margin = new Thickness(0, 0, 7, 0) };
        box.LostFocus += (_, _) =>
        {
            command.ColorHex = NormalizeColor(box.Text);
            box.Text = command.ColorHex;
            MarkDirtyAndRefresh(command.Id);
        };

        var pick = new Button { Content = "Pick Screen", MinWidth = 95 };
        pick.Click += (_, _) =>
        {
            var picker = new PointPickerWindow(true);
            Hide();
            try
            {
                if (picker.ShowDialog() == true)
                {
                    command.X = picker.PickedX;
                    command.Y = picker.PickedY;
                    command.ColorHex = picker.PickedColor;
                    MarkDirty();
                }
            }
            finally
            {
                Show();
                Activate();
            }
            BuildProperties(command);
            RefreshCommandList(command.Id);
        };

        Grid.SetColumn(box, 0);
        Grid.SetColumn(pick, 1);
        row.Children.Add(box);
        row.Children.Add(pick);
        PropertiesPanel.Children.Add(row);
    }

    private void AddLocationFields(MacroCommand command, string label = "Location")
    {
        AddLabel(label);
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var xLabel = new TextBlock { Text = "X", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        var xBox = new TextBox { Text = command.X.ToString(), Margin = new Thickness(0, 0, 10, 0) };
        var yLabel = new TextBlock { Text = "Y", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        var yBox = new TextBox { Text = command.Y.ToString() };

        xBox.LostFocus += (_, _) =>
        {
            if (int.TryParse(xBox.Text, out var value))
            {
                command.X = value;
                MarkDirtyAndRefresh(command.Id);
            }
        };
        yBox.LostFocus += (_, _) =>
        {
            if (int.TryParse(yBox.Text, out var value))
            {
                command.Y = value;
                MarkDirtyAndRefresh(command.Id);
            }
        };

        Grid.SetColumn(xLabel, 0); Grid.SetColumn(xBox, 1); Grid.SetColumn(yLabel, 2); Grid.SetColumn(yBox, 3);
        grid.Children.Add(xLabel); grid.Children.Add(xBox); grid.Children.Add(yLabel); grid.Children.Add(yBox);
        PropertiesPanel.Children.Add(grid);

        var set = new Button { Content = "Set Location From Screen", Margin = new Thickness(0, 0, 0, 12) };
        set.Click += (_, _) =>
        {
            var picker = new PointPickerWindow(false);
            Hide();
            try
            {
                if (picker.ShowDialog() == true)
                {
                    command.X = picker.PickedX;
                    command.Y = picker.PickedY;
                    MarkDirty();
                }
            }
            finally
            {
                Show();
                Activate();
            }
            BuildProperties(command);
            RefreshCommandList(command.Id);
        };
        PropertiesPanel.Children.Add(set);
    }

    private void AddEndLocationFields(MacroCommand command)
    {
        AddLabel("End location");
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var xLabel = new TextBlock { Text = "X", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        var xBox = new TextBox { Text = command.EndX.ToString(), Margin = new Thickness(0, 0, 10, 0) };
        var yLabel = new TextBlock { Text = "Y", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        var yBox = new TextBox { Text = command.EndY.ToString() };

        xBox.LostFocus += (_, _) =>
        {
            if (int.TryParse(xBox.Text, out var value))
            {
                command.EndX = value;
                MarkDirtyAndRefresh(command.Id);
            }
        };
        yBox.LostFocus += (_, _) =>
        {
            if (int.TryParse(yBox.Text, out var value))
            {
                command.EndY = value;
                MarkDirtyAndRefresh(command.Id);
            }
        };

        Grid.SetColumn(xLabel, 0); Grid.SetColumn(xBox, 1); Grid.SetColumn(yLabel, 2); Grid.SetColumn(yBox, 3);
        grid.Children.Add(xLabel); grid.Children.Add(xBox); grid.Children.Add(yLabel); grid.Children.Add(yBox);
        PropertiesPanel.Children.Add(grid);

        var set = new Button { Content = "Set End From Screen", Margin = new Thickness(0, 0, 0, 12) };
        set.Click += (_, _) =>
        {
            var picker = new PointPickerWindow(false);
            Hide();
            try
            {
                if (picker.ShowDialog() == true)
                {
                    command.EndX = picker.PickedX;
                    command.EndY = picker.PickedY;
                    MarkDirty();
                }
            }
            finally
            {
                Show();
                Activate();
            }
            BuildProperties(command);
            RefreshCommandList(command.Id);
        };
        PropertiesPanel.Children.Add(set);
    }

    private void AddSearchAreaFields(MacroCommand command)
    {
        AddLabel("Search area");
        var regionText = command.SearchWidth <= 0 || command.SearchHeight <= 0
            ? "Full screen"
            : $"X {command.SearchX}, Y {command.SearchY}, W {command.SearchWidth}, H {command.SearchHeight}";
        PropertiesPanel.Children.Add(new TextBlock
        {
            Text = regionText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 7),
            Foreground = (Brush)FindResource("TextBrush")
        });

        var buttons = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var setRegion = new Button { Content = "Set Search Area", Margin = new Thickness(0, 0, 4, 0) };
        var fullRegion = new Button { Content = "Use Full Screen", Margin = new Thickness(4, 0, 0, 0) };
        setRegion.Click += (_, _) =>
        {
            var picker = new RegionPickerWindow();
            Hide();
            try
            {
                if (picker.ShowDialog() == true)
                {
                    command.SearchX = picker.Region.X;
                    command.SearchY = picker.Region.Y;
                    command.SearchWidth = picker.Region.Width;
                    command.SearchHeight = picker.Region.Height;
                    MarkDirty();
                }
            }
            finally
            {
                Show();
                WindowState = System.Windows.WindowState.Normal;
                Activate();
            }
            BuildProperties(command);
            RefreshCommandList(command.Id);
        };
        fullRegion.Click += (_, _) =>
        {
            command.SearchX = command.SearchY = command.SearchWidth = command.SearchHeight = 0;
            MarkDirty();
            BuildProperties(command);
            RefreshCommandList(command.Id);
        };
        Grid.SetColumn(setRegion, 0); Grid.SetColumn(fullRegion, 1);
        buttons.Children.Add(setRegion); buttons.Children.Add(fullRegion);
        PropertiesPanel.Children.Add(buttons);
    }

    private void AddColorSearchTest(MacroCommand command)
    {
        var test = new Button { Content = "Test Color Search", Margin = new Thickness(0, 0, 0, 12) };
        test.Click += async (_, _) =>
        {
            Hide();
            await Task.Delay(120);
            (int X, int Y)? point = null;
            try
            {
                point = await ColorFinder.FindAsync(command, CancellationToken.None);
            }
            finally
            {
                Show();
                Activate();
            }

            MessageBox.Show(this, point is null
                ? "Color not found in the search area."
                : $"Found at {point.Value.X}, {point.Value.Y}.",
                "Color Search", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        PropertiesPanel.Children.Add(test);
    }

    private void AddProgramField(MacroCommand command)
    {
        AddLabel("Program, file, folder, or URL");
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox { Text = command.ProgramPath, Margin = new Thickness(0, 0, 7, 0) };
        box.LostFocus += (_, _) =>
        {
            command.ProgramPath = box.Text.Trim();
            MarkDirtyAndRefresh(command.Id);
        };
        var browse = new Button { Content = "Browse…", MinWidth = 85 };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose program or file",
                Filter = "All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) == true)
            {
                command.ProgramPath = dialog.FileName;
                box.Text = command.ProgramPath;
                MarkDirtyAndRefresh(command.Id);
            }
        };
        Grid.SetColumn(box, 0); Grid.SetColumn(browse, 1);
        grid.Children.Add(box); grid.Children.Add(browse);
        PropertiesPanel.Children.Add(grid);
    }

    private void AddImageFields(MacroCommand command)
    {
        AddLabel("Reference image");
        var pathRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var pathBox = new TextBox { Text = command.ImagePath, IsReadOnly = true, Margin = new Thickness(0, 0, 7, 0) };
        var choose = new Button { Content = "Choose...", MinWidth = 80 };
        var capture = new Button { Content = "Capture", MinWidth = 80, Margin = new Thickness(7, 0, 0, 0) };
        choose.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose image to detect",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) == true)
            {
                command.ImagePath = dialog.FileName;
                MarkDirty();
                BuildProperties(command);
                RefreshCommandList(command.Id);
            }
        };
        capture.Click += async (_, _) =>
        {
            var picker = new RegionPickerWindow();
            System.Windows.Media.Imaging.BitmapSource? image = null;

            Hide();
            try
            {
                if (picker.ShowDialog() == true)
                {
                    await Task.Delay(100);
                    image = ScreenTools.CaptureRegion(picker.Region);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Capture failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Show();
                WindowState = System.Windows.WindowState.Normal;
                Activate();
            }

            if (image is not null)
            {
                var save = new SaveFileDialog
                {
                    Title = "Save reference image",
                    Filter = "PNG image (*.png)|*.png",
                    FileName = "reference.png"
                };
                if (save.ShowDialog(this) == true)
                {
                    ScreenTools.SavePng(image, save.FileName);
                    command.ImagePath = save.FileName;
                    MarkDirty();
                }
            }

            BuildProperties(command);
            RefreshCommandList(command.Id);
        };

        Grid.SetColumn(pathBox, 0); Grid.SetColumn(choose, 1); Grid.SetColumn(capture, 2);
        pathRow.Children.Add(pathBox); pathRow.Children.Add(choose); pathRow.Children.Add(capture);
        PropertiesPanel.Children.Add(pathRow);

        AddNumberField("Image tolerance (0 exact → 255 loose)", command.ImageTolerance, 0, 255, value => command.ImageTolerance = value);

        AddLabel("Search area");
        var regionText = command.SearchWidth <= 0 || command.SearchHeight <= 0
            ? "Entire virtual screen"
            : $"X {command.SearchX}, Y {command.SearchY}, W {command.SearchWidth}, H {command.SearchHeight}";
        PropertiesPanel.Children.Add(new TextBlock
        {
            Text = regionText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 7),
            Foreground = (Brush)FindResource("TextBrush")
        });

        var regionButtons = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        regionButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        regionButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var setRegion = new Button { Content = "Set Search Area", Margin = new Thickness(0, 0, 4, 0) };
        var fullRegion = new Button { Content = "Use Full Screen", Margin = new Thickness(4, 0, 0, 0) };
        setRegion.Click += (_, _) =>
        {
            var picker = new RegionPickerWindow();
            Hide();
            try
            {
                if (picker.ShowDialog() == true)
                {
                    command.SearchX = picker.Region.X;
                    command.SearchY = picker.Region.Y;
                    command.SearchWidth = picker.Region.Width;
                    command.SearchHeight = picker.Region.Height;
                    MarkDirty();
                }
            }
            finally
            {
                Show();
                WindowState = System.Windows.WindowState.Normal;
                Activate();
            }
            BuildProperties(command);
            RefreshCommandList(command.Id);
        };
        fullRegion.Click += (_, _) =>
        {
            command.SearchX = command.SearchY = command.SearchWidth = command.SearchHeight = 0;
            MarkDirty();
            BuildProperties(command);
            RefreshCommandList(command.Id);
        };
        Grid.SetColumn(setRegion, 0); Grid.SetColumn(fullRegion, 1);
        regionButtons.Children.Add(setRegion); regionButtons.Children.Add(fullRegion);
        PropertiesPanel.Children.Add(regionButtons);

        var test = new Button { Content = "Test Image Detection", Margin = new Thickness(0, 0, 0, 12) };
        test.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(command.ImagePath) || !File.Exists(command.ImagePath))
            {
                MessageBox.Show(this, "Choose an image first.", "Image Detection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var previous = WindowState;
            WindowState = System.Windows.WindowState.Minimized;
            await Task.Delay(180);
            ImageMatch? match = null;
            Exception? error = null;
            try
            {
                match = await ImageMatcher.FindAsync(command, CancellationToken.None);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            WindowState = previous == System.Windows.WindowState.Minimized ? System.Windows.WindowState.Normal : previous;
            Activate();

            if (error is not null)
            {
                MessageBox.Show(this, error.Message, "Image Detection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else if (match.HasValue)
            {
                MessageBox.Show(this,
                    $"FOUND\nTop-left: {match.Value.X}, {match.Value.Y}\nCenter: {match.Value.CenterX}, {match.Value.CenterY}\nSize: {match.Value.Width} × {match.Value.Height}",
                    "Image Detection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this,
                    "Image was not found. Try a tighter search area or increase tolerance a little.",
                    "Image Detection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        PropertiesPanel.Children.Add(test);
        AddInfo("Tip: crop the reference image tightly. Smaller search areas make detection much faster.");
    }

    private void AddPollingFields(MacroCommand command)
    {
        AddNumberField("Poll every (ms)", command.PollMs, 10, 5000, value => command.PollMs = value);
        AddNumberField("Timeout (ms, 0 = forever)", command.TimeoutMs, 0, 86_400_000, value => command.TimeoutMs = value);
    }

    private void AddBlockInfo(MacroCommand command)
    {
        AddInfo(command.HasElse
            ? "Use + Add Inside for THEN commands and + Add Else for ELSE commands."
            : "Use + Add Inside to place commands inside this loop.");
    }

    private void AddSequencePicker(MacroCommand command)
    {
        AddLabel("Sequence to run");
        var combo = new ComboBox
        {
            ItemsSource = _project.Sequences.Select(s => s.Name).ToList(),
            SelectedItem = command.TargetSequence,
            Margin = new Thickness(0, 0, 0, 12)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string name)
            {
                command.TargetSequence = name;
                MarkDirtyAndRefresh(command.Id);
            }
        };
        PropertiesPanel.Children.Add(combo);
        AddInfo("Sequences work like reusable functions/tabs. A sequence can call another sequence.");
    }

    private void AddNumberField(string label, int currentValue, int min, int max, Action<int> setter)
    {
        AddLabel(label);
        var box = new TextBox { Text = currentValue.ToString(), Margin = new Thickness(0, 0, 0, 12) };
        box.LostFocus += (_, _) =>
        {
            if (!int.TryParse(box.Text, out var value))
            {
                box.Text = currentValue.ToString();
                return;
            }
            value = Math.Clamp(value, min, max);
            setter(value);
            box.Text = value.ToString();
            MarkDirtyAndRefresh(_selectedRow?.Command?.Id);
        };
        PropertiesPanel.Children.Add(box);
    }

    private void AddTextField(string label, string currentValue, Action<string> setter, bool multiline = false)
    {
        AddLabel(label);
        var box = new TextBox
        {
            Text = currentValue,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 80 : 0,
            Margin = new Thickness(0, 0, 0, 12)
        };
        box.LostFocus += (_, _) =>
        {
            setter(box.Text);
            MarkDirtyAndRefresh(_selectedRow?.Command?.Id);
        };
        PropertiesPanel.Children.Add(box);
    }

    private void AddReadOnlyField(string label, string value)
    {
        AddLabel(label);
        PropertiesPanel.Children.Add(new TextBlock
        {
            Text = value,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 14)
        });
    }

    private void AddInfo(string text)
    {
        // Intentionally hidden in the cleaner UI.
    }

    private void AddLabel(string text)
    {
        PropertiesPanel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = (Brush)FindResource("MutedTextBrush"),
            Margin = new Thickness(0, 0, 0, 5)
        });
    }

    private void ClearProperties(string hint)
    {
        PropertiesPanel.Children.Clear();
        PropertiesHintText.Text = hint;
    }

    private void MarkDirtyAndRefresh(Guid? selectedId)
    {
        MarkDirty();
        RefreshCommandList(selectedId);
    }

    private static string NormalizeColor(string value)
    {
        var text = value.Trim().ToUpperInvariant();
        if (text.StartsWith('#'))
            text = text[1..];
        if (text.StartsWith("0X"))
            text = text[2..];

        if (text.Length > 6)
            text = text[^6..];
        text = text.PadLeft(6, '0');
        return "0x" + text;
    }

    private static string FriendlyName(CommandType type) => type switch
    {
        CommandType.Comment => "Comment",
        CommandType.MoveMouse => "Move Mouse",
        CommandType.Click => "Click Location",
        CommandType.DoubleClick => "Double Click Location",
        CommandType.RightClick => "Right Click Location",
        CommandType.Scroll => "Scroll",
        CommandType.DragMouse => "Drag Mouse",
        CommandType.LeftMouseDown => "Left Mouse Down",
        CommandType.LeftMouseUp => "Left Mouse Up",
        CommandType.RightMouseDown => "Right Mouse Down",
        CommandType.RightMouseUp => "Right Mouse Up",
        CommandType.PressKey => "Press Key / Combo",
        CommandType.KeyDown => "Key Down",
        CommandType.KeyUp => "Key Up",
        CommandType.TypeText => "Type Text",
        CommandType.HoldKey => "Hold Key",
        CommandType.RepeatKey => "Repeat Key",
        CommandType.WaitUntilKeyPressed => "Wait Until Key Pressed",
        CommandType.Wait => "Wait",
        CommandType.RandomWait => "Random Wait",
        CommandType.RecordedActions => "Record Actions",
        CommandType.IfColor => "IF Color",
        CommandType.WaitUntilColor => "Wait Until Color at Location",
        CommandType.LoopWhileColor => "Loop While Color",
        CommandType.LoopUntilColor => "Loop Until Color",
        CommandType.ClickColor => "Find + Click Color",
        CommandType.IfImage => "IF Image Found",
        CommandType.WaitUntilImage => "Wait Until Image",
        CommandType.WaitUntilImageGone => "Wait Until Image Gone",
        CommandType.ClickImage => "Find + Click Image",
        CommandType.DoubleClickImage => "Find + Double Click Image",
        CommandType.MoveToImage => "Move To Image",
        CommandType.LoopUntilImage => "Loop Until Image",
        CommandType.FocusWindow => "Focus Window",
        CommandType.WaitForWindow => "Wait For Window",
        CommandType.WaitForWindowGone => "Wait For Window Gone",
        CommandType.RunProgram => "Open Program / File / URL",
        CommandType.RunSequence => "Run Sequence",
        CommandType.LoopTimes => "Loop X Times",
        CommandType.LoopForever => "Loop Forever",
        CommandType.Break => "Break Loop",
        CommandType.Return => "Return",
        CommandType.StopMacro => "Stop Macro",
        _ => type.ToString()
    };

    // ---------------- RUNNER ----------------

    private void RebuildEngine()
    {
        _engine?.Stop();
        _engine = new MacroEngine(_project);
        _engine.StatusChanged += text => Dispatcher.InvokeAsync(() => StatusText.Text = text);
        _engine.StateChanged += () => Dispatcher.InvokeAsync(UpdateRunButtons);
        _engine.CommandStarted += (sequence, id) => Dispatcher.InvokeAsync(() =>
        {
            var command = EnumerateAllCommands().FirstOrDefault(c => c.Id == id);
            StatusText.Text = command is null ? $"Running: {sequence}" : $"{sequence}: {command.DisplayText()}";
        });
        UpdateRunButtons();
    }

    private async void RunStartButton_Click(object sender, RoutedEventArgs e)
    {
        await RunSequenceFromUi("Starting Sequence");
    }

    private async void RunCurrentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSequence is not null)
            await RunSequenceFromUi(_currentSequence.Name);
    }

    private async Task RunSequenceFromUi(string name)
    {
        if (_engine is null || _engine.IsRunning)
            return;

        Exception? error = null;
        Hide();
        try
        {
            await Task.Delay(120);
            await _engine.StartAsync(name);
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            Show();
            WindowState = System.Windows.WindowState.Normal;
            Activate();
        }

        if (error is not null)
            MessageBox.Show(this, error.Message, "Macro stopped", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        _engine?.TogglePause();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _engine?.Stop();
    }

    private void UpdateRunButtons()
    {
        var running = _engine?.IsRunning == true;
        RunStartButton.IsEnabled = !running && !_isRecording;
        RunCurrentButton.IsEnabled = !running && !_isRecording;
        StopButton.IsEnabled = running;
        PauseButton.IsEnabled = running;
        PauseButton.Content = _engine?.IsPaused == true ? "▶ Resume" : "⏸ Pause";
    }

    // ---------------- NON-BLOCKING GLOBAL F8/F9 ----------------

    private void InstallKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero)
            return;

        _keyboardProc = KeyboardHookCallback;
        var module = NativeMethods.GetModuleHandle(null);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam.ToInt32() == NativeMethods.WM_KEYDOWN || wParam.ToInt32() == NativeMethods.WM_SYSKEYDOWN))
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var injected = (data.flags & 0x10) != 0;
            if (!injected)
            {
                if (data.vkCode == 0x77) // F8
                    Dispatcher.InvokeAsync(() => _engine?.Stop());
                else if (data.vkCode == 0x78) // F9
                    Dispatcher.InvokeAsync(() => _engine?.TogglePause());
            }
        }

        // Always pass the key through. F8/F9 remain usable in other programs.
        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IEnumerable<MacroCommand> EnumerateAllCommands()
    {
        foreach (var sequence in _project.Sequences)
        {
            foreach (var command in EnumerateCommands(sequence.Commands))
                yield return command;
        }
    }

    private static IEnumerable<MacroCommand> EnumerateCommands(IEnumerable<MacroCommand> commands)
    {
        foreach (var command in commands)
        {
            yield return command;
            foreach (var child in EnumerateCommands(command.Children))
                yield return child;
            foreach (var child in EnumerateCommands(command.ElseChildren))
                yield return child;
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _engine?.Stop();

        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }

        _foregroundTimer.Stop();

        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
    }
}
