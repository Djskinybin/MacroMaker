using Microsoft.Win32;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MacroMaker;

public partial class MainWindow
{
    private MacroCommand? _copiedCommand;

    private bool UseProjectRuntimeSettings => _project.RuntimeSettings?.UseProjectRuntimeSettings == true;
    private bool EffectiveLockMouse => UseProjectRuntimeSettings ? _project.RuntimeSettings.LockMouseMovementWhileRunning : _appSettings.LockMouseMovementWhileRunning;
    private bool EffectiveShowHud => UseProjectRuntimeSettings ? _project.RuntimeSettings.ShowRunStatusHud : _appSettings.ShowRunStatusHud;
    private int EffectivePlaybackSpeed => UseProjectRuntimeSettings ? Math.Clamp(_project.RuntimeSettings.PlaybackSpeedPercent, 10, 400) : Math.Clamp(_appSettings.PlaybackSpeedPercent, 10, 400);
    private HudCorner EffectiveHudCorner => UseProjectRuntimeSettings ? _project.RuntimeSettings.HudCorner : _appSettings.RunHudCorner;
    private int EffectiveHudOpacity => UseProjectRuntimeSettings ? Math.Clamp(_project.RuntimeSettings.HudOpacityPercent, 35, 100) : Math.Clamp(_appSettings.RunHudOpacityPercent, 35, 100);
    private string EffectiveStartupSequence => UseProjectRuntimeSettings && _project.Sequences.Any(s => s.Name.Equals(_project.RuntimeSettings.StartupSequence, StringComparison.OrdinalIgnoreCase))
        ? _project.RuntimeSettings.StartupSequence
        : "Starting Sequence";


    private bool TryPrepareRunOverrides(out IReadOnlyDictionary<string, string>? values)
    {
        values = null;
        if (_project.RuntimeSettings?.PromptForVariablesOnRun != true)
            return true;

        var editable = (_project.Variables ?? new List<ProjectVariable>()).Where(v => v.UserEditable).ToList();
        if (editable.Count == 0)
            return true;

        var dialog = new MacroInputsWindow(_project.Name, editable) { Owner = this };
        if (dialog.ShowDialog() != true)
            return false;

        values = dialog.Values;
        return true;
    }

    private void MacroSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine?.IsRunning == true || _isRecording)
            return;

        var dialog = new MacroSettingsWindow(_project) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _project.RuntimeSettings = dialog.Settings;
        _project.Variables = dialog.Variables.Select(v => v.DeepClone()).ToList();
        MarkDirty();
        RebuildEngine();
        StatusText.Text = "Macro settings updated";
    }

    private void ExportProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveProject(false))
            return;
        if (string.IsNullOrWhiteSpace(_projectPath) || !Directory.Exists(_projectPath))
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export MacroMaker Project",
            Filter = "ZIP archive (*.zip)|*.zip",
            FileName = SanitizeFileName(_project.Name) + ".zip"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            if (File.Exists(dialog.FileName))
                File.Delete(dialog.FileName);
            ZipFile.CreateFromDirectory(_projectPath, dialog.FileName, CompressionLevel.Optimal, includeBaseDirectory: true);
            StatusText.Text = "Project exported";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not export project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyCommandButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRow?.Command is not { } command)
            return;
        _copiedCommand = command.DeepClone();
        StatusText.Text = "Command copied";
    }

    private void PasteCommandButton_Click(object sender, RoutedEventArgs e)
    {
        if (_copiedCommand is null || _currentSequence is null)
            return;

        var clone = _copiedCommand.DeepClone();
        if (_selectedRow?.Owner is { } owner && _selectedRow.Command is { } selected)
        {
            var index = owner.IndexOf(selected);
            owner.Insert(Math.Clamp(index + 1, 0, owner.Count), clone);
        }
        else
        {
            _currentSequence.Commands.Add(clone);
        }
        MarkDirty();
        RefreshCommandList(clone.Id);
    }

    private async void RunFromHereButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRow?.Command is not { } command || _selectedRow.Owner is null || _engine is null || _engine.IsRunning || _isRecording)
        {
            StatusText.Text = "Select a command first";
            return;
        }

        var index = _selectedRow.Owner.IndexOf(command);
        if (index < 0)
            return;
        var commands = _selectedRow.Owner.Skip(index).ToList();
        await RunCommandsFromUi(commands, $"{_currentSequence?.Name ?? "Sequence"} from here");
    }

    private async Task RunCommandsFromUi(IReadOnlyList<MacroCommand> commands, string label)
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
            _runStatusWindow = new RunStatusWindow(_appSettings.StopMacroHotkey, _appSettings.PauseMacroHotkey, EffectiveHudCorner, EffectiveHudOpacity);
            _runStatusWindow.Show();
            _runStatusWindow.UpdateStatus($"Starting: {label}");
        }

        if (EffectiveLockMouse)
            EnableMouseMovementLock();

        try
        {
            await Task.Delay(120);
            await _engine.StartCommandsAsync(commands, label);
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            DisableMouseMovementLock();
            _runStatusWindow?.Close();
            _runStatusWindow = null;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        if (error is not null)
            MessageBox.Show(this, error.Message, "Macro stopped", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void AddCommonCommandProperties(MacroCommand command)
    {
        var enabled = new CheckBox
        {
            Content = "Enabled",
            IsChecked = command.Enabled,
            Margin = new Thickness(0, 0, 0, 10)
        };
        enabled.Checked += (_, _) => { if (!command.Enabled) { command.Enabled = true; MarkDirtyAndRefresh(command.Id); } };
        enabled.Unchecked += (_, _) => { if (command.Enabled) { command.Enabled = false; MarkDirtyAndRefresh(command.Id); } };
        PropertiesPanel.Children.Add(enabled);

        AddTextField("Label (optional)", command.CustomName, value => command.CustomName = value);
    }

    private void AddCoordinateModeFields(MacroCommand command)
    {
        AddLabel("Coordinates relative to");
        var combo = new ComboBox
        {
            ItemsSource = Enum.GetValues<CoordinateMode>(),
            SelectedItem = command.CoordinateMode,
            Margin = new Thickness(0, 0, 0, 10)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is CoordinateMode mode && mode != command.CoordinateMode)
            {
                command.CoordinateMode = mode;
                MarkDirtyAndRefresh(command.Id);
            }
        };
        PropertiesPanel.Children.Add(combo);
    }

    private void AddCoordinateExpressionFields(MacroCommand command, bool includeEnd = false)
    {
        AddLabel("Variable / math override (optional)");
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var x = new TextBox { Text = command.XExpression, Margin = new Thickness(0, 0, 4, 0), ToolTip = "X, e.g. {FoundX}+20" };
        var y = new TextBox { Text = command.YExpression, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Y, e.g. {FoundY}" };
        x.LostFocus += (_, _) => { command.XExpression = x.Text.Trim(); MarkDirtyAndRefresh(command.Id); };
        y.LostFocus += (_, _) => { command.YExpression = y.Text.Trim(); MarkDirtyAndRefresh(command.Id); };
        Grid.SetColumn(x, 0); Grid.SetColumn(y, 1); grid.Children.Add(x); grid.Children.Add(y);
        PropertiesPanel.Children.Add(grid);

        if (!includeEnd)
            return;

        AddLabel("End X / Y variable override (optional)");
        var endGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        endGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        endGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var ex = new TextBox { Text = command.EndXExpression, Margin = new Thickness(0, 0, 4, 0) };
        var ey = new TextBox { Text = command.EndYExpression, Margin = new Thickness(4, 0, 0, 0) };
        ex.LostFocus += (_, _) => { command.EndXExpression = ex.Text.Trim(); MarkDirtyAndRefresh(command.Id); };
        ey.LostFocus += (_, _) => { command.EndYExpression = ey.Text.Trim(); MarkDirtyAndRefresh(command.Id); };
        Grid.SetColumn(ex, 0); Grid.SetColumn(ey, 1); endGrid.Children.Add(ex); endGrid.Children.Add(ey);
        PropertiesPanel.Children.Add(endGrid);
    }

    private void AddEndCoordinateExpressionFields(MacroCommand command)
    {
        AddLabel("End X / Y variable override (optional)");
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var x = new TextBox { Text = command.EndXExpression, Margin = new Thickness(0, 0, 4, 0) };
        var y = new TextBox { Text = command.EndYExpression, Margin = new Thickness(4, 0, 0, 0) };
        x.LostFocus += (_, _) => { command.EndXExpression = x.Text.Trim(); MarkDirtyAndRefresh(command.Id); };
        y.LostFocus += (_, _) => { command.EndYExpression = y.Text.Trim(); MarkDirtyAndRefresh(command.Id); };
        Grid.SetColumn(x, 0); Grid.SetColumn(y, 1); grid.Children.Add(x); grid.Children.Add(y);
        PropertiesPanel.Children.Add(grid);
    }

    private void AddVariableConditionFields(MacroCommand command)
    {
        AddTextField("Variable name", command.VariableName, value => command.VariableName = RuntimeValues.NormalizeName(value));
        AddLabel("Comparison");
        var compare = new ComboBox { ItemsSource = Enum.GetValues<VariableCompareMode>(), SelectedItem = command.VariableCompareMode, Margin = new Thickness(0, 0, 0, 10) };
        compare.SelectionChanged += (_, _) =>
        {
            if (compare.SelectedItem is VariableCompareMode mode)
            {
                command.VariableCompareMode = mode;
                MarkDirtyAndRefresh(command.Id);
            }
        };
        PropertiesPanel.Children.Add(compare);
        AddTextField("Compare to", command.VariableValue, value => command.VariableValue = value);
    }

    private void AddFailureBehaviorFields(MacroCommand command)
    {
        if (!CommandCatalog.CanFail(command.Type))
            return;

        AddLabel("If this fails / times out");
        var combo = new ComboBox { ItemsSource = Enum.GetValues<FailureAction>(), SelectedItem = command.FailureAction, Margin = new Thickness(0, 0, 0, 10) };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is FailureAction action && action != command.FailureAction)
            {
                command.FailureAction = action;
                MarkDirtyAndRefresh(command.Id);
                BuildProperties(command);
            }
        };
        PropertiesPanel.Children.Add(combo);

        if (command.FailureAction == FailureAction.Retry)
        {
            AddNumberField("Retries", command.FailureRetryCount, 0, 100, value => command.FailureRetryCount = value);
            AddNumberField("Delay between retries (ms)", command.FailureRetryDelayMs, 0, 60000, value => command.FailureRetryDelayMs = value);
        }
        else if (command.FailureAction == FailureAction.RunSequence)
        {
            AddLabel("Run sequence on failure");
            var sequence = new ComboBox
            {
                ItemsSource = _project.Sequences.Select(s => s.Name).ToList(),
                SelectedItem = command.FailureSequence,
                Margin = new Thickness(0, 0, 0, 10)
            };
            if (sequence.SelectedItem is null && _project.Sequences.Count > 0) sequence.SelectedIndex = 0;
            sequence.SelectionChanged += (_, _) =>
            {
                if (sequence.SelectedItem is string name)
                {
                    command.FailureSequence = name;
                    MarkDirtyAndRefresh(command.Id);
                }
            };
            PropertiesPanel.Children.Add(sequence);
        }
    }

    private void AddStorePointVariableFields(MacroCommand command)
    {
        AddLabel("Save found location into variables");
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var x = new TextBox { Text = command.StoreXVariable, Margin = new Thickness(0, 0, 4, 0) };
        var y = new TextBox { Text = command.StoreYVariable, Margin = new Thickness(4, 0, 0, 0) };
        x.LostFocus += (_, _) => { command.StoreXVariable = RuntimeValues.NormalizeName(x.Text); MarkDirtyAndRefresh(command.Id); };
        y.LostFocus += (_, _) => { command.StoreYVariable = RuntimeValues.NormalizeName(y.Text); MarkDirtyAndRefresh(command.Id); };
        Grid.SetColumn(x, 0); Grid.SetColumn(y, 1); grid.Children.Add(x); grid.Children.Add(y);
        PropertiesPanel.Children.Add(grid);
    }

    private void AddFileDataFields(MacroCommand command, bool showText, bool showVariable, bool showAppend)
    {
        AddTextField("File path", command.FilePath, value => command.FilePath = value);
        if (showVariable)
            AddTextField("Save into variable", command.VariableName, value => command.VariableName = RuntimeValues.NormalizeName(value));
        if (showText)
            AddTextField("Text", command.Text, value => command.Text = value, true);
        if (showAppend)
        {
            var append = new CheckBox { Content = "Append instead of replace", IsChecked = command.AppendFile, Margin = new Thickness(0, 0, 0, 10) };
            append.Checked += (_, _) => { command.AppendFile = true; MarkDirtyAndRefresh(command.Id); };
            append.Unchecked += (_, _) => { command.AppendFile = false; MarkDirtyAndRefresh(command.Id); };
            PropertiesPanel.Children.Add(append);
        }
    }
}
