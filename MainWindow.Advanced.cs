using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MacroMaker;

public partial class MainWindow
{
    private readonly List<MacroCommand> _copiedCommands = new();

    private bool EffectiveLockMouse => _appSettings.LockMouseMovementWhileRunning;
    private bool EffectiveShowHud => _appSettings.ShowRunStatusHud;
    private int EffectivePlaybackSpeed => Math.Clamp(_appSettings.PlaybackSpeedPercent, 10, 400);
    private HudCorner EffectiveHudCorner => _appSettings.RunHudCorner;
    private int EffectiveHudOpacity => Math.Clamp(_appSettings.RunHudOpacityPercent, 35, 100);
    private string EffectiveStartupSequence => "Starting Sequence";


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


    private static string FriendlyErrorMessage(Exception error, string action)
    {
        var detail = error.Message.Trim();
        return error switch
        {
            TimeoutException => $"MacroMaker waited too long while {action}.\n\n{detail}",
            FileNotFoundException => $"A file or image needed while {action} could not be found.\n\n{detail}",
            DirectoryNotFoundException => $"A folder needed while {action} could not be found.\n\n{detail}",
            UnauthorizedAccessException => $"Windows blocked access while {action}.\n\nTry choosing a different file/folder or check its permissions.\n\n{detail}",
            InvalidOperationException => $"MacroMaker could not continue while {action}.\n\n{detail}",
            _ => $"Something stopped MacroMaker while {action}.\n\n{detail}"
        };
    }

    private void VariablesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine?.IsRunning == true || _isRecording)
            return;

        var dialog = new VariablesManagerWindow(_project.Variables ?? new List<ProjectVariable>()) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _project.Variables = dialog.Variables.Select(v => v.DeepClone()).ToList();
        _project.RuntimeSettings ??= new MacroRuntimeSettings();
        _project.RuntimeSettings.PromptForVariablesOnRun = _project.Variables.Any(variable => variable.UserEditable);
        MarkDirty();
        RebuildEngine();
        if (_selectedRow?.Command is { } selected)
            BuildProperties(selected);
        StatusText.Text = "Variables updated";
    }

    private void TestMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = TestMenuButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
            StaysOpen = false
        };

        var current = new MenuItem { Header = "Run Current Tab" };
        current.IsEnabled = _currentSequence is not null && _engine?.IsRunning != true && !_isRecording;
        current.Click += RunCurrentButton_Click;

        var fromHere = new MenuItem { Header = "Run From Here" };
        fromHere.IsEnabled = _selectedRow?.Command is not null && _engine?.IsRunning != true && !_isRecording;
        fromHere.Click += RunFromHereButton_Click;

        var test = new MenuItem { Header = "Test Selected Command" };
        test.IsEnabled = _selectedRow?.Command is not null && _engine?.IsRunning != true && !_isRecording;
        test.Click += TestSelectedButton_Click;

        var check = new MenuItem { Header = "Check Macro" };
        check.IsEnabled = _engine?.IsRunning != true && !_isRecording;
        check.Click += CheckMacroButton_Click;

        menu.Items.Add(current);
        menu.Items.Add(fromHere);
        menu.Items.Add(test);
        menu.Items.Add(check);
        menu.IsOpen = true;
    }

    private void CopyCommandButton_Click(object sender, RoutedEventArgs e)
    {
        var rows = GetSelectedCommandRows();
        if (rows.Count == 0)
            return;

        _copiedCommands.Clear();
        _copiedCommands.AddRange(rows.Select(row => row.Command!.DeepClone()));
        StatusText.Text = rows.Count == 1 ? "Command copied" : $"{rows.Count} commands copied";
    }

    private void PasteCommandButton_Click(object sender, RoutedEventArgs e)
    {
        if (_copiedCommands.Count == 0 || _currentSequence is null)
            return;

        var clones = _copiedCommands.Select(command => command.DeepClone()).ToList();
        if (_selectedRow?.Owner is { } owner && _selectedRow.Command is { } selected)
        {
            var index = owner.IndexOf(selected);
            owner.InsertRange(Math.Clamp(index + 1, 0, owner.Count), clones);
        }
        else
        {
            _currentSequence.Commands.AddRange(clones);
        }
        MarkDirty();
        var ids = clones.Select(command => command.Id).ToList();
        RefreshCommandList(ids.FirstOrDefault(), ids);
        StatusText.Text = clones.Count == 1 ? "Command pasted" : $"{clones.Count} commands pasted";
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
            MessageBox.Show(this, FriendlyErrorMessage(error, $"running {label}"), "Macro stopped", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void AddCommonCommandProperties(MacroCommand command)
    {
        var enabled = new CheckBox
        {
            Content = "Run this command",
            IsChecked = command.Enabled,
            Margin = new Thickness(0, 0, 0, 10)
        };
        enabled.Checked += (_, _) => { if (!command.Enabled) { command.Enabled = true; MarkDirtyAndRefresh(command.Id); } };
        enabled.Unchecked += (_, _) => { if (command.Enabled) { command.Enabled = false; MarkDirtyAndRefresh(command.Id); } };
        PropertiesPanel.Children.Add(enabled);

        AddTextField("Custom name (optional)", command.CustomName, value => command.CustomName = value, allowVariables: false);
    }

    private void AddLocationOptionsExpander(MacroCommand command, bool includeEnd)
    {
        var panel = new StackPanel { Margin = new Thickness(2, 7, 2, 2) };

        panel.Children.Add(new TextBlock
        {
            Text = "Location is based on",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        });

        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        var options = new[]
        {
            (Mode: CoordinateMode.Screen, Label: "Screen"),
            (Mode: CoordinateMode.ActiveWindow, Label: "Active window"),
            (Mode: CoordinateMode.RelativeToMouse, Label: "Current mouse position")
        };
        foreach (var option in options)
            combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Mode });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is CoordinateMode mode && mode == command.CoordinateMode);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: CoordinateMode mode } && mode != command.CoordinateMode)
            {
                command.CoordinateMode = mode;
                MarkDirtyAndRefresh(command.Id);
            }
        };
        panel.Children.Add(combo);
        panel.Children.Add(new TextBlock
        {
            Text = "Screen is best for most macros.",
            Foreground = (Brush)Application.Current.FindResource("MutedTextBrush"),
            FontSize = 11
        });

        var expander = new Expander
        {
            Header = "Location Options",
            IsExpanded = _expandedLocationOptions.Contains(command.Id),
            Foreground = (Brush)Application.Current.FindResource("TextBrush"),
            Content = new Border
            {
                Background = (Brush)Application.Current.FindResource("Panel2Brush"),
                BorderBrush = (Brush)Application.Current.FindResource("BorderBrushSoft"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(11),
                Margin = new Thickness(0, 7, 0, 0),
                Child = panel
            },
            Margin = new Thickness(0, 0, 0, 10)
        };
        expander.Expanded += (_, _) => _expandedLocationOptions.Add(command.Id);
        expander.Collapsed += (_, _) => { if (!_rebuildingProperties) _expandedLocationOptions.Remove(command.Id); };
        PropertiesPanel.Children.Add(expander);
    }

    private void AddCoordinateModeFields(MacroCommand command)
    {
        AddLabel("Location is based on");
        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        var options = new[]
        {
            (Mode: CoordinateMode.Screen, Label: "Screen"),
            (Mode: CoordinateMode.ActiveWindow, Label: "Active window"),
            (Mode: CoordinateMode.RelativeToMouse, Label: "Current mouse position")
        };
        foreach (var option in options)
            combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Mode });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Tag is CoordinateMode m && m == command.CoordinateMode);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: CoordinateMode mode } && mode != command.CoordinateMode)
            {
                command.CoordinateMode = mode;
                MarkDirtyAndRefresh(command.Id);
            }
        };
        PropertiesPanel.Children.Add(combo);
        AddInfo("Screen = exact screen spot. Active window = spot inside the focused app. Current mouse = offset from where the mouse is now.");
    }

    private void AddCoordinateExpressionFields(MacroCommand command, bool includeEnd = false)
    {
        AddLabel("Use saved X / Y instead (optional)");
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var x = CreateValueBox(command.XExpression, new Thickness(0, 0, 4, 0), "X: type a number, saved variable, or formula like FoundX+20");
        var y = CreateValueBox(command.YExpression, new Thickness(4, 0, 0, 0), "Y: type a number, saved variable, or formula like FoundY+20");
        x.TextChanged += (_, _) => { command.XExpression = x.Text.Trim(); MarkDirtyAndRefreshCommandDisplay(command.Id); };
        y.TextChanged += (_, _) => { command.YExpression = y.Text.Trim(); MarkDirtyAndRefreshCommandDisplay(command.Id); };
        Grid.SetColumn(x, 0); Grid.SetColumn(y, 1); grid.Children.Add(x); grid.Children.Add(y);
        PropertiesPanel.Children.Add(grid);

        if (!includeEnd)
            return;

        AddLabel("Use saved end X / Y instead (optional)");
        var endGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        endGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        endGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var ex = CreateValueBox(command.EndXExpression, new Thickness(0, 0, 4, 0));
        var ey = CreateValueBox(command.EndYExpression, new Thickness(4, 0, 0, 0));
        ex.TextChanged += (_, _) => { command.EndXExpression = ex.Text.Trim(); MarkDirtyAndRefreshCommandDisplay(command.Id); };
        ey.TextChanged += (_, _) => { command.EndYExpression = ey.Text.Trim(); MarkDirtyAndRefreshCommandDisplay(command.Id); };
        Grid.SetColumn(ex, 0); Grid.SetColumn(ey, 1); endGrid.Children.Add(ex); endGrid.Children.Add(ey);
        PropertiesPanel.Children.Add(endGrid);
    }

    private void AddEndCoordinateExpressionFields(MacroCommand command)
    {
        AddLabel("Use saved end X / Y instead (optional)");
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var x = CreateValueBox(command.EndXExpression, new Thickness(0, 0, 4, 0));
        var y = CreateValueBox(command.EndYExpression, new Thickness(4, 0, 0, 0));
        x.TextChanged += (_, _) => { command.EndXExpression = x.Text.Trim(); MarkDirtyAndRefreshCommandDisplay(command.Id); };
        y.TextChanged += (_, _) => { command.EndYExpression = y.Text.Trim(); MarkDirtyAndRefreshCommandDisplay(command.Id); };
        Grid.SetColumn(x, 0); Grid.SetColumn(y, 1); grid.Children.Add(x); grid.Children.Add(y);
        PropertiesPanel.Children.Add(grid);
    }

    private void AddVariableConditionFields(MacroCommand command)
    {
        AddExistingVariableField("Variable", command.VariableName, value => command.VariableName = value);
        AddLabel("Check");
        var compare = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        var options = new[]
        {
            (Mode: VariableCompareMode.Equals, Label: "Equals"),
            (Mode: VariableCompareMode.NotEquals, Label: "Does not equal"),
            (Mode: VariableCompareMode.GreaterThan, Label: "Is greater than"),
            (Mode: VariableCompareMode.GreaterThanOrEqual, Label: "Is at least"),
            (Mode: VariableCompareMode.LessThan, Label: "Is less than"),
            (Mode: VariableCompareMode.LessThanOrEqual, Label: "Is at most"),
            (Mode: VariableCompareMode.Contains, Label: "Contains text"),
            (Mode: VariableCompareMode.StartsWith, Label: "Starts with"),
            (Mode: VariableCompareMode.EndsWith, Label: "Ends with")
        };
        foreach (var option in options)
            compare.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Mode });
        compare.SelectedItem = compare.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Tag is VariableCompareMode m && m == command.VariableCompareMode);
        compare.SelectionChanged += (_, _) =>
        {
            if (compare.SelectedItem is ComboBoxItem { Tag: VariableCompareMode mode })
            {
                command.VariableCompareMode = mode;
                MarkDirtyAndRefresh(command.Id);
            }
        };
        PropertiesPanel.Children.Add(compare);
        AddValueOrVariableField("Value to compare with", command.VariableValue, value => command.VariableValue = value);
    }

    private void AddFailureBehaviorFields(MacroCommand command)
    {
        if (!CommandCatalog.CanFail(command.Type))
            return;

        AddLabel("If this cannot finish");
        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        var options = new[]
        {
            (Action: FailureAction.Continue, Label: "Keep going"),
            (Action: FailureAction.StopMacro, Label: "Stop the macro"),
            (Action: FailureAction.Retry, Label: "Try again"),
            (Action: FailureAction.RunSequence, Label: "Run another tab")
        };
        foreach (var option in options)
            combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Action });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Tag is FailureAction a && a == command.FailureAction);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: FailureAction action } && action != command.FailureAction)
            {
                command.FailureAction = action;
                MarkDirtyAndRefresh(command.Id);
                _expandedMoreOptions.Add(command.Id);
                BuildProperties(command);
            }
        };
        PropertiesPanel.Children.Add(combo);

        if (command.FailureAction == FailureAction.Retry)
        {
            AddNumberField("How many retries", command.FailureRetryCount, 0, 100, value => command.FailureRetryCount = value);
            AddNumberField("Wait between retries (ms)", command.FailureRetryDelayMs, 0, 60000, value => command.FailureRetryDelayMs = value);
        }
        else if (command.FailureAction == FailureAction.RunSequence)
        {
            AddLabel("Tab to run");
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

    private FrameworkElement CreateInlineNumberInput(MacroCommand command, string propertyName, int currentValue, int min, int max, Action<int> setter)
    {
        command.ValueExpressions ??= new Dictionary<string, string>();
        var expression = command.ValueExpressions.TryGetValue(propertyName, out var saved) ? saved : string.Empty;
        var box = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(expression) ? currentValue.ToString() : expression,
            ToolTip = "Type a number, saved variable, or formula"
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
                command.ValueExpressions.Remove(propertyName);
            }
            else if (string.IsNullOrWhiteSpace(text))
            {
                command.ValueExpressions.Remove(propertyName);
            }
            else
            {
                command.ValueExpressions[propertyName] = text;
            }
            MarkDirtyAndRefreshCommandDisplay(command.Id);
        };
        box.LostFocus += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(box.Text) && !int.TryParse(box.Text, out _))
                return;
            if (!int.TryParse(box.Text, out var value))
            {
                box.Text = lastNumber.ToString();
                return;
            }
            value = Math.Clamp(value, min, max);
            lastNumber = value;
            setter(value);
            if (box.Text != value.ToString()) box.Text = value.ToString();
        };
        return CreateVariableInputRow(box, currentValue.ToString());
    }

    private void AddMoreOptions(MacroCommand command)
    {
        var panel = new StackPanel { Margin = new Thickness(2, 8, 2, 2) };

        var enabled = new CheckBox
        {
            Content = "Run this command",
            IsChecked = command.Enabled,
            Margin = new Thickness(0, 0, 0, 10)
        };
        enabled.Checked += (_, _) =>
        {
            if (!command.Enabled)
            {
                command.Enabled = true;
                MarkDirtyAndRefresh(command.Id);
            }
        };
        enabled.Unchecked += (_, _) =>
        {
            if (command.Enabled)
            {
                command.Enabled = false;
                MarkDirtyAndRefresh(command.Id);
            }
        };
        panel.Children.Add(enabled);

        panel.Children.Add(new TextBlock
        {
            Text = "Custom name (optional)",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        });
        var customName = new TextBox { Text = command.CustomName, Margin = new Thickness(0, 0, 0, 10) };
        customName.TextChanged += (_, _) =>
        {
            var value = customName.Text.Trim();
            if (value == command.CustomName)
                return;
            command.CustomName = value;
            MarkDirtyAndRefreshCommandDisplay(command.Id);
        };
        panel.Children.Add(customName);

        if (CommandCatalog.CanFail(command.Type))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "If this command cannot finish",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 7, 0, 5)
            });

            var failure = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            var choices = new[]
            {
                (Action: FailureAction.Continue, Label: "Keep going"),
                (Action: FailureAction.StopMacro, Label: "Stop the macro"),
                (Action: FailureAction.Retry, Label: "Try again"),
                (Action: FailureAction.RunSequence, Label: "Run another tab")
            };
            foreach (var choice in choices)
                failure.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Action });
            failure.SelectedItem = failure.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is FailureAction action && action == command.FailureAction);
            failure.SelectionChanged += (_, _) =>
            {
                if (failure.SelectedItem is not ComboBoxItem { Tag: FailureAction action } || action == command.FailureAction)
                    return;
                command.FailureAction = action;
                MarkDirtyAndRefresh(command.Id);
                _expandedMoreOptions.Add(command.Id);
                BuildProperties(command);
            };
            panel.Children.Add(failure);

            if (command.FailureAction == FailureAction.Retry)
            {
                panel.Children.Add(new TextBlock { Text = "Retries", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
                panel.Children.Add(CreateInlineNumberInput(command, nameof(MacroCommand.FailureRetryCount), command.FailureRetryCount, 0, 100, value => command.FailureRetryCount = value));

                panel.Children.Add(new TextBlock { Text = "Wait between retries (ms)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
                panel.Children.Add(CreateInlineNumberInput(command, nameof(MacroCommand.FailureRetryDelayMs), command.FailureRetryDelayMs, 0, 60000, value => command.FailureRetryDelayMs = value));
            }
            else if (command.FailureAction == FailureAction.RunSequence)
            {
                panel.Children.Add(new TextBlock { Text = "Tab to run", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
                var sequence = new ComboBox
                {
                    ItemsSource = _project.Sequences.Select(sequenceItem => sequenceItem.Name).ToList(),
                    SelectedItem = command.FailureSequence,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                if (sequence.SelectedItem is null && _project.Sequences.Count > 0)
                    sequence.SelectedIndex = 0;
                sequence.SelectionChanged += (_, _) =>
                {
                    if (sequence.SelectedItem is string name && name != command.FailureSequence)
                    {
                        command.FailureSequence = name;
                        MarkDirtyAndRefresh(command.Id);
                    }
                };
                panel.Children.Add(sequence);
            }
        }

        panel.Children.Add(new TextBlock
        {
            Text = "Most macros can leave these settings alone.",
            Foreground = (Brush)Application.Current.FindResource("MutedTextBrush"),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0)
        });

        var expander = new Expander
        {
            Header = "More Options",
            IsExpanded = _expandedMoreOptions.Contains(command.Id),
            Foreground = (Brush)Application.Current.FindResource("TextBrush"),
            Background = Brushes.Transparent,
            Content = new Border
            {
                Background = (Brush)Application.Current.FindResource("Panel2Brush"),
                BorderBrush = (Brush)Application.Current.FindResource("BorderBrushSoft"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(11),
                Margin = new Thickness(0, 7, 0, 0),
                Child = panel
            },
            Margin = new Thickness(0, 12, 0, 4)
        };
        expander.Expanded += (_, _) => _expandedMoreOptions.Add(command.Id);
        expander.Collapsed += (_, _) => { if (!_rebuildingProperties) _expandedMoreOptions.Remove(command.Id); };
        PropertiesPanel.Children.Add(expander);
    }

    private void AddStorePointVariableFields(MacroCommand command)
    {
        AddInfo("Saves the found spot so another command can use it.");
        AddVariableNameField("Save X as", command.StoreXVariable, value => command.StoreXVariable = value);
        AddVariableNameField("Save Y as", command.StoreYVariable, value => command.StoreYVariable = value);
    }

    private void AddFileDataFields(MacroCommand command, bool showText, bool showVariable, bool showAppend)
    {
        AddFilePathField("File path", command.FilePath, value => command.FilePath = value, saveFile: showText);
        if (showVariable)
            AddVariableNameField("Save as", command.VariableName, value => command.VariableName = value);
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
