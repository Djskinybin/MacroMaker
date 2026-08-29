using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
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
    private const string ProjectFileName = "macro.json";
    private static readonly string RecoveryFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacroMaker", "Recovery", "unsaved-recovery.json");
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
    private readonly HashSet<uint> _hookKeysDown = new();

    private IntPtr _mouseLockHook;
    private NativeMethods.LowLevelMouseProc? _mouseLockProc;
    private RunStatusWindow? _runStatusWindow;

    private readonly DispatcherTimer _foregroundTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private IntPtr _mainWindowHandle;
    private IntPtr _lastExternalForeground;
    private bool _startupUpdateCheckStarted;
    private bool _welcomeShown;
    private bool _skipSavePromptForUpdate;

    private readonly Stack<string> _undoHistory = new();
    private readonly Stack<string> _redoHistory = new();
    private string _historySnapshot = string.Empty;
    private bool _historySuspended;
    private readonly DispatcherTimer _autoSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(1400) };
    private readonly HashSet<Guid> _collapsedBlocks = new();
    private readonly HashSet<Guid> _expandedMoreOptions = new();
    private readonly HashSet<Guid> _expandedLocationOptions = new();
    private readonly HashSet<Guid> _expandedTimingOptions = new();
    private bool _rebuildingProperties;
    private bool _refreshingCommandSelection;
    private Point _commandDragStartPoint;
    private CommandRow? _commandDragStartRow;
    private bool _commandDragInProgress;
    private const string CommandDragDataFormat = "MacroMaker.CommandRows";

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
        WindowTheme.Attach(this);
        NewProjectCore();
        RefreshQuickAdd();
        Loaded += MainWindow_Loaded;
        ContentRendered += MainWindow_ContentRendered;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        TryOfferUnsavedRecovery();
        InstallKeyboardHook();
        _mainWindowHandle = new WindowInteropHelper(this).Handle;
        _foregroundTimer.Tick += (_, _) => TrackExternalForegroundWindow();
        _foregroundTimer.Start();

        if (!_welcomeShown && string.IsNullOrWhiteSpace(_projectPath) && !_dirty)
        {
            _welcomeShown = true;
            Dispatcher.BeginInvoke(new Action(ShowWelcomeWindow), DispatcherPriority.ContextIdle);
        }
    }

    private void ShowWelcomeWindow()
    {
        if (_engine?.IsRunning == true || _isRecording)
            return;

        var welcome = new WelcomeWindow(_appSettings.RecentProjects) { Owner = this };
        if (welcome.ShowDialog() != true)
            return;

        if (welcome.Action == WelcomeAction.OpenFolder)
        {
            OpenProject_Click(this, new RoutedEventArgs());
        }
        else if (welcome.Action == WelcomeAction.OpenRecent && !string.IsNullOrWhiteSpace(welcome.SelectedProjectPath))
        {
            TryOpenProjectFolder(welcome.SelectedProjectPath);
        }
    }

    private async void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        if (_startupUpdateCheckStarted || !_appSettings.CheckForUpdatesOnStartup)
            return;

        _startupUpdateCheckStarted = true;

        // Wait until the welcome screen is closed, then silently check GitHub.
        while (OwnedWindows.OfType<WelcomeWindow>().Any(window => window.IsVisible))
            await Task.Delay(200);
        await Task.Delay(350);
        await UpdateService.CheckAndPromptAsync(this, false);
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

        DeleteUnsavedRecovery();
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
        ProjectPaths.CurrentFolder = null;
        _dirty = false;
        _collapsedBlocks.Clear();
        _sequenceCounter = 1;
        RebuildEngine();
        SelectSequence(_project.Sequences[0]);
        RefreshTabs();
        UpdateProjectTitle();
        StatusText.Text = "Ready";
        ResetHistory();
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges())
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Open Macro Maker Project Folder",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        TryOpenProjectFolder(dialog.FolderName, skipDiscardPrompt: true);
    }

    private bool TryOpenProjectFolder(string folder, bool skipDiscardPrompt = false)
    {
        if (!skipDiscardPrompt && !ConfirmDiscardChanges())
            return false;

        try
        {
            var projectFile = FindProjectFile(folder);
            if (projectFile is null)
                throw new FileNotFoundException(
                    $"That folder is not a MacroMaker project. Expected '{ProjectFileName}' inside it.\n\n" +
                    "Tip: for an older .macro.json save, put that file inside its own folder and open the folder.");

            var json = File.ReadAllText(projectFile);
            var project = JsonSerializer.Deserialize<MacroProject>(json, JsonOptions)
                          ?? throw new InvalidOperationException("The project file was empty.");

            project.Sequences ??= new List<MacroSequence>();
            project.RecorderSettings ??= new RecorderSettings();
            project.Variables ??= new List<ProjectVariable>();
            project.RuntimeSettings ??= new MacroRuntimeSettings();
            project.RuntimeSettings.PlaybackSpeedPercent = Math.Clamp(project.RuntimeSettings.PlaybackSpeedPercent, 10, 400);
            project.RuntimeSettings.HudOpacityPercent = Math.Clamp(project.RuntimeSettings.HudOpacityPercent, 35, 100);
            if (project.Sequences.Count == 0)
                project.Sequences.Add(new MacroSequence("Starting Sequence"));

            var starting = project.Sequences.FirstOrDefault(sequence =>
                sequence.Name.Equals("Starting Sequence", StringComparison.OrdinalIgnoreCase));
            if (starting is null)
                project.Sequences.Insert(0, new MacroSequence("Starting Sequence"));

            foreach (var sequence in project.Sequences)
            {
                sequence.Commands ??= new List<MacroCommand>();
                RepairCommandLists(sequence.Commands);
            }

            _project = project;
            _projectPath = folder;
            ProjectPaths.CurrentFolder = folder;
            Directory.CreateDirectory(Path.Combine(folder, "Images"));
            _dirty = false;
            _collapsedBlocks.Clear();
            DeleteUnsavedRecovery();
            RebuildEngine();
            SelectSequence(_project.Sequences.First(sequence =>
                sequence.Name.Equals("Starting Sequence", StringComparison.OrdinalIgnoreCase)));
            RefreshTabs();
            UpdateProjectTitle();
            StatusText.Text = $"Project loaded: {Path.GetFileName(folder)}";
            ResetHistory();
            RememberRecentProject(folder);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, FriendlyErrorMessage(ex, "opening this project"), "Could not open project", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void RememberRecentProject(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        _appSettings.RecentProjects ??= new List<string>();
        _appSettings.RecentProjects.RemoveAll(path => path.Equals(folder, StringComparison.OrdinalIgnoreCase));
        _appSettings.RecentProjects.Insert(0, folder);
        if (_appSettings.RecentProjects.Count > 8)
            _appSettings.RecentProjects.RemoveRange(8, _appSettings.RecentProjects.Count - 8);
        AppSettingsStore.Save(_appSettings);
    }

    private static string? FindProjectFile(string folder)
    {
        var standard = Path.Combine(folder, ProjectFileName);
        if (File.Exists(standard))
            return standard;

        // Backward compatibility: an old standalone .macro.json can be placed
        // inside a folder and that folder can then be opened as a project.
        return Directory.EnumerateFiles(folder, "*.macro.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static void RepairCommandLists(List<MacroCommand> commands)
    {
        foreach (var command in commands)
        {
            // V1/V1.2 stored smooth movement only as a non-zero duration.
            if (command.MouseMoveMode == MouseMoveMode.Legacy)
                command.MouseMoveMode = command.MoveDurationMs > 0 ? MouseMoveMode.Smooth : MouseMoveMode.Teleport;

            command.ImagePriority ??= new List<string>();
            command.Children ??= new List<MacroCommand>();
            command.ElseChildren ??= new List<MacroCommand>();
            command.FailureRetryCount = Math.Clamp(command.FailureRetryCount, 0, 100);
            command.FailureRetryDelayMs = Math.Clamp(command.FailureRetryDelayMs, 0, 60000);
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

    private bool SaveProject(bool forceDialog, Window? dialogOwner = null)
    {
        var owner = dialogOwner ?? this;
        var previousProjectPath = _projectPath;

        if (forceDialog || string.IsNullOrWhiteSpace(_projectPath))
        {
            var defaultName = _project.Name == "Untitled Macro" ? "My Macro" : _project.Name;
            var namePrompt = new TextPromptWindow("Save Macro As", "Macro name:", defaultName) { Owner = owner };
            if (namePrompt.ShowDialog() != true)
                return false;

            var displayName = namePrompt.Value.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = "My Macro";
            var folderName = SanitizeFileName(displayName);

            var dialog = new OpenFolderDialog
            {
                Title = $"Choose where to save '{folderName}'",
                Multiselect = false
            };

            if (dialog.ShowDialog(owner) != true)
                return false;

            var targetFolder = Path.Combine(dialog.FolderName, folderName);
            var existingProject = Path.Combine(targetFolder, ProjectFileName);
            if (File.Exists(existingProject) &&
                MessageBox.Show(owner,
                    $"A MacroMaker project named '{folderName}' already exists there. Replace its macro.json?\n\nImages already in that project will be kept.",
                    "Existing Project", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return false;

            _projectPath = targetFolder;
            _project.Name = displayName;
            Directory.CreateDirectory(_projectPath);
            Directory.CreateDirectory(Path.Combine(_projectPath, "Images"));

            // Save As copies the project's image library with it, so all
            // relative image/folder references continue to work in the new folder.
            if (!string.IsNullOrWhiteSpace(previousProjectPath) &&
                !Path.GetFullPath(previousProjectPath).Equals(Path.GetFullPath(_projectPath), StringComparison.OrdinalIgnoreCase))
            {
                var previousImages = Path.Combine(previousProjectPath, "Images");
                if (Directory.Exists(previousImages))
                    CopyImageFolderRecursive(previousImages, Path.Combine(_projectPath, "Images"));
            }

            ProjectPaths.CurrentFolder = _projectPath;
        }

        try
        {
            Directory.CreateDirectory(_projectPath!);
            Directory.CreateDirectory(Path.Combine(_projectPath!, "Images"));
            ProjectPaths.CurrentFolder = _projectPath;

            var json = JsonSerializer.Serialize(_project, JsonOptions);
            File.WriteAllText(Path.Combine(_projectPath!, ProjectFileName), json);
            _dirty = false;
            DeleteUnsavedRecovery();
            UpdateProjectTitle();
            StatusText.Text = $"Saved: {Path.GetFileName(_projectPath)}";
            RememberRecentProject(_projectPath!);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Could not save project", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private bool EnsureProjectFolder()
    {
        if (!string.IsNullOrWhiteSpace(_projectPath))
            return true;

        MessageBox.Show(this,
            "Save this macro as a project folder first. MacroMaker keeps images inside the project so it can be moved or shared without broken paths.",
            "Save Project Folder", MessageBoxButton.OK, MessageBoxImage.Information);
        return SaveProject(true);
    }

    private string ProjectImagesFolder()
    {
        if (string.IsNullOrWhiteSpace(_projectPath))
            throw new InvalidOperationException("Save the project folder first.");
        var folder = Path.Combine(_projectPath, "Images");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static bool IsInsideFolder(string path, string folder)
    {
        try
        {
            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private string ImportImageFile(string sourcePath)
    {
        var images = ProjectImagesFolder();
        if (IsInsideFolder(sourcePath, images))
            return ProjectPaths.MakeRelative(sourcePath);

        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        var destination = Path.Combine(images, baseName + ext);
        var suffix = 2;
        while (File.Exists(destination) && !FilesAreSame(sourcePath, destination))
            destination = Path.Combine(images, $"{baseName}_{suffix++}{ext}");

        if (!File.Exists(destination))
            File.Copy(sourcePath, destination);
        return ProjectPaths.MakeRelative(destination);
    }

    private string ImportImageFolder(string sourceFolder)
    {
        var imagesRoot = ProjectImagesFolder();
        if (IsInsideFolder(sourceFolder, imagesRoot))
            return ProjectPaths.MakeRelative(sourceFolder);

        var folderName = SanitizeFileName(Path.GetFileName(sourceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        var destination = Path.Combine(imagesRoot, folderName);
        var suffix = 2;
        while (Directory.Exists(destination))
            destination = Path.Combine(imagesRoot, $"{folderName}_{suffix++}");

        CopyImageFolderRecursive(sourceFolder, destination);
        return ProjectPaths.MakeRelative(destination);
    }

    private static void CopyImageFolderRecursive(string sourceFolder, string destinationFolder)
    {
        Directory.CreateDirectory(destinationFolder);

        foreach (var source in Directory.EnumerateFiles(sourceFolder, "*.*", SearchOption.TopDirectoryOnly).Where(IsImageFile))
            File.Copy(source, Path.Combine(destinationFolder, Path.GetFileName(source)), overwrite: true);

        foreach (var sourceSubfolder in Directory.EnumerateDirectories(sourceFolder, "*", SearchOption.TopDirectoryOnly))
        {
            var destinationSubfolder = Path.Combine(destinationFolder, Path.GetFileName(sourceSubfolder));
            CopyImageFolderRecursive(sourceSubfolder, destinationSubfolder);

            // Do not leave empty directories behind if a source subfolder had no supported images.
            if (Directory.Exists(destinationSubfolder) && !Directory.EnumerateFileSystemEntries(destinationSubfolder).Any())
                Directory.Delete(destinationSubfolder);
        }
    }

    private static bool IsImageFile(string path)
        => new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool FilesAreSame(string first, string second)
    {
        try
        {
            return Path.GetFullPath(first).Equals(Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void SyncImagePriority(MacroCommand command)
    {
        command.ImagePriority ??= new List<string>();
        if (string.IsNullOrWhiteSpace(command.ImageFolder))
            return;

        var folder = ProjectPaths.Resolve(command.ImageFolder);
        if (!Directory.Exists(folder))
            return;

        // Priorities are stored relative to the selected image folder so nested
        // folders remain portable and duplicate file names are safe.
        var searchOption = command.ImageIncludeSubfolders
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        var names = Directory.EnumerateFiles(folder, "*.*", searchOption)
            .Where(IsImageFile)
            .Select(path => Path.GetRelativePath(folder, path))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        command.ImagePriority.RemoveAll(saved => !names.Contains(saved, StringComparer.OrdinalIgnoreCase));
        foreach (var name in names)
        {
            if (!command.ImagePriority.Contains(name, StringComparer.OrdinalIgnoreCase))
                command.ImagePriority.Add(name);
        }
    }

    private sealed record ProjectImageAsset(string Label, string RelativePath, bool IsFolder)
    {
        public override string ToString() => Label;
    }

    private List<ProjectImageAsset> GetProjectImageAssets()
    {
        var result = new List<ProjectImageAsset>();
        if (string.IsNullOrWhiteSpace(_projectPath))
            return result;

        var imagesRoot = ProjectImagesFolder();
        if (!Directory.Exists(imagesRoot))
            return result;

        // Every imported image can be reused by any image command.
        foreach (var file in Directory.EnumerateFiles(imagesRoot, "*.*", SearchOption.AllDirectories)
                     .Where(IsImageFile)
                     .OrderBy(path => Path.GetRelativePath(imagesRoot, path), StringComparer.OrdinalIgnoreCase))
        {
            var relativeToImages = Path.GetRelativePath(imagesRoot, file);
            result.Add(new ProjectImageAsset($"Image  •  {relativeToImages}", ProjectPaths.MakeRelative(file), false));
        }

        // Any folder containing images is also reusable as a priority source.
        foreach (var folder in Directory.EnumerateDirectories(imagesRoot, "*", SearchOption.AllDirectories)
                     .Prepend(imagesRoot)
                     .Where(folder => Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories).Any(IsImageFile))
                     .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase))
        {
            // The Images root itself is useful only when it has images directly in it.
            if (Path.GetFullPath(folder).Equals(Path.GetFullPath(imagesRoot), StringComparison.OrdinalIgnoreCase) &&
                !Directory.EnumerateFiles(imagesRoot, "*.*", SearchOption.TopDirectoryOnly).Any(IsImageFile))
                continue;

            var display = Path.GetFullPath(folder).Equals(Path.GetFullPath(imagesRoot), StringComparison.OrdinalIgnoreCase)
                ? "Images"
                : Path.GetRelativePath(imagesRoot, folder);
            var imageCount = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories).Count(IsImageFile);
            result.Add(new ProjectImageAsset($"Folder • {display} ({imageCount})", ProjectPaths.MakeRelative(folder), true));
        }

        return result;
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

        if (result == MessageBoxResult.Yes)
            return SaveProject(false);
        if (result == MessageBoxResult.No)
        {
            DeleteUnsavedRecovery();
            return true;
        }
        return false;
    }

    internal bool TryPrepareForUpdate(Window promptOwner, out bool discardUnsavedChanges)
    {
        discardUnsavedChanges = false;
        if (!_dirty)
            return true;

        var message = string.IsNullOrWhiteSpace(_projectPath)
            ? "This macro has not been saved yet. Save it before updating MacroMaker?\n\nChoosing No will update MacroMaker and discard this unsaved macro."
            : "This macro has unsaved changes. Save them before updating MacroMaker?\n\nChoosing No will update MacroMaker and discard the unsaved changes.";

        var result = MessageBox.Show(promptOwner, message, "Save Before Update",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
            return false;

        if (result == MessageBoxResult.Yes)
            return SaveProject(false, promptOwner);

        discardUnsavedChanges = true;
        DeleteUnsavedRecovery();
        return true;
    }

    internal void AllowUpdateShutdownWithoutSavePrompt()
    {
        _skipSavePromptForUpdate = true;
    }

    private void MarkDirty()
    {
        TrackHistoryChange();

        if (!_dirty)
        {
            _dirty = true;
            UpdateProjectTitle();
        }

        if (_appSettings.AutoSaveProjectChanges)
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }
    }

    private string SerializeProjectSnapshot() => JsonSerializer.Serialize(_project, JsonOptions);

    private void ResetHistory()
    {
        _undoHistory.Clear();
        _redoHistory.Clear();
        _historySnapshot = SerializeProjectSnapshot();
        UpdateUndoButtons();
    }

    private void TrackHistoryChange()
    {
        if (_historySuspended)
            return;

        var current = SerializeProjectSnapshot();
        if (string.Equals(current, _historySnapshot, StringComparison.Ordinal))
            return;

        if (!string.IsNullOrEmpty(_historySnapshot))
            _undoHistory.Push(_historySnapshot);

        _redoHistory.Clear();
        _historySnapshot = current;
        UpdateUndoButtons();
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e) => UndoProjectChange();
    private void RedoButton_Click(object sender, RoutedEventArgs e) => RedoProjectChange();

    private void UndoProjectChange()
    {
        if (_undoHistory.Count == 0 || _engine?.IsRunning == true || _isRecording)
            return;

        var current = SerializeProjectSnapshot();
        var previous = _undoHistory.Pop();
        _redoHistory.Push(current);
        ApplyHistorySnapshot(previous);
        StatusText.Text = "Undo";
    }

    private void RedoProjectChange()
    {
        if (_redoHistory.Count == 0 || _engine?.IsRunning == true || _isRecording)
            return;

        var current = SerializeProjectSnapshot();
        var next = _redoHistory.Pop();
        _undoHistory.Push(current);
        ApplyHistorySnapshot(next);
        StatusText.Text = "Redo";
    }

    private void ApplyHistorySnapshot(string json)
    {
        var selectedSequenceName = _currentSequence?.Name ?? "Starting Sequence";
        _historySuspended = true;
        try
        {
            var restored = JsonSerializer.Deserialize<MacroProject>(json, JsonOptions);
            if (restored is null)
                return;
            restored.Sequences ??= new List<MacroSequence>();
            foreach (var sequence in restored.Sequences)
            {
                sequence.Commands ??= new List<MacroCommand>();
                RepairCommandLists(sequence.Commands);
            }
            _project = restored;
            _historySnapshot = json;
            _dirty = true;
            RebuildEngine();
            var sequenceToSelect = _project.Sequences.FirstOrDefault(x => x.Name.Equals(selectedSequenceName, StringComparison.OrdinalIgnoreCase))
                                   ?? _project.Sequences.FirstOrDefault()
                                   ?? new MacroSequence("Starting Sequence");
            if (_project.Sequences.Count == 0)
                _project.Sequences.Add(sequenceToSelect);
            SelectSequence(sequenceToSelect);
            RefreshTabs();
            UpdateProjectTitle();
            UpdateUndoButtons();
            if (_appSettings.AutoSaveProjectChanges)
            {
                _autoSaveTimer.Stop();
                _autoSaveTimer.Start();
            }
        }
        finally
        {
            _historySuspended = false;
        }
    }

    private void UpdateUndoButtons()
    {
        if (UndoButton is not null) UndoButton.IsEnabled = _undoHistory.Count > 0 && _engine?.IsRunning != true;
        if (RedoButton is not null) RedoButton.IsEnabled = _redoHistory.Count > 0 && _engine?.IsRunning != true;
    }

    private async void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        _autoSaveTimer.Stop();
        if (!_appSettings.AutoSaveProjectChanges || !_dirty || _engine?.IsRunning == true || _isRecording)
            return;

        try
        {
            var json = SerializeProjectSnapshot();
            if (string.IsNullOrWhiteSpace(_projectPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecoveryFilePath)!);
                await File.WriteAllTextAsync(RecoveryFilePath, json);
                StatusText.Text = "Unsaved work backed up";
                return;
            }

            await File.WriteAllTextAsync(Path.Combine(_projectPath!, ProjectFileName), json);
            _dirty = false;
            UpdateProjectTitle();
            StatusText.Text = "Auto-saved";
        }
        catch
        {
            // Auto-save/recovery should never interrupt editing. Manual Save still reports errors.
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var control = (modifiers & ModifierKeys.Control) != 0;
        var shift = (modifiers & ModifierKeys.Shift) != 0;

        // Saving should work even while the user is typing in a property field.
        if (control && e.Key == Key.S)
        {
            SaveProject(shift);
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is TextBox or ComboBox)
            return;

        if (control && e.Key == Key.Z && !shift)
        {
            UndoProjectChange();
            e.Handled = true;
        }
        else if (control && (e.Key == Key.Y || (shift && e.Key == Key.Z)))
        {
            RedoProjectChange();
            e.Handled = true;
        }
        else if (control && e.Key == Key.C)
        {
            CopyCommandButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (control && e.Key == Key.V)
        {
            PasteCommandButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (control && e.Key == Key.D)
        {
            DuplicateCommandButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (control && e.Key == Key.Enter)
        {
            TestSelectedButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteCommandButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void TryOfferUnsavedRecovery()
    {
        try
        {
            if (!File.Exists(RecoveryFilePath))
                return;
            var json = File.ReadAllText(RecoveryFilePath);
            var recovered = JsonSerializer.Deserialize<MacroProject>(json, JsonOptions);
            if (recovered?.Sequences is null || recovered.Sequences.Count == 0)
            {
                DeleteUnsavedRecovery();
                return;
            }

            var answer = MessageBox.Show(this,
                "MacroMaker found an unsaved macro from a previous session. Recover it?",
                "Recover Unsaved Macro", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                DeleteUnsavedRecovery();
                return;
            }

            recovered.RecorderSettings ??= new RecorderSettings();
            recovered.Variables ??= new List<ProjectVariable>();
            recovered.RuntimeSettings ??= new MacroRuntimeSettings();
            foreach (var sequence in recovered.Sequences)
            {
                sequence.Commands ??= new List<MacroCommand>();
                RepairCommandLists(sequence.Commands);
            }
            _project = recovered;
            _projectPath = null;
            ProjectPaths.CurrentFolder = null;
            _dirty = true;
            _collapsedBlocks.Clear();
            RebuildEngine();
            var startupSequence = _project.Sequences.FirstOrDefault(x => x.Name.Equals("Starting Sequence", StringComparison.OrdinalIgnoreCase))
                                  ?? _project.Sequences[0];
            SelectSequence(startupSequence);
            RefreshTabs();
            UpdateProjectTitle();
            ResetHistory();
            StatusText.Text = "Recovered unsaved macro";
        }
        catch
        {
            DeleteUnsavedRecovery();
        }
    }

    private static void DeleteUnsavedRecovery()
    {
        try
        {
            if (File.Exists(RecoveryFilePath)) File.Delete(RecoveryFilePath);
        }
        catch { }
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
        if (!_appSettings.AutoSaveProjectChanges)
            _autoSaveTimer.Stop();
        else if (_dirty)
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }
        ThemeManager.Apply(_appSettings.Theme);
        if (_engine is not null)
            _engine.PlaybackSpeedPercent = EffectivePlaybackSpeed;
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
            var duration = 50;
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
                ColorHex = m.Groups[1].Success ? NormalizeColor(m.Groups[1].Value) : "0x000000",
                X = m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var ifx) ? ifx : 0,
                Y = m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out var ify) ? ify : 0,
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
            StatusText.Text = "Select an IF, loop, group, THEN/ELSE row, or a command already inside one";
            return;
        }

        OpenCommandMenu(sender as FrameworkElement, type =>
        {
            var command = CreateCommand(type);
            target.Add(command);
            MarkDirty();
            RefreshCommandList(command.Id);
            StatusText.Text = target == parent.ElseChildren ? "Added to ELSE" : parent.HasElse ? "Added to THEN" : parent.Type == CommandType.Group ? "Added to group" : "Added to loop";
        });
    }

    private void AddElseButton_Click(object sender, RoutedEventArgs e)
    {
        var parent = ResolveIfParent();
        if (parent is null)
        {
            StatusText.Text = "ELSE is available on IF commands";
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
                label = parent.HasElse ? "THEN" : parent.Type == CommandType.Group ? "Group" : "Loop";
            }
            return true;
        }

        if (row.Command is { } selected && selected.HasBody && selected.Type != CommandType.RecordedActions)
        {
            parent = selected;
            target = selected.Children;
            label = selected.HasElse ? "THEN" : selected.Type == CommandType.Group ? "Group" : "Loop";
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
                label = parent.HasElse ? "THEN" : parent.Type == CommandType.Group ? "Group" : "Loop";
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
            AddBlockButton.Visibility = Visibility.Visible;
            AddBlockButton.IsEnabled = true;
            AddBlockButton.Content = label switch
            {
                "THEN" => "+ Add to THEN",
                "ELSE" => "+ Add to ELSE",
                "Group" => "+ Add to Group",
                _ => "+ Add to Loop"
            };
        }
        else
        {
            AddBlockButton.Visibility = Visibility.Collapsed;
            AddBlockButton.IsEnabled = false;
        }

        var ifParent = ResolveIfParent();
        AddElseButton.Visibility = ifParent is null ? Visibility.Collapsed : Visibility.Visible;
        AddElseButton.IsEnabled = ifParent is not null;
    }

    private void OpenCommandMenu(FrameworkElement? target, Action<CommandType> onSelected)
    {
        var picker = new CommandPickerWindow()
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
            CoordinateMode = defaults.CoordinateMode,
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
            ImageIncludeSubfolders = defaults.ImageIncludeSubfolders,
            WindowTitle = defaults.WindowTitle,
            ProgramPath = defaults.ProgramPath,
            ProgramArguments = defaults.ProgramArguments,
            WorkingDirectory = defaults.WorkingDirectory,
            SearchX = defaults.SearchX,
            SearchY = defaults.SearchY,
            SearchWidth = defaults.SearchWidth,
            SearchHeight = defaults.SearchHeight,
            ImageOffsetX = defaults.ImageOffsetX,
            ImageOffsetY = defaults.ImageOffsetY,
            VariableName = defaults.VariableName,
            VariableValue = defaults.VariableValue,
            VariableValue2 = defaults.VariableValue2,
            VariableCompareMode = defaults.VariableCompareMode,
            StoreXVariable = defaults.StoreXVariable,
            StoreYVariable = defaults.StoreYVariable,
            StoreTextVariable = defaults.StoreTextVariable,
            ValueExpressions = defaults.ValueExpressions is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(defaults.ValueExpressions, StringComparer.OrdinalIgnoreCase),
            FilePath = defaults.FilePath,
            AppendFile = defaults.AppendFile,
            PromptText = defaults.PromptText,
            FailureAction = defaults.FailureAction,
            FailureRetryCount = defaults.FailureRetryCount,
            FailureRetryDelayMs = defaults.FailureRetryDelayMs,
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
        var rows = GetSelectedCommandRows();
        if (rows.Count == 0)
            return;

        var clonedIds = new List<Guid>();
        foreach (var group in rows.GroupBy(row => row.Owner!))
        {
            var owner = group.Key;
            var commands = group.Select(row => row.Command!).ToList();
            var insertionIndex = commands.Select(command => owner.IndexOf(command)).DefaultIfEmpty(-1).Max() + 1;
            var clones = commands.Select(command => command.DeepClone()).ToList();
            owner.InsertRange(Math.Clamp(insertionIndex, 0, owner.Count), clones);
            clonedIds.AddRange(clones.Select(command => command.Id));
        }

        MarkDirty();
        RefreshCommandList(clonedIds.FirstOrDefault(), clonedIds);
        StatusText.Text = clonedIds.Count == 1 ? "Command duplicated" : $"{clonedIds.Count} commands duplicated";
    }

    private void CollapseRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CommandRow row } || row.Command is not { } command || !row.IsCollapsible)
            return;

        ToggleCollapsed(command);
        e.Handled = true;
    }

    private void ToggleCollapsed(MacroCommand command)
    {
        if (!command.HasBody || command.Type == CommandType.RecordedActions)
            return;

        if (!_collapsedBlocks.Add(command.Id))
            _collapsedBlocks.Remove(command.Id);

        RefreshCommandList(command.Id);
    }

    private List<CommandRow> GetSelectedCommandRows(bool rootsOnly = true)
    {
        var selectedIds = CommandList.SelectedItems
            .OfType<CommandRow>()
            .Where(row => !row.IsHeader && row.Command is not null && row.Owner is not null)
            .Select(row => row.Command!.Id)
            .ToHashSet();

        if (selectedIds.Count == 0 && _selectedRow?.Command is { } selectedCommand && _selectedRow.Owner is not null)
            selectedIds.Add(selectedCommand.Id);

        var ordered = CommandList.Items
            .OfType<CommandRow>()
            .Where(row => !row.IsHeader && row.Command is not null && row.Owner is not null && selectedIds.Contains(row.Command.Id))
            .ToList();

        if (!rootsOnly || ordered.Count < 2)
            return ordered;

        return ordered
            .Where(row => !ordered.Any(other =>
                other.Command!.Id != row.Command!.Id && CommandContains(other.Command, row.Command.Id)))
            .ToList();
    }

    private static bool CommandContains(MacroCommand root, Guid commandId)
    {
        foreach (var child in root.Children)
        {
            if (child.Id == commandId || CommandContains(child, commandId))
                return true;
        }
        foreach (var child in root.ElseChildren)
        {
            if (child.Id == commandId || CommandContains(child, commandId))
                return true;
        }
        return false;
    }

    private void CommandList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not CommandRow { IsHeader: false, Command: not null } row)
            return;

        if (!item.IsSelected)
        {
            CommandList.UnselectAll();
            item.IsSelected = true;
        }

        ApplyCommandSelection(row);
    }

    private void CommandList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (CommandList.ContextMenu is null)
            return;

        var menu = CommandList.ContextMenu;
        menu.Items.Clear();

        var selected = GetSelectedCommandRows();
        if (_selectedRow?.Command is not { } command || selected.Count == 0)
        {
            e.Handled = true;
            return;
        }

        var running = _engine?.IsRunning == true || _isRecording;

        if (selected.Count == 1 && _selectedRow.IsCollapsible)
        {
            var collapse = new MenuItem
            {
                Header = _collapsedBlocks.Contains(command.Id) ? "Expand Block" : "Collapse Block"
            };
            collapse.Click += (_, _) => ToggleCollapsed(command);
            menu.Items.Add(collapse);
            menu.Items.Add(new Separator());
        }

        var runFromHere = new MenuItem { Header = "Run From Here", IsEnabled = !running };
        runFromHere.Click += RunFromHereButton_Click;
        menu.Items.Add(runFromHere);

        var test = new MenuItem { Header = "Test Command", IsEnabled = !running && selected.Count == 1, InputGestureText = "Ctrl+Enter" };
        test.Click += TestSelectedButton_Click;
        menu.Items.Add(test);
        menu.Items.Add(new Separator());

        var copy = new MenuItem { Header = selected.Count == 1 ? "Copy" : $"Copy {selected.Count} Commands", InputGestureText = "Ctrl+C" };
        copy.Click += CopyCommandButton_Click;
        menu.Items.Add(copy);

        var paste = new MenuItem { Header = "Paste", IsEnabled = _copiedCommands.Count > 0, InputGestureText = "Ctrl+V" };
        paste.Click += PasteCommandButton_Click;
        menu.Items.Add(paste);

        var duplicate = new MenuItem { Header = selected.Count == 1 ? "Duplicate Command" : $"Duplicate {selected.Count} Commands", InputGestureText = "Ctrl+D" };
        duplicate.Click += DuplicateCommandButton_Click;
        menu.Items.Add(duplicate);
        menu.Items.Add(new Separator());

        var allEnabled = selected.All(row => row.Command!.Enabled);
        var enabled = new MenuItem { Header = allEnabled ? (selected.Count == 1 ? "Disable Command" : "Disable Commands") : (selected.Count == 1 ? "Enable Command" : "Enable Commands") };
        enabled.Click += (_, _) => SetSelectedCommandsEnabled(!allEnabled);
        menu.Items.Add(enabled);

        var delete = new MenuItem { Header = selected.Count == 1 ? "Delete Command" : $"Delete {selected.Count} Commands", InputGestureText = "Del" };
        delete.Click += DeleteCommandButton_Click;
        menu.Items.Add(delete);
    }

    private void SetSelectedCommandsEnabled(bool enabled)
    {
        var rows = GetSelectedCommandRows();
        if (rows.Count == 0)
            return;

        foreach (var row in rows)
            row.Command!.Enabled = enabled;

        var ids = rows.Select(row => row.Command!.Id).ToList();
        MarkDirty();
        RefreshCommandList(_selectedRow?.Command?.Id, ids);
        StatusText.Text = enabled
            ? (rows.Count == 1 ? "Command enabled" : $"{rows.Count} commands enabled")
            : (rows.Count == 1 ? "Command disabled" : $"{rows.Count} commands disabled");
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void CommandList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _commandDragStartPoint = e.GetPosition(CommandList);
        _commandDragStartRow = null;

        if (FindVisualParent<Button>(e.OriginalSource as DependencyObject) is not null)
            return;

        var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is CommandRow { IsHeader: true })
        {
            e.Handled = true;
            return;
        }
        if (item?.DataContext is CommandRow { IsHeader: false, Command: not null } row)
            _commandDragStartRow = row;
    }

    private void CommandList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_commandDragInProgress || _commandDragStartRow?.Command is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var point = e.GetPosition(CommandList);
        if (Math.Abs(point.X - _commandDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _commandDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (!CommandList.SelectedItems.OfType<CommandRow>().Any(row => row.Command?.Id == _commandDragStartRow.Command.Id))
        {
            CommandList.UnselectAll();
            var item = CommandList.ItemContainerGenerator.ContainerFromItem(_commandDragStartRow) as ListBoxItem;
            if (item is not null)
                item.IsSelected = true;
            _selectedRow = _commandDragStartRow;
        }

        var rows = GetSelectedCommandRows();
        if (rows.Count == 0)
            return;

        var data = new DataObject(CommandDragDataFormat, string.Join(";", rows.Select(row => row.Command!.Id)));
        _commandDragInProgress = true;
        try
        {
            DragDrop.DoDragDrop(CommandList, data, DragDropEffects.Move);
        }
        finally
        {
            _commandDragInProgress = false;
            _commandDragStartRow = null;
        }
    }

    private void CommandList_DragOver(object sender, DragEventArgs e)
    {
        if (!TryGetDragIds(e.Data, out var ids) || !CanDropCommands(ids, e))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void CommandList_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDragIds(e.Data, out var ids) || !CanDropCommands(ids, e) || _currentSequence is null)
            return;

        var draggedRows = CommandList.Items
            .OfType<CommandRow>()
            .Where(row => row.Command is not null && ids.Contains(row.Command.Id))
            .Where(row => !CommandList.Items.OfType<CommandRow>().Any(other =>
                other.Command is not null && ids.Contains(other.Command.Id) && other.Command.Id != row.Command!.Id &&
                CommandContains(other.Command, row.Command.Id)))
            .ToList();

        if (draggedRows.Count == 0)
            return;

        var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        var targetRow = item?.DataContext as CommandRow;
        List<MacroCommand> targetOwner;
        MacroCommand? targetCommand = null;
        var insertAfter = false;

        if (targetRow is null)
        {
            targetOwner = _currentSequence.Commands;
        }
        else if (targetRow.IsHeader)
        {
            targetOwner = targetRow.Owner ?? _currentSequence.Commands;
        }
        else
        {
            targetOwner = targetRow.Owner ?? _currentSequence.Commands;
            targetCommand = targetRow.Command;
            if (item is not null)
                insertAfter = e.GetPosition(item).Y >= item.ActualHeight / 2;
        }

        var commands = draggedRows.Select(row => row.Command!).ToList();
        foreach (var row in draggedRows)
            row.Owner!.Remove(row.Command!);

        var insertIndex = targetOwner.Count;
        if (targetRow?.IsHeader == true)
        {
            insertIndex = 0;
        }
        else if (targetCommand is not null)
        {
            var targetIndex = targetOwner.IndexOf(targetCommand);
            insertIndex = targetIndex < 0 ? targetOwner.Count : targetIndex + (insertAfter ? 1 : 0);
        }

        targetOwner.InsertRange(Math.Clamp(insertIndex, 0, targetOwner.Count), commands);

        var selectedIds = commands.Select(command => command.Id).ToList();
        MarkDirty();
        RefreshCommandList(selectedIds.FirstOrDefault(), selectedIds);
        StatusText.Text = commands.Count == 1 ? "Command moved" : $"{commands.Count} commands moved";
        e.Handled = true;
    }

    private bool CanDropCommands(HashSet<Guid> ids, DragEventArgs e)
    {
        if (_currentSequence is null || ids.Count == 0)
            return false;

        var dragged = CommandList.Items
            .OfType<CommandRow>()
            .Where(row => row.Command is not null && ids.Contains(row.Command.Id))
            .Select(row => row.Command!)
            .ToList();
        if (dragged.Count == 0)
            return false;

        var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        var targetRow = item?.DataContext as CommandRow;
        var anchor = targetRow?.Command ?? targetRow?.ParentCommand;
        if (anchor is null)
            return true;

        return !dragged.Any(command => command.Id == anchor.Id || CommandContains(command, anchor.Id));
    }

    private static bool TryGetDragIds(IDataObject data, out HashSet<Guid> ids)
    {
        ids = new HashSet<Guid>();
        if (!data.GetDataPresent(CommandDragDataFormat) || data.GetData(CommandDragDataFormat) is not string raw)
            return false;

        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(part, out var id))
                ids.Add(id);
        }
        return ids.Count > 0;
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDownButton_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int direction)
    {
        var rows = GetSelectedCommandRows();
        if (rows.Count == 0 || direction == 0)
            return;

        if (rows.Count > 1)
        {
            var owner = rows[0].Owner!;
            if (rows.Any(row => !ReferenceEquals(row.Owner, owner)))
            {
                StatusText.Text = "Selected commands must be in the same block to move together";
                return;
            }

            var indices = rows.Select(row => owner.IndexOf(row.Command!)).OrderBy(index => index).ToList();
            if (indices.Any(index => index < 0) || indices.Zip(indices.Skip(1), (a, b) => b == a + 1).Any(contiguous => !contiguous))
            {
                StatusText.Text = "Select neighboring commands to move them together";
                return;
            }

            var first = indices[0];
            var last = indices[^1];
            if ((direction < 0 && first == 0) || (direction > 0 && last == owner.Count - 1))
                return;

            var commands = rows.Select(row => row.Command!).ToList();
            foreach (var selectedCommand in commands)
                owner.Remove(selectedCommand);

            var insertIndex = direction < 0 ? first - 1 : first + 1;
            owner.InsertRange(Math.Clamp(insertIndex, 0, owner.Count), commands);

            var ids = commands.Select(command => command.Id).ToList();
            MarkDirty();
            RefreshCommandList(_selectedRow?.Command?.Id, ids);
            return;
        }

        var row = rows[0];
        var command = row.Command!;
        var ownerSingle = row.Owner!;
        var index = ownerSingle.IndexOf(command);
        if (index < 0)
            return;

        // Normal move between commands in the same block.
        var siblingIndex = index + Math.Sign(direction);
        if (siblingIndex >= 0 && siblingIndex < ownerSingle.Count)
        {
            ownerSingle.RemoveAt(index);
            ownerSingle.Insert(siblingIndex, command);
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
                ownerSingle.Remove(command);
                parent.Children.Add(command);
            }
            else if (TryFindCommandLocation(parent, out var parentOwner, out _, out _))
            {
                ownerSingle.Remove(command);
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
                ownerSingle.Remove(command);
                parent.ElseChildren.Insert(0, command);
            }
            else if (TryFindCommandLocation(parent, out var parentOwner, out _, out _))
            {
                ownerSingle.Remove(command);
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
        var rows = GetSelectedCommandRows();
        if (rows.Count == 0)
            return;

        var nestedCount = rows.Sum(row => CountCommandsInside(row.Command!));
        if (rows.Count > 1 || nestedCount > 0)
        {
            string message;
            if (rows.Count == 1)
            {
                var noun = nestedCount == 1 ? "command" : "commands";
                message = $"Delete this block and {nestedCount} {noun} inside it?";
            }
            else if (nestedCount > 0)
            {
                var noun = nestedCount == 1 ? "command" : "commands";
                message = $"Delete {rows.Count} selected commands and {nestedCount} {noun} inside them?";
            }
            else
            {
                message = $"Delete {rows.Count} selected commands?";
            }

            var result = MessageBox.Show(this, message, "Delete Commands", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;
        }

        foreach (var row in rows)
        {
            row.Owner!.Remove(row.Command!);
            RemoveCollapsedIds(row.Command!);
        }

        _selectedRow = null;
        MarkDirty();
        RefreshCommandList(selectIds: Array.Empty<Guid>());
        ClearProperties("Select a command to edit it.");
        StatusText.Text = rows.Count == 1 ? "Command deleted" : $"{rows.Count} commands deleted";
    }

    private static int CountCommandsInside(MacroCommand command)
    {
        var total = 0;
        foreach (var child in command.Children)
            total += 1 + CountCommandsInside(child);
        foreach (var child in command.ElseChildren)
            total += 1 + CountCommandsInside(child);
        return total;
    }

    private void RemoveCollapsedIds(MacroCommand command)
    {
        _collapsedBlocks.Remove(command.Id);
        foreach (var child in command.Children)
            RemoveCollapsedIds(child);
        foreach (var child in command.ElseChildren)
            RemoveCollapsedIds(child);
    }

    private void CommandList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingCommandSelection)
            return;

        var preferred = e.AddedItems
            .OfType<CommandRow>()
            .LastOrDefault(row => !row.IsHeader && row.Command is not null);

        if (preferred is null && _selectedRow?.Command is { } current &&
            CommandList.SelectedItems.OfType<CommandRow>().Any(row => row.Command?.Id == current.Id))
        {
            preferred = _selectedRow;
        }

        preferred ??= CommandList.SelectedItems
            .OfType<CommandRow>()
            .FirstOrDefault(row => !row.IsHeader && row.Command is not null);

        ApplyCommandSelection(preferred);
    }

    private void ApplyCommandSelection(CommandRow? row)
    {
        var selectedCommandCount = CommandList.SelectedItems
            .OfType<CommandRow>()
            .Count(item => !item.IsHeader && item.Command is not null);

        if (selectedCommandCount > 1)
        {
            _selectedRow = row?.Command is not null && !row.IsHeader
                ? row
                : CommandList.SelectedItems
                    .OfType<CommandRow>()
                    .FirstOrDefault(item => !item.IsHeader && item.Command is not null);

            UpdateBlockButtonState();
            UpdateRunButtons();
            ClearProperties($"{selectedCommandCount} commands selected");
            return;
        }

        if (row?.Command is null || row.IsHeader)
        {
            _selectedRow = null;
            UpdateBlockButtonState();
            UpdateRunButtons();
            ClearProperties("Select a command to edit it.");
            return;
        }

        _selectedRow = row;
        UpdateBlockButtonState();
        UpdateRunButtons();
        BuildProperties(row.Command);
    }

    private void CommandList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var selectedCommandCount = CommandList.SelectedItems
            .OfType<CommandRow>()
            .Count(item => !item.IsHeader && item.Command is not null);
        if (selectedCommandCount > 1)
            return;

        if (_selectedRow?.Command is not { } command)
            return;

        if (_selectedRow.IsCollapsible)
            ToggleCollapsed(command);
        else
            BuildProperties(command);
    }

    private void RefreshCommandList(Guid? selectId = null, IReadOnlyCollection<Guid>? selectIds = null)
    {
        if (_currentSequence is null)
            return;

        var previousActiveId = _selectedRow?.Command?.Id;
        var previousSelectedIds = CommandList.SelectedItems
            .OfType<CommandRow>()
            .Where(row => row.Command is not null)
            .Select(row => row.Command!.Id)
            .ToHashSet();

        HashSet<Guid> idsToSelect;
        if (selectIds is not null)
        {
            idsToSelect = selectIds.ToHashSet();
        }
        else if (selectId.HasValue)
        {
            // Keep a multi-selection together when a property refreshes the active command.
            idsToSelect = previousSelectedIds.Count > 1 && previousSelectedIds.Contains(selectId.Value)
                ? previousSelectedIds
                : new HashSet<Guid> { selectId.Value };
        }
        else
        {
            idsToSelect = previousSelectedIds;
            if (idsToSelect.Count == 0 && previousActiveId.HasValue)
                idsToSelect.Add(previousActiveId.Value);
        }

        var rows = new List<CommandRow>();
        FlattenCommands(_currentSequence.Commands, rows, 0, CommandBranch.Root);
        if (rows.Count == 0)
        {
            rows.Add(new CommandRow
            {
                IsHeader = true,
                Label = "EMPTY",
                Owner = _currentSequence.Commands,
                Depth = 0,
                Branch = CommandBranch.Root
            });
        }

        _refreshingCommandSelection = true;
        try
        {
            CommandList.ItemsSource = rows;
            CommandList.UnselectAll();
            foreach (var row in rows.Where(row => row.Command is not null && idsToSelect.Contains(row.Command.Id)))
                CommandList.SelectedItems.Add(row);
        }
        finally
        {
            _refreshingCommandSelection = false;
        }

        var activeId = selectId ?? (previousActiveId.HasValue && idsToSelect.Contains(previousActiveId.Value) ? previousActiveId : null);
        var activeRow = activeId.HasValue
            ? rows.FirstOrDefault(row => row.Command?.Id == activeId.Value)
            : rows.FirstOrDefault(row => row.Command is not null && idsToSelect.Contains(row.Command.Id));

        if (activeRow is not null)
            CommandList.ScrollIntoView(activeRow);

        ApplyCommandSelection(activeRow);
        UpdateBlockButtonState();
    }

    private void FlattenCommands(List<MacroCommand> source, List<CommandRow> rows, int depth, CommandBranch branch, MacroCommand? parentCommand = null)
    {
        foreach (var command in source)
        {
            if (command.Type == CommandType.Comment)
                continue;

            var isCollapsed = _collapsedBlocks.Contains(command.Id);
            rows.Add(new CommandRow
            {
                Command = command,
                Owner = source,
                ParentCommand = parentCommand,
                Depth = depth,
                Branch = branch,
                IsCollapsed = isCollapsed
            });

            if (!command.HasBody || command.Type == CommandType.RecordedActions || isCollapsed)
                continue;

            var bodyLabel = command.HasElse ? "THEN" : command.Type == CommandType.Group ? "GROUP" : "DO";
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
        _rebuildingProperties = true;
        try
        {
            PropertiesPanel.Children.Clear();
            PropertiesHintText.Text = FriendlyName(command.Type);

            switch (command.Type)
            {
            case CommandType.Comment:
                AddTextField("Comment", command.Text, value => command.Text = value, true, allowVariables: false);
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
                AddNumberField("Scroll amount (positive = up, negative = down)", command.ScrollAmount, -12000, 12000, value => command.ScrollAmount = value);
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
                break;

            case CommandType.PressKey:
                AddTextField("Key or combo", command.Key, value => command.Key = value);
                break;

            case CommandType.KeyDown:
            case CommandType.KeyUp:
                AddTextField("Key", command.Key, value => command.Key = value);
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
                AdoptLegacyNumberExpression(command, nameof(MacroCommand.RepeatCount), command.RepeatExpression, () => command.RepeatExpression = string.Empty);
                AddNumberField("Repeat count", command.RepeatCount, 1, 1_000_000, value => command.RepeatCount = value);
                AddNumberField("Delay between presses (ms)", command.WaitMs, 0, 86_400_000, value => command.WaitMs = value);
                break;

            case CommandType.WaitUntilKeyPressed:
            case CommandType.WaitUntilKeyReleased:
                AddTextField("Key", command.Key, value => command.Key = value);
                AddPollingFields(command);
                break;

            case CommandType.IfKeyPressed:
                AddTextField("Key", command.Key, value => command.Key = value);
                AddBlockInfo(command);
                break;

            case CommandType.LoopWhileKeyPressed:
                AddTextField("Key", command.Key, value => command.Key = value);
                AddNumberField("Check every (ms)", command.PollMs, 10, 5000, value => command.PollMs = value);
                AddBlockInfo(command);
                break;

            case CommandType.Wait:
                AdoptLegacyNumberExpression(command, nameof(MacroCommand.WaitMs), command.WaitExpression, () => command.WaitExpression = string.Empty);
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
                    AddNumberField("Check every (ms)", command.PollMs, 10, 5000, value => command.PollMs = value);
                if (command.HasBody)
                    AddBlockInfo(command);
                break;

            case CommandType.ClickColor:
            case CommandType.FindColorToVariables:
                AddColorField(command);
                AddNumberField("Color tolerance", command.ColorTolerance, 0, 255, value => command.ColorTolerance = value);
                AddSearchAreaFields(command);
                if (command.Type == CommandType.ClickColor)
                    AddMouseMovementFields(command);
                else
                    AddStorePointVariableFields(command);
                AddColorSearchTest(command);
                break;

            case CommandType.SampleColorToVariable:
                AddLocationFields(command);
                AddVariableNameField("Save color as", command.StoreTextVariable, value => command.StoreTextVariable = value);
                break;

            case CommandType.IfImage:
            case CommandType.WaitUntilImage:
            case CommandType.WaitUntilImageGone:
            case CommandType.ClickImage:
            case CommandType.DoubleClickImage:
            case CommandType.MoveToImage:
            case CommandType.LoopUntilImage:
            case CommandType.LoopWhileImage:
            case CommandType.FindImageToVariables:
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
                if (command.Type is CommandType.LoopUntilImage or CommandType.LoopWhileImage)
                    AddNumberField("Check every (ms)", command.PollMs, 10, 5000, value => command.PollMs = value);
                if (command.Type == CommandType.FindImageToVariables)
                    AddStorePointVariableFields(command);
                if (command.HasBody)
                    AddBlockInfo(command);
                break;

            case CommandType.IfWindow:
                AddTextField("Window title contains", command.WindowTitle, value => command.WindowTitle = value);
                AddBlockInfo(command);
                break;

            case CommandType.FocusWindow:
            case CommandType.MinimizeWindow:
            case CommandType.MaximizeWindow:
            case CommandType.RestoreWindow:
            case CommandType.CloseWindow:
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

            case CommandType.SetVariable:
                AddVariableNameField("Save as", command.VariableName, value => command.VariableName = value);
                AddValueOrVariableField("Value", command.VariableValue, value => command.VariableValue = value);
                break;

            case CommandType.AddVariable:
                AddExistingVariableField("Change this variable", command.VariableName, value => command.VariableName = value);
                AddValueOrVariableField("Add this amount", command.VariableValue, value => command.VariableValue = value);
                break;

            case CommandType.RandomNumber:
                AddVariableNameField("Save random number as", command.VariableName, value => command.VariableName = value);
                AddValueOrVariableField("Minimum", command.VariableValue, value => command.VariableValue = value);
                AddValueOrVariableField("Maximum", command.VariableValue2, value => command.VariableValue2 = value);
                break;

            case CommandType.IfVariable:
            case CommandType.WaitUntilVariable:
            case CommandType.LoopWhileVariable:
            case CommandType.LoopUntilVariable:
                AddVariableConditionFields(command);
                if (command.Type == CommandType.WaitUntilVariable) AddPollingFields(command);
                else if (command.Type is CommandType.LoopWhileVariable or CommandType.LoopUntilVariable)
                    AddNumberField("Check every (ms)", command.PollMs, 10, 5000, value => command.PollMs = value);
                if (command.HasBody) AddBlockInfo(command);
                break;

            case CommandType.SetClipboard:
                AddTextField("Clipboard text", command.Text, value => command.Text = value, true);
                break;

            case CommandType.ClipboardToVariable:
                AddVariableNameField("Save as", command.VariableName, value => command.VariableName = value);
                break;

            case CommandType.ReadTextFile:
                AddFileDataFields(command, false, true, false);
                break;

            case CommandType.WriteTextFile:
                AddFileDataFields(command, true, false, true);
                break;

            case CommandType.PromptText:
                AddTextField("Question", command.PromptText, value => command.PromptText = value, true);
                AddVariableNameField("Save answer as", command.VariableName, value => command.VariableName = value);
                AddTextField("Default answer", command.VariableValue, value => command.VariableValue = value);
                break;

            case CommandType.PromptYesNo:
                AddTextField("Question", command.PromptText, value => command.PromptText = value, true);
                AddVariableNameField("Save answer as", command.VariableName, value => command.VariableName = value);
                break;

            case CommandType.Group:
                AddBlockInfo(command);
                break;

            case CommandType.RunSequence:
                AddSequencePicker(command);
                break;

            case CommandType.LoopTimes:
                AdoptLegacyNumberExpression(command, nameof(MacroCommand.RepeatCount), command.RepeatExpression, () => command.RepeatExpression = string.Empty);
                AddNumberField("Repeat count", command.RepeatCount, 0, 1_000_000, value => command.RepeatCount = value);
                AddBlockInfo(command);
                break;

            case CommandType.LoopForever:
                AddBlockInfo(command);
                AddInfo($"Use Break Loop inside this block, or {_appSettings.StopMacroHotkey} at any time, to stop it.");
                break;

            case CommandType.Break:
            case CommandType.Return:
            case CommandType.StopMacro:
                break;
        }

            AddMoreOptions(command);
        }
        finally
        {
            _rebuildingProperties = false;
        }
    }

    private void AddMouseMovementFields(MacroCommand command)
    {
        if (command.MouseMoveMode == MouseMoveMode.Legacy)
            command.MouseMoveMode = command.MoveDurationMs > 0 ? MouseMoveMode.Smooth : MouseMoveMode.Teleport;

        AddLabel("Mouse movement");
        var mode = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        mode.Items.Add(new ComboBoxItem { Content = "Instant", Tag = MouseMoveMode.Teleport });
        mode.Items.Add(new ComboBoxItem { Content = "Smooth", Tag = MouseMoveMode.Smooth });
        mode.SelectedItem = mode.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Tag is MouseMoveMode m && m == command.MouseMoveMode);
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedItem is not ComboBoxItem { Tag: MouseMoveMode selected } || selected == command.MouseMoveMode)
                return;

            command.MouseMoveMode = selected;
            if (selected == MouseMoveMode.Smooth && command.MoveDurationMs <= 0)
                command.MoveDurationMs = 50;
            MarkDirtyAndRefresh(command.Id);
            BuildProperties(command);
        };
        PropertiesPanel.Children.Add(mode);

        if (command.MouseMoveMode == MouseMoveMode.Smooth)
        {
            AddNumberField("Slide time (ms)", command.MoveDurationMs, 1, 60000, value => command.MoveDurationMs = value);
        }
    }

    private void AddRecordedActionsProperties(MacroCommand command)
    {
        AddLabel("Stop recording hotkey");
        var hotkeyBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(command.RecordingStopHotkey) ? "F7" : command.RecordingStopHotkey,
            Margin = new Thickness(0, 0, 0, 8)
        };
        hotkeyBox.TextChanged += (_, _) =>
        {
            var value = hotkeyBox.Text.Trim();
            if (!GlobalInputRecorder.IsValidHotkey(value))
                return;
            if (value == command.RecordingStopHotkey)
                return;
            command.RecordingStopHotkey = value;
            MarkDirtyAndRefreshCommandDisplay(command.Id);
        };
        hotkeyBox.LostFocus += (_, _) =>
        {
            var value = hotkeyBox.Text.Trim();
            if (GlobalInputRecorder.IsValidHotkey(value))
                return;
            MessageBox.Show(this,
                "Invalid hotkey. Examples: F7, F10, Ctrl+F7, Shift+F6.",
                "Recorder Hotkey", MessageBoxButton.OK, MessageBoxImage.Information);
            hotkeyBox.Text = string.IsNullOrWhiteSpace(command.RecordingStopHotkey) ? "F7" : command.RecordingStopHotkey;
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
            AddNumberField("Mouse sample interval (ms)", command.RecordMouseSampleMs, 15, 500, value => command.RecordMouseSampleMs = value, allowVariables: false);

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
        AddNumberField("Color tolerance", command.ColorTolerance, 0, 255, value => command.ColorTolerance = value);

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
        AddLabel("Check for");
        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 12) };
        combo.Items.Add(new ComboBoxItem { Content = "Color matches", Tag = CompareMode.Equals });
        combo.Items.Add(new ComboBoxItem { Content = "Color does not match", Tag = CompareMode.NotEquals });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Tag is CompareMode m && m == command.CompareMode);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: CompareMode mode })
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
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new TextBox
        {
            Text = command.ColorHex,
            ToolTip = "Type a color like #FF8800 or choose a saved variable"
        };
        box.TextChanged += (_, _) =>
        {
            var text = box.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return;

            command.ColorHex = Regex.IsMatch(text, @"^(?:0x|#)?[0-9A-Fa-f]{6}$")
                ? NormalizeColor(text)
                : text;
            MarkDirtyAndRefreshCommandDisplay(command.Id);
        };
        box.LostFocus += (_, _) =>
        {
            var text = box.Text.Trim();
            if (Regex.IsMatch(text, @"^(?:0x|#)?[0-9A-Fa-f]{6}$"))
                box.Text = NormalizeColor(text);
        };

        var variablePicker = CreateVariablePicker(box);
        var pick = new Button { Content = "Pick Color + Location", MinWidth = 95, Margin = new Thickness(7, 0, 0, 0) };
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
                    command.XExpression = string.Empty;
                    command.YExpression = string.Empty;
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
        Grid.SetColumn(variablePicker, 1);
        Grid.SetColumn(pick, 2);
        row.Children.Add(box);
        row.Children.Add(variablePicker);
        row.Children.Add(pick);
        PropertiesPanel.Children.Add(row);
    }

    private void AddLocationFields(MacroCommand command, string label = "Location")
    {
        AddLabel(label);

        var xBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(command.XExpression) ? command.X.ToString() : command.XExpression,
            ToolTip = "Type a number or choose a saved variable"
        };
        var yBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(command.YExpression) ? command.Y.ToString() : command.YExpression,
            ToolTip = "Type a number or choose a saved variable"
        };

        xBox.TextChanged += (_, _) =>
        {
            var text = xBox.Text.Trim();
            if (int.TryParse(text, out var value))
            {
                command.X = value;
                command.XExpression = string.Empty;
            }
            else
            {
                command.XExpression = text;
            }
            MarkDirtyAndRefreshCommandDisplay(command.Id);
        };
        yBox.TextChanged += (_, _) =>
        {
            var text = yBox.Text.Trim();
            if (int.TryParse(text, out var value))
            {
                command.Y = value;
                command.YExpression = string.Empty;
            }
            else
            {
                command.YExpression = text;
            }
            MarkDirtyAndRefreshCommandDisplay(command.Id);
        };

        PropertiesPanel.Children.Add(CreateCoordinateRow("X", xBox, command.X.ToString()));
        PropertiesPanel.Children.Add(CreateCoordinateRow("Y", yBox, command.Y.ToString()));

        var set = new Button { Content = "Pick Location", Margin = new Thickness(0, 0, 0, 12) };
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
                    command.XExpression = string.Empty;
                    command.YExpression = string.Empty;
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
        AddLocationOptionsExpander(command, command.Type == CommandType.DragMouse);
    }

    private Grid CreateCoordinateRow(string axis, TextBox box, string fallbackValue)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = axis,
            Width = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        };

        var variablePicker = CreateVariablePicker(box, fallbackValue);
        Grid.SetColumn(label, 0);
        Grid.SetColumn(box, 1);
        Grid.SetColumn(variablePicker, 2);
        row.Children.Add(label);
        row.Children.Add(box);
        row.Children.Add(variablePicker);
        return row;
    }

    private void AddEndLocationFields(MacroCommand command)
    {
        AddLabel("End location");

        var xBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(command.EndXExpression) ? command.EndX.ToString() : command.EndXExpression,
            ToolTip = "Type a number or choose a saved variable"
        };
        var yBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(command.EndYExpression) ? command.EndY.ToString() : command.EndYExpression,
            ToolTip = "Type a number or choose a saved variable"
        };

        xBox.TextChanged += (_, _) =>
        {
            var text = xBox.Text.Trim();
            if (int.TryParse(text, out var value))
            {
                command.EndX = value;
                command.EndXExpression = string.Empty;
            }
            else
            {
                command.EndXExpression = text;
            }
            MarkDirtyAndRefreshCommandDisplay(command.Id);
        };
        yBox.TextChanged += (_, _) =>
        {
            var text = yBox.Text.Trim();
            if (int.TryParse(text, out var value))
            {
                command.EndY = value;
                command.EndYExpression = string.Empty;
            }
            else
            {
                command.EndYExpression = text;
            }
            MarkDirtyAndRefreshCommandDisplay(command.Id);
        };

        PropertiesPanel.Children.Add(CreateCoordinateRow("X", xBox, command.EndX.ToString()));
        PropertiesPanel.Children.Add(CreateCoordinateRow("Y", yBox, command.EndY.ToString()));

        var set = new Button { Content = "Pick End Location", Margin = new Thickness(0, 0, 0, 12) };
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
                    command.EndXExpression = string.Empty;
                    command.EndYExpression = string.Empty;
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
        AddCoordinateModeFields(command);
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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new TextBox { Text = command.ProgramPath };
        box.TextChanged += (_, _) =>
        {
            command.ProgramPath = box.Text.Trim();
            MarkDirtyAndRefreshCommandDisplay(command.Id);
        };

        var variablePicker = CreateVariablePicker(box);
        var browse = new Button { Content = "Browse…", MinWidth = 76, Margin = new Thickness(7, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose program or file",
                Filter = "All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) == true)
            {
                box.Text = dialog.FileName;
                MarkDirtyAndRefreshCommandDisplay(command.Id);
            }
        };

        Grid.SetColumn(box, 0);
        Grid.SetColumn(variablePicker, 1);
        Grid.SetColumn(browse, 2);
        grid.Children.Add(box);
        grid.Children.Add(variablePicker);
        grid.Children.Add(browse);
        PropertiesPanel.Children.Add(grid);
        AddTextField("Arguments (optional)", command.ProgramArguments, value => command.ProgramArguments = value);
        AddTextField("Working directory (optional)", command.WorkingDirectory, value => command.WorkingDirectory = value);
    }

    private void AddImageFields(MacroCommand command)
    {
        AddLabel("Image source");
        var sourceText = !string.IsNullOrWhiteSpace(command.ImageFolder)
            ? $"Folder: {command.ImageFolder}"
            : (!string.IsNullOrWhiteSpace(command.ImagePath) ? $"Image: {command.ImagePath}" : "No image selected");

        PropertiesPanel.Children.Add(new TextBlock
        {
            Text = sourceText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = (Brush)FindResource("TextBrush")
        });

        if (!string.IsNullOrWhiteSpace(_projectPath))
        {
            AddLabel("Project image library");
            var assets = GetProjectImageAssets();
            if (assets.Count > 0)
            {
                var chooseLibrary = new Button
                {
                    Content = "Choose From Project…",
                    Margin = new Thickness(0, 0, 0, 10),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                chooseLibrary.Click += (_, _) =>
                {
                    var currentPath = !string.IsNullOrWhiteSpace(command.ImageFolder)
                        ? command.ImageFolder
                        : command.ImagePath;
                    var picker = new ProjectImageLibraryWindow(
                        _projectPath!,
                        currentPath,
                        !string.IsNullOrWhiteSpace(command.ImageFolder))
                    {
                        Owner = this
                    };

                    if (picker.ShowDialog() != true)
                        return;

                    if (picker.SelectedIsFolder)
                    {
                        command.ImageFolder = picker.SelectedRelativePath;
                        command.ImagePath = string.Empty;
                        command.ImagePriority.Clear();
                        SyncImagePriority(command);
                    }
                    else
                    {
                        command.ImagePath = picker.SelectedRelativePath;
                        command.ImageFolder = string.Empty;
                        command.ImagePriority.Clear();
                    }

                    MarkDirty();
                    BuildProperties(command);
                    RefreshCommandList(command.Id);
                };
                PropertiesPanel.Children.Add(chooseLibrary);
            }
            else
            {
                PropertiesPanel.Children.Add(new TextBlock
                {
                    Text = "No project images yet. Import one below and it becomes reusable in every image command.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10),
                    Foreground = (Brush)FindResource("MutedTextBrush")
                });
            }
        }

        var sourceButtons = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        sourceButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sourceButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sourceButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var chooseImage = new Button { Content = "Import Image", Margin = new Thickness(0, 0, 4, 0) };
        var chooseFolder = new Button { Content = "Import Folder", Margin = new Thickness(4, 0, 4, 0) };
        var capture = new Button { Content = "Capture", Margin = new Thickness(4, 0, 0, 0) };

        chooseImage.Click += (_, _) =>
        {
            if (!EnsureProjectFolder())
                return;

            var dialog = new OpenFileDialog
            {
                Title = "Choose image to detect",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                command.ImagePath = ImportImageFile(dialog.FileName);
                command.ImageFolder = string.Empty;
                command.ImagePriority.Clear();
                MarkDirty();
                BuildProperties(command);
                RefreshCommandList(command.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not import image", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        chooseFolder.Click += (_, _) =>
        {
            if (!EnsureProjectFolder())
                return;

            var dialog = new OpenFolderDialog
            {
                Title = "Choose a folder of priority images",
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                command.ImageFolder = ImportImageFolder(dialog.FolderName);
                command.ImagePath = string.Empty;
                command.ImagePriority.Clear();
                SyncImagePriority(command);
                MarkDirty();
                BuildProperties(command);
                RefreshCommandList(command.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not import image folder", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        capture.Click += async (_, _) =>
        {
            if (!EnsureProjectFolder())
                return;

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
                Activate();
            }

            if (image is null)
                return;

            var prompt = new TextPromptWindow("Save Reference Image", "Image name:", "reference") { Owner = this };
            if (prompt.ShowDialog() != true)
                return;

            var baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(prompt.Value.Trim()));
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "reference";
            var destination = Path.Combine(ProjectImagesFolder(), baseName + ".png");
            var suffix = 2;
            while (File.Exists(destination))
                destination = Path.Combine(ProjectImagesFolder(), $"{baseName}_{suffix++}.png");

            ScreenTools.SavePng(image, destination);
            command.ImagePath = ProjectPaths.MakeRelative(destination);
            command.ImageFolder = string.Empty;
            command.ImagePriority.Clear();
            MarkDirty();
            BuildProperties(command);
            RefreshCommandList(command.Id);
        };

        Grid.SetColumn(chooseImage, 0);
        Grid.SetColumn(chooseFolder, 1);
        Grid.SetColumn(capture, 2);
        sourceButtons.Children.Add(chooseImage);
        sourceButtons.Children.Add(chooseFolder);
        sourceButtons.Children.Add(capture);
        PropertiesPanel.Children.Add(sourceButtons);

        if (!string.IsNullOrWhiteSpace(command.ImageFolder))
            AddImagePriorityEditor(command);

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
        Grid.SetColumn(setRegion, 0);
        Grid.SetColumn(fullRegion, 1);
        regionButtons.Children.Add(setRegion);
        regionButtons.Children.Add(fullRegion);
        PropertiesPanel.Children.Add(regionButtons);

        var test = new Button { Content = "Test Image Detection", Margin = new Thickness(0, 0, 0, 12) };
        test.Click += async (_, _) =>
        {
            if (ImageMatcher.GetCandidatePaths(command).Count == 0)
            {
                MessageBox.Show(this, "Choose an image or an image folder first.", "Image Detection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Hide();
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
            finally
            {
                Show();
                Activate();
            }

            if (error is not null)
            {
                MessageBox.Show(this, error.Message, "Image Detection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else if (match.HasValue)
            {
                var matchedName = string.IsNullOrWhiteSpace(match.Value.SourcePath)
                    ? "image"
                    : Path.GetFileName(match.Value.SourcePath);
                MessageBox.Show(this,
                    $"FOUND: {matchedName}\nTop-left: {match.Value.X}, {match.Value.Y}\nCenter: {match.Value.CenterX}, {match.Value.CenterY}\nSize: {match.Value.Width} × {match.Value.Height}",
                    "Image Detection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this,
                    "No image was found. With a folder source, images are checked from top to bottom in Priorities.",
                    "Image Detection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        PropertiesPanel.Children.Add(test);
    }

    private void AddImagePriorityEditor(MacroCommand command)
    {
        SyncImagePriority(command);

        AddLabel("Priorities");
        PropertiesPanel.Children.Add(new TextBlock
        {
            Text = "Top = highest priority. The first matching image wins.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 7),
            Foreground = (Brush)FindResource("MutedTextBrush")
        });

        var includeSubfolders = new CheckBox
        {
            Content = "Include images inside subfolders",
            IsChecked = command.ImageIncludeSubfolders,
            Margin = new Thickness(0, 0, 0, 9)
        };
        includeSubfolders.Checked += (_, _) =>
        {
            command.ImageIncludeSubfolders = true;
            command.ImagePriority.Clear();
            SyncImagePriority(command);
            MarkDirty();
            BuildProperties(command);
            RefreshCommandList(command.Id);
        };
        includeSubfolders.Unchecked += (_, _) =>
        {
            command.ImageIncludeSubfolders = false;
            command.ImagePriority.Clear();
            SyncImagePriority(command);
            MarkDirty();
            BuildProperties(command);
            RefreshCommandList(command.Id);
        };
        PropertiesPanel.Children.Add(includeSubfolders);

        var list = new ListBox
        {
            Height = 210,
            Margin = new Thickness(0, 0, 0, 7),
            Background = (Brush)FindResource("InputBrush"),
            Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = (Brush)FindResource("BorderBrushDark"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6)
        };

        void RefreshPriorityList(int selectedIndex = -1)
        {
            list.ItemsSource = command.ImagePriority
                .Select((name, index) => $"{index + 1}.  {name}")
                .ToList();
            if (selectedIndex >= 0 && selectedIndex < command.ImagePriority.Count)
                list.SelectedIndex = selectedIndex;
        }

        RefreshPriorityList();
        PropertiesPanel.Children.Add(list);

        var buttons = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var up = new Button { Content = "↑ Higher", Margin = new Thickness(0, 0, 4, 0) };
        var down = new Button { Content = "↓ Lower", Margin = new Thickness(4, 0, 4, 0) };
        var refresh = new Button { Content = "↻ Refresh", Margin = new Thickness(4, 0, 0, 0) };

        up.Click += (_, _) =>
        {
            var index = list.SelectedIndex;
            if (index <= 0 || index >= command.ImagePriority.Count)
                return;

            (command.ImagePriority[index - 1], command.ImagePriority[index]) =
                (command.ImagePriority[index], command.ImagePriority[index - 1]);
            MarkDirty();
            RefreshPriorityList(index - 1);
            RefreshCommandList(command.Id);
        };

        down.Click += (_, _) =>
        {
            var index = list.SelectedIndex;
            if (index < 0 || index >= command.ImagePriority.Count - 1)
                return;

            (command.ImagePriority[index + 1], command.ImagePriority[index]) =
                (command.ImagePriority[index], command.ImagePriority[index + 1]);
            MarkDirty();
            RefreshPriorityList(index + 1);
            RefreshCommandList(command.Id);
        };

        refresh.Click += (_, _) =>
        {
            var selected = list.SelectedIndex;
            SyncImagePriority(command);
            MarkDirty();
            RefreshPriorityList(Math.Min(selected, command.ImagePriority.Count - 1));
            RefreshCommandList(command.Id);
        };

        Grid.SetColumn(up, 0);
        Grid.SetColumn(down, 1);
        Grid.SetColumn(refresh, 2);
        buttons.Children.Add(up);
        buttons.Children.Add(down);
        buttons.Children.Add(refresh);
        PropertiesPanel.Children.Add(buttons);
    }

    private void AddPollingFields(MacroCommand command)
    {
        var panel = new StackPanel { Margin = new Thickness(2, 7, 2, 2) };

        panel.Children.Add(new TextBlock { Text = "Check every (ms)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
        panel.Children.Add(CreateInlineNumberInput(command, nameof(MacroCommand.PollMs), command.PollMs, 10, 5000, value => command.PollMs = value));

        panel.Children.Add(new TextBlock { Text = "Give up after (ms)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
        panel.Children.Add(CreateInlineNumberInput(command, nameof(MacroCommand.TimeoutMs), command.TimeoutMs, 0, 86_400_000, value => command.TimeoutMs = value));
        panel.Children.Add(new TextBlock
        {
            Text = "0 = wait forever.",
            Foreground = (Brush)FindResource("MutedTextBrush"),
            FontSize = 11
        });

        var expander = new Expander
        {
            Header = "Timing Options",
            IsExpanded = _expandedTimingOptions.Contains(command.Id),
            Foreground = (Brush)FindResource("TextBrush"),
            Content = new Border
            {
                Background = (Brush)FindResource("Panel2Brush"),
                BorderBrush = (Brush)FindResource("BorderBrushSoft"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(11),
                Margin = new Thickness(0, 7, 0, 0),
                Child = panel
            },
            Margin = new Thickness(0, 0, 0, 10)
        };
        expander.Expanded += (_, _) => _expandedTimingOptions.Add(command.Id);
        expander.Collapsed += (_, _) => { if (!_rebuildingProperties) _expandedTimingOptions.Remove(command.Id); };
        PropertiesPanel.Children.Add(expander);
    }

    private void AddBlockInfo(MacroCommand command)
    {
        AddInfo(command.HasElse
            ? "Add to THEN for the true side. Add to ELSE for the other side."
            : "Add commands to this block to choose what repeats.");
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
    }

    private void AdoptLegacyNumberExpression(MacroCommand command, string propertyName, string legacyExpression, Action clearLegacy)
    {
        if (string.IsNullOrWhiteSpace(legacyExpression))
            return;

        command.ValueExpressions ??= new Dictionary<string, string>();
        if (!command.ValueExpressions.ContainsKey(propertyName))
            command.ValueExpressions[propertyName] = legacyExpression.Trim();
        clearLegacy();
        MarkDirty();
    }

    private void AddNumberField(
        string label,
        int currentValue,
        int min,
        int max,
        Action<int> setter,
        bool allowVariables = true,
        [CallerArgumentExpression(nameof(currentValue))] string? currentValueExpression = null)
    {
        AddLabel(label);

        var command = _selectedRow?.Command;
        var propertyName = ExtractPropertyName(currentValueExpression);
        var savedExpression = allowVariables && command?.ValueExpressions is not null && !string.IsNullOrWhiteSpace(propertyName)
            && command.ValueExpressions.TryGetValue(propertyName, out var expression)
                ? expression
                : string.Empty;

        var box = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(savedExpression) ? currentValue.ToString() : savedExpression,
            ToolTip = allowVariables
                ? "Type a number, saved variable, or formula"
                : null
        };

        var lastNumber = currentValue;
        box.TextChanged += (_, _) =>
        {
            var text = box.Text.Trim();
            if (int.TryParse(text, out var value))
            {
                value = Math.Clamp(value, min, max);
                lastNumber = value;
                setter(value);
                if (command?.ValueExpressions is not null && !string.IsNullOrWhiteSpace(propertyName))
                    command.ValueExpressions.Remove(propertyName);
            }
            else if (allowVariables && command is not null && !string.IsNullOrWhiteSpace(propertyName))
            {
                command.ValueExpressions ??= new Dictionary<string, string>();
                if (string.IsNullOrWhiteSpace(text))
                    command.ValueExpressions.Remove(propertyName);
                else
                    command.ValueExpressions[propertyName] = text;
            }

            MarkDirtyAndRefreshCommandDisplay(command?.Id);
        };

        box.LostFocus += (_, _) =>
        {
            if (allowVariables && !string.IsNullOrWhiteSpace(box.Text) && !int.TryParse(box.Text, out _))
                return;

            if (!int.TryParse(box.Text, out var value))
            {
                box.Text = lastNumber.ToString();
                return;
            }

            value = Math.Clamp(value, min, max);
            lastNumber = value;
            setter(value);
            if (box.Text != value.ToString())
                box.Text = value.ToString();
            MarkDirtyAndRefreshCommandDisplay(command?.Id);
        };

        PropertiesPanel.Children.Add(allowVariables ? CreateVariableInputRow(box, currentValue.ToString()) : WrapInput(box));
    }

    private static string ExtractPropertyName(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return string.Empty;
        var text = expression.Trim();
        var dot = text.LastIndexOf('.');
        return dot >= 0 ? text[(dot + 1)..].Trim() : text;
    }

    private TextBox CreateValueBox(string currentValue, Thickness margin, string? tooltip = null)
    {
        return new TextBox
        {
            Text = currentValue,
            Margin = margin,
            ToolTip = tooltip ?? "Type a number, a saved variable name, or a simple formula"
        };
    }

    private void AddVariableNameField(string label, string currentValue, Action<string> setter)
    {
        AddLabel(label);
        var box = new TextBox
        {
            Text = currentValue,
            Margin = new Thickness(0, 0, 0, 12),
            ToolTip = "Type the name you want to save this value as"
        };
        box.TextChanged += (_, _) =>
        {
            setter(box.Text.Trim());
            MarkDirtyAndRefreshCommandDisplay(_selectedRow?.Command?.Id);
        };
        PropertiesPanel.Children.Add(box);
    }

    private void AddExistingVariableField(string label, string currentValue, Action<string> setter)
    {
        AddLabel(label);
        var variableNames = GetSavedVariableNames();
        var combo = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 12),
            ToolTip = "Choose a variable that has been saved in this macro"
        };

        combo.Items.Add("None");
        foreach (var name in variableNames)
            combo.Items.Add(name);

        var selectedName = variableNames.FirstOrDefault(name =>
            string.Equals(name, currentValue?.Trim(), StringComparison.OrdinalIgnoreCase));
        combo.SelectedItem = selectedName ?? "None";
        combo.IsEnabled = variableNames.Count > 0;

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not string value)
                return;

            var nextValue = value == "None" ? string.Empty : value;
            if (string.Equals(nextValue, currentValue, StringComparison.OrdinalIgnoreCase))
                return;

            setter(nextValue);
            currentValue = nextValue;
            MarkDirtyAndRefreshCommandDisplay(_selectedRow?.Command?.Id);
        };

        PropertiesPanel.Children.Add(combo);
    }

    private List<string> GetSavedVariableNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddName(string? value)
        {
            var name = value?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        // Variables explicitly created in the Variables manager are real saved variables too.
        foreach (var variable in _project.Variables ?? new List<ProjectVariable>())
            AddName(variable.Name);

        void ScanCommands(IEnumerable<MacroCommand> commands)
        {
            foreach (var command in commands)
            {
                switch (command.Type)
                {
                    case CommandType.SetVariable:
                    case CommandType.RandomNumber:
                    case CommandType.ClipboardToVariable:
                    case CommandType.ReadTextFile:
                    case CommandType.PromptText:
                    case CommandType.PromptYesNo:
                        AddName(command.VariableName);
                        break;

                    case CommandType.FindColorToVariables:
                    case CommandType.FindImageToVariables:
                        AddName(command.StoreXVariable);
                        AddName(command.StoreYVariable);
                        break;

                    case CommandType.SampleColorToVariable:
                        AddName(command.StoreTextVariable);
                        break;
                }

                ScanCommands(command.Children);
                ScanCommands(command.ElseChildren);
            }
        }

        foreach (var sequence in _project.Sequences)
            ScanCommands(sequence.Commands);

        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private ComboBox CreateVariablePicker(TextBox target, string? fallbackValue = null)
    {
        var names = GetSavedVariableNames();
        var combo = new ComboBox
        {
            MinWidth = 112,
            MaxWidth = 142,
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "Use a saved variable in this field"
        };

        combo.Items.Add("None");
        foreach (var name in names)
            combo.Items.Add(name);

        var initialMatch = names.FirstOrDefault(name =>
            string.Equals(name, target.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        var previousLiteralValue = initialMatch is null ? target.Text : fallbackValue ?? string.Empty;
        var updating = false;

        void MatchPickerToText()
        {
            if (updating)
                return;

            var text = target.Text.Trim();
            var match = names.FirstOrDefault(name =>
                string.Equals(name, text, StringComparison.OrdinalIgnoreCase));

            if (match is null)
                previousLiteralValue = target.Text;

            updating = true;
            try
            {
                combo.SelectedItem = match ?? "None";
            }
            finally
            {
                updating = false;
            }
        }

        combo.SelectedItem = initialMatch ?? "None";
        combo.IsEnabled = names.Count > 0;
        target.TextChanged += (_, _) => MatchPickerToText();
        combo.SelectionChanged += (_, _) =>
        {
            if (updating || combo.SelectedItem is not string selected)
                return;

            updating = true;
            try
            {
                if (selected == "None")
                {
                    target.Text = previousLiteralValue;
                }
                else
                {
                    var currentMatch = names.Any(name =>
                        string.Equals(name, target.Text.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (!currentMatch)
                        previousLiteralValue = target.Text;
                    target.Text = selected;
                }

                target.CaretIndex = target.Text.Length;
                target.Focus();
            }
            finally
            {
                updating = false;
            }
        };

        return combo;
    }

    private FrameworkElement CreateVariableInputRow(TextBox box, string? fallbackValue = null)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        box.Margin = new Thickness(0);
        var picker = CreateVariablePicker(box, fallbackValue);
        Grid.SetColumn(box, 0);
        Grid.SetColumn(picker, 1);
        row.Children.Add(box);
        row.Children.Add(picker);
        return row;
    }

    private static FrameworkElement WrapInput(TextBox box)
    {
        box.Margin = new Thickness(0, 0, 0, 12);
        return box;
    }

    private void AddValueOrVariableField(string label, string currentValue, Action<string> setter)
    {
        AddLabel(label);
        var box = new TextBox
        {
            Text = currentValue,
            ToolTip = "Type a value, saved variable name, or simple formula"
        };
        box.TextChanged += (_, _) =>
        {
            setter(box.Text.Trim());
            MarkDirtyAndRefreshCommandDisplay(_selectedRow?.Command?.Id);
        };
        PropertiesPanel.Children.Add(CreateVariableInputRow(box));
    }

    private void AddFilePathField(string label, string currentValue, Action<string> setter, bool saveFile = false)
    {
        AddLabel(label);
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new TextBox
        {
            Text = currentValue,
            ToolTip = "Select a file, type a path, or use a saved variable"
        };
        box.TextChanged += (_, _) =>
        {
            setter(box.Text.Trim());
            MarkDirtyAndRefreshCommandDisplay(_selectedRow?.Command?.Id);
        };

        var variablePicker = CreateVariablePicker(box);
        var browse = new Button { Content = "Browse…", MinWidth = 76, Margin = new Thickness(7, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            if (saveFile)
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Choose file",
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    AddExtension = false,
                    OverwritePrompt = false
                };
                if (dialog.ShowDialog(this) == true)
                    box.Text = dialog.FileName;
            }
            else
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Choose file",
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
                };
                if (dialog.ShowDialog(this) == true)
                    box.Text = dialog.FileName;
            }
        };

        Grid.SetColumn(box, 0);
        Grid.SetColumn(variablePicker, 1);
        Grid.SetColumn(browse, 2);
        row.Children.Add(box);
        row.Children.Add(variablePicker);
        row.Children.Add(browse);
        PropertiesPanel.Children.Add(row);
    }

    private void AddCommandOverview(MacroCommand command)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = FriendlyName(command.Type),
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            Foreground = (Brush)FindResource("TextBrush")
        });

        PropertiesPanel.Children.Add(new Border
        {
            Background = (Brush)FindResource("Panel2Brush"),
            BorderBrush = (Brush)FindResource("BorderBrushDark"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(11),
            Margin = new Thickness(0, 0, 0, 14),
            Child = panel
        });
    }

    private void AddTextField(
        string label,
        string currentValue,
        Action<string> setter,
        bool multiline = false,
        bool allowVariables = true)
    {
        AddLabel(label);
        var box = new TextBox
        {
            Text = currentValue,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 80 : 0
        };
        box.TextChanged += (_, _) =>
        {
            setter(box.Text);
            MarkDirtyAndRefreshCommandDisplay(_selectedRow?.Command?.Id);
        };
        PropertiesPanel.Children.Add(allowVariables ? CreateVariableInputRow(box) : WrapInput(box));
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
        if (string.IsNullOrWhiteSpace(text))
            return;

        PropertiesPanel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("MutedTextBrush"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10)
        });
    }

    private void AddCommandHelp(CommandType type)
    {
        AddInfo(CommandHelp.Get(type));
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

    private void MarkDirtyAndRefreshCommandDisplay(Guid? selectedId)
    {
        MarkDirty();
        if (_selectedRow?.Command is { } command && (!selectedId.HasValue || command.Id == selectedId.Value))
            _selectedRow.RefreshDisplay();
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

    private static string FriendlyName(CommandType type)
        => type == CommandType.Comment ? "Comment" : CommandCatalog.QuickLabel(type);

    // ---------------- RUNNER ----------------

    private void RebuildEngine()
    {
        _engine?.Stop();
        _engine = new MacroEngine(_project) { PlaybackSpeedPercent = EffectivePlaybackSpeed };
        _engine.StatusChanged += text => Dispatcher.InvokeAsync(() =>
        {
            StatusText.Text = text;
            _runStatusWindow?.UpdateStatus(text);
        });
        _engine.StateChanged += () => Dispatcher.InvokeAsync(() =>
        {
            UpdateRunButtons();
            UpdateMouseLockForEngineState();
        });
        _engine.CommandStarted += (sequence, id) => Dispatcher.InvokeAsync(() =>
        {
            var command = EnumerateAllCommands().FirstOrDefault(c => c.Id == id);
            var text = command is null ? $"Running: {sequence}" : $"{sequence}: {command.DisplayText()}";
            StatusText.Text = text;
            _runStatusWindow?.UpdateStatus(text);
        });
        UpdateRunButtons();
    }

    private async void RunStartButton_Click(object sender, RoutedEventArgs e)
    {
        await RunSequenceFromUi(EffectiveStartupSequence);
    }

    private async void RunCurrentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSequence is not null)
            await RunSequenceFromUi(_currentSequence.Name);
    }

    private async void TestSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRow?.Command is not { } command || _engine is null || _engine.IsRunning || _isRecording)
        {
            StatusText.Text = "Select a command to test";
            return;
        }

        Exception? error = null;
        Hide();
        FocusLastExternalWindow();

        _engine.PlaybackSpeedPercent = EffectivePlaybackSpeed;
        if (EffectiveShowHud)
        {
            _runStatusWindow = new RunStatusWindow(_appSettings.StopMacroHotkey, _appSettings.PauseMacroHotkey, EffectiveHudCorner, EffectiveHudOpacity);
            _runStatusWindow.Show();
            _runStatusWindow.UpdateStatus($"Testing: {command.DisplayText()}");
        }

        if (EffectiveLockMouse)
            EnableMouseMovementLock();

        try
        {
            await Task.Delay(120);
            await _engine.StartCommandAsync(command, "Selected Command");
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            DisableMouseMovementLock();
            if (_runStatusWindow is not null)
            {
                _runStatusWindow.Close();
                _runStatusWindow = null;
            }
            Show();
            WindowState = System.Windows.WindowState.Normal;
            Activate();
        }

        if (error is not null)
            MessageBox.Show(this, FriendlyErrorMessage(error, $"testing {command.DisplayText()}"), "Command test stopped", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void CheckMacroButton_Click(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var duplicateSequences = _project.Sequences
            .GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        foreach (var duplicate in duplicateSequences)
            errors.Add($"Sequence name '{duplicate}' is used more than once.");

        var sequenceNames = _project.Sequences.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var variableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in _project.Variables ?? new List<ProjectVariable>())
        {
            var name = RuntimeValues.NormalizeName(variable.Name);
            if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$"))
                errors.Add($"Project variable '{variable.Name}' has an invalid name.");
            else if (!variableNames.Add(name))
                errors.Add($"Project variable '{name}' is defined more than once.");
        }

        foreach (var sequence in _project.Sequences)
            ValidateCommands(sequence.Name, sequence.Commands, sequenceNames, errors, warnings);

        var hotkeys = new[]
        {
            ("Stop", _appSettings.StopMacroHotkey),
            ("Pause", _appSettings.PauseMacroHotkey),
            ("Run Start", _appSettings.RunStartHotkey),
            ("Run Current", _appSettings.RunCurrentHotkey)
        }.Where(x => !string.IsNullOrWhiteSpace(x.Item2)).ToList();
        foreach (var group in hotkeys.GroupBy(x => x.Item2.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            warnings.Add($"Hotkey {group.Key} is used by: {string.Join(", ", group.Select(x => x.Item1))}.");

        var dialog = new MacroCheckWindow(errors, warnings) { Owner = this };
        dialog.ShowDialog();
        StatusText.Text = errors.Count > 0 ? "Macro check found problems" : warnings.Count > 0 ? "Macro check found warnings" : "Macro check passed";
    }

    private void ValidateCommands(string sequenceName, IEnumerable<MacroCommand> commands, HashSet<string> sequenceNames, List<string> errors, List<string> warnings)
    {
        foreach (var command in commands)
        {
            if (!command.Enabled)
                continue;

            if (CommandCatalog.UsesColor(command.Type)
                && !GetSavedVariableNames().Any(name => name.Equals(command.ColorHex.Trim(), StringComparison.OrdinalIgnoreCase))
                && !ScreenTools.TryParseColor(command.ColorHex, out _, out _, out _))
                errors.Add($"{sequenceName}: invalid color or variable '{command.ColorHex}' in {FriendlyName(command.Type)}.");

            if (command.Type == CommandType.RunSequence
                && !command.TargetSequence.Contains('{')
                && !sequenceNames.Contains(command.TargetSequence))
                errors.Add($"{sequenceName}: sequence '{command.TargetSequence}' does not exist.");

            if (command.FailureAction == FailureAction.RunSequence
                && !string.IsNullOrWhiteSpace(command.FailureSequence)
                && !command.FailureSequence.Contains('{')
                && !sequenceNames.Contains(command.FailureSequence))
                errors.Add($"{sequenceName}: failure sequence '{command.FailureSequence}' does not exist.");

            if (command.Type is CommandType.PressKey or CommandType.KeyDown or CommandType.KeyUp or CommandType.HoldKey
                or CommandType.RepeatKey or CommandType.WaitUntilKeyPressed or CommandType.WaitUntilKeyReleased
                or CommandType.IfKeyPressed or CommandType.LoopWhileKeyPressed)
            {
                if (!string.IsNullOrWhiteSpace(command.Key) && !command.Key.Contains('{')
                    && !InputController.TryGetVirtualKey(command.Key.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? command.Key, out _))
                    warnings.Add($"{sequenceName}: key '{command.Key}' may not be recognized in {FriendlyName(command.Type)}.");
            }

            if (command.Type is CommandType.IfImage or CommandType.WaitUntilImage or CommandType.WaitUntilImageGone
                or CommandType.ClickImage or CommandType.DoubleClickImage or CommandType.MoveToImage
                or CommandType.LoopUntilImage or CommandType.LoopWhileImage or CommandType.FindImageToVariables)
            {
                var hasFolder = !string.IsNullOrWhiteSpace(command.ImageFolder);
                var hasImage = !string.IsNullOrWhiteSpace(command.ImagePath);
                if (!hasFolder && !hasImage)
                    errors.Add($"{sequenceName}: {FriendlyName(command.Type)} has no image or folder selected.");
                else if (hasFolder && !command.ImageFolder.Contains('{') && !Directory.Exists(ProjectPaths.Resolve(command.ImageFolder)))
                    errors.Add($"{sequenceName}: image folder is missing: {command.ImageFolder}.");
                else if (!hasFolder && hasImage && !command.ImagePath.Contains('{') && !File.Exists(ProjectPaths.Resolve(command.ImagePath)))
                    errors.Add($"{sequenceName}: image is missing: {command.ImagePath}.");
            }

            if ((command.Type is CommandType.ReadTextFile or CommandType.WriteTextFile) && string.IsNullOrWhiteSpace(command.FilePath))
                errors.Add($"{sequenceName}: {FriendlyName(command.Type)} has no file path.");

            if (command.Type == CommandType.RunProgram && string.IsNullOrWhiteSpace(command.ProgramPath))
                errors.Add($"{sequenceName}: Open Program / File / URL is empty.");

            if (command.Type == CommandType.RecordedActions && command.Children.Count == 0)
                warnings.Add($"{sequenceName}: Record Actions is empty.");
            else if (command.HasBody && command.Children.Count == 0)
                warnings.Add($"{sequenceName}: {FriendlyName(command.Type)} has an empty {(command.HasElse ? "THEN" : "body")} block.");

            ValidateCommands(sequenceName, command.Children, sequenceNames, errors, warnings);
            ValidateCommands(sequenceName, command.ElseChildren, sequenceNames, errors, warnings);
        }
    }

    private async Task RunSequenceFromUi(string name)
    {
        if (_engine is null || _engine.IsRunning || _isRecording)
            return;

        if (!TryPrepareRunOverrides(out var runValues))
            return;
        _engine.SetRunOverrides(runValues);

        Exception? error = null;
        Hide();
        FocusLastExternalWindow();

        _engine.PlaybackSpeedPercent = EffectivePlaybackSpeed;
        if (EffectiveShowHud)
        {
            _runStatusWindow = new RunStatusWindow(
                _appSettings.StopMacroHotkey,
                _appSettings.PauseMacroHotkey,
                EffectiveHudCorner,
                EffectiveHudOpacity);
            _runStatusWindow.Show();
            _runStatusWindow.UpdateStatus($"Starting: {name}");
        }

        if (EffectiveLockMouse)
            EnableMouseMovementLock();

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
            DisableMouseMovementLock();

            if (_runStatusWindow is not null)
            {
                _runStatusWindow.Close();
                _runStatusWindow = null;
            }

            Show();
            WindowState = System.Windows.WindowState.Normal;
            Activate();
        }

        if (error is not null)
            MessageBox.Show(this, FriendlyErrorMessage(error, $"running {name}"), "Macro stopped", MessageBoxButton.OK, MessageBoxImage.Error);
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
        RunFromHereButton.IsEnabled = !running && !_isRecording && _selectedRow?.Command is not null;
        TestSelectedButton.IsEnabled = !running && !_isRecording && _selectedRow?.Command is not null;
        CheckMacroButton.IsEnabled = !running && !_isRecording;
        StopButton.IsEnabled = running;
        PauseButton.IsEnabled = running;
        PauseButton.Content = _engine?.IsPaused == true ? "▶ Resume" : "⏸ Pause";
        UpdateUndoButtons();
    }

    // ---------------- GLOBAL HOTKEYS + MOUSE LOCK ----------------

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
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var injected = (data.flags & 0x10) != 0;

            if (!injected)
            {
                if (message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP)
                {
                    _hookKeysDown.Remove(data.vkCode);
                }
                else if ((message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN)
                         && _hookKeysDown.Add(data.vkCode))
                {
                    if (HotkeyMatches(_appSettings.StopMacroHotkey, data.vkCode))
                    {
                        Dispatcher.InvokeAsync(() => _engine?.Stop());
                    }
                    else if (HotkeyMatches(_appSettings.PauseMacroHotkey, data.vkCode))
                    {
                        Dispatcher.InvokeAsync(() => _engine?.TogglePause());
                    }
                    else if (HotkeyMatches(_appSettings.RunStartHotkey, data.vkCode))
                    {
                        Dispatcher.InvokeAsync(() => { _ = RunSequenceFromUi(EffectiveStartupSequence); });
                    }
                    else if (HotkeyMatches(_appSettings.RunCurrentHotkey, data.vkCode))
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (_currentSequence is not null)
                                _ = RunSequenceFromUi(_currentSequence.Name);
                        });
                    }
                }
            }
        }

        // Global hotkeys are non-blocking and still pass through to the focused app.
        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static bool HotkeyMatches(string? expression, uint eventVk)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var parts = expression.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        var needCtrl = false;
        var needShift = false;
        var needAlt = false;
        var needWin = false;
        string? keyPart = null;

        foreach (var raw in parts)
        {
            if (raw.Equals("CTRL", StringComparison.OrdinalIgnoreCase) || raw.Equals("CONTROL", StringComparison.OrdinalIgnoreCase))
                needCtrl = true;
            else if (raw.Equals("SHIFT", StringComparison.OrdinalIgnoreCase))
                needShift = true;
            else if (raw.Equals("ALT", StringComparison.OrdinalIgnoreCase))
                needAlt = true;
            else if (raw.Equals("WIN", StringComparison.OrdinalIgnoreCase) || raw.Equals("LWIN", StringComparison.OrdinalIgnoreCase) || raw.Equals("RWIN", StringComparison.OrdinalIgnoreCase))
                needWin = true;
            else if (keyPart is null)
                keyPart = raw;
            else
                return false;
        }

        if (keyPart is null || !InputController.TryGetVirtualKey(keyPart, out var keyVk) || keyVk != eventVk)
            return false;

        var ctrl = (NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0;
        var shift = (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0;
        var alt = (NativeMethods.GetAsyncKeyState(0x12) & 0x8000) != 0;
        var win = (NativeMethods.GetAsyncKeyState(0x5B) & 0x8000) != 0
                  || (NativeMethods.GetAsyncKeyState(0x5C) & 0x8000) != 0;

        return (!needCtrl || ctrl)
               && (!needShift || shift)
               && (!needAlt || alt)
               && (!needWin || win);
    }

    private void UpdateMouseLockForEngineState()
    {
        if (EffectiveLockMouse
            && _engine?.IsRunning == true
            && _engine.IsPaused == false)
        {
            EnableMouseMovementLock();
        }
        else
        {
            DisableMouseMovementLock();
        }
    }

    private void EnableMouseMovementLock()
    {
        if (_mouseLockHook != IntPtr.Zero)
            return;

        _mouseLockProc = MouseLockHookCallback;
        var module = NativeMethods.GetModuleHandle(null);
        _mouseLockHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseLockProc, module, 0);
    }

    private void DisableMouseMovementLock()
    {
        if (_mouseLockHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseLockHook);
            _mouseLockHook = IntPtr.Zero;
        }
        _mouseLockProc = null;
    }

    private IntPtr MouseLockHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == NativeMethods.WM_MOUSEMOVE)
        {
            var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            var injected = (data.flags & 0x01) != 0 || data.dwExtraInfo == InputController.MacroMouseInputTag;
            if (!injected)
                return (IntPtr)1;
        }

        return NativeMethods.CallNextHookEx(_mouseLockHook, nCode, wParam, lParam);
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

        if (!_skipSavePromptForUpdate && !ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }

        _foregroundTimer.Stop();
        DisableMouseMovementLock();
        if (_runStatusWindow is not null)
        {
            _runStatusWindow.Close();
            _runStatusWindow = null;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
    }
}
