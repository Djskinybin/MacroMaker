using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MacroMaker;

public sealed class CommandDefaultsWindow : Window
{
    private readonly Dictionary<CommandType, CommandDefaultProfile> _profiles;
    private readonly StackPanel _categoryPanel = new();
    private readonly StackPanel _commandPanel = new();
    private readonly StackPanel _propertiesPanel = new();
    private readonly TextBlock _commandTitle = new();
    private Button? _activeCategoryButton;
    private Button? _activeCommandButton;
    private int _currentCategory;
    private CommandType _currentType;

    public CommandDefaultsWindow(IEnumerable<CommandDefaultProfile> current)
    {
        _profiles = current.ToDictionary(x => x.Type, x => x.DeepClone());
        foreach (var option in CommandCatalog.AllOptions)
        {
            if (!_profiles.ContainsKey(option.Type))
                _profiles[option.Type] = CommandDefaultsFactory.Create(option.Type);
        }

        Title = "Command Defaults";
        Width = 1080;
        Height = 700;
        MinWidth = 900;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("BgBrush");
        Foreground = Brush("TextBrush");

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(3, 0, 3, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = "Command Defaults",
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush")
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Each command gets its own defaults. New commands start with these values.",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = Brush("MutedTextBrush")
        });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var card = new Border
        {
            Background = Brush("PanelBrush"),
            BorderBrush = Brush("BorderBrushDark"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10)
        };
        Grid.SetRow(card, 1);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(225) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var categoryScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _categoryPanel
        };
        Grid.SetColumn(categoryScroll, 0);
        content.Children.Add(categoryScroll);
        content.Children.Add(Divider(1));

        var commandArea = new Grid { Margin = new Thickness(12, 0, 12, 0) };
        commandArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        commandArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var commandLabel = new TextBlock
        {
            Text = "Commands",
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("MutedTextBrush"),
            Margin = new Thickness(2, 4, 0, 10)
        };
        commandArea.Children.Add(commandLabel);
        var commandScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _commandPanel
        };
        Grid.SetRow(commandScroll, 1);
        commandArea.Children.Add(commandScroll);
        Grid.SetColumn(commandArea, 2);
        content.Children.Add(commandArea);
        content.Children.Add(Divider(3));

        var propertyArea = new Grid { Margin = new Thickness(14, 0, 2, 0) };
        propertyArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        propertyArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _commandTitle.FontSize = 18;
        _commandTitle.FontWeight = FontWeights.SemiBold;
        _commandTitle.Foreground = Brush("TextBrush");
        _commandTitle.Margin = new Thickness(2, 3, 0, 12);
        propertyArea.Children.Add(_commandTitle);
        var propertiesScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _propertiesPanel
        };
        Grid.SetRow(propertiesScroll, 1);
        propertyArea.Children.Add(propertiesScroll);
        Grid.SetColumn(propertyArea, 4);
        content.Children.Add(propertyArea);

        card.Child = content;
        root.Children.Add(card);

        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var resetAll = new Button
        {
            Content = "Reset All Defaults",
            MinWidth = 132,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        resetAll.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Reset every command to the built-in defaults?", "Command Defaults",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            _profiles.Clear();
            foreach (var option in CommandCatalog.AllOptions)
                _profiles[option.Type] = CommandDefaultsFactory.Create(option.Type);
            ShowCommand(_currentType);
        };
        DockPanel.SetDock(resetAll, Dock.Left);
        footer.Children.Add(resetAll);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var save = new Button
        {
            Content = "Save Defaults",
            Width = 120,
            Style = (Style)Application.Current.FindResource("AccentButtonStyle"),
            IsDefault = true
        };
        save.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        BuildCategories();
        ShowCategory(0);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        };
    }

    public IReadOnlyList<CommandDefaultProfile> Profiles =>
        CommandCatalog.AllOptions.Select(x => _profiles[x.Type].DeepClone()).ToList();

    private Border Divider(int column)
    {
        var divider = new Border
        {
            Width = 1,
            Background = Brush("BorderBrushDark"),
            Margin = new Thickness(0, 2, 0, 2)
        };
        Grid.SetColumn(divider, column);
        return divider;
    }

    private void BuildCategories()
    {
        for (var i = 0; i < CommandCatalog.Categories.Length; i++)
        {
            var index = i;
            var button = PickerButton(CommandCatalog.Categories[i].Name, false);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 8, 6);
            button.Click += (_, _) => ShowCategory(index);
            _categoryPanel.Children.Add(button);
        }
    }

    private void ShowCategory(int index)
    {
        if (index < 0 || index >= CommandCatalog.Categories.Length)
            return;
        _currentCategory = index;

        if (_activeCategoryButton is not null)
        {
            _activeCategoryButton.Background = Brush("Panel2Brush");
            _activeCategoryButton.BorderBrush = Brush("BorderBrushDark");
        }
        _activeCategoryButton = _categoryPanel.Children[index] as Button;
        if (_activeCategoryButton is not null)
        {
            _activeCategoryButton.Background = Brush("Panel3Brush");
            _activeCategoryButton.BorderBrush = Brush("AccentBrush");
        }

        _commandPanel.Children.Clear();
        var category = CommandCatalog.Categories[index];
        foreach (var option in category.Commands)
        {
            var type = option.Type;
            var button = PickerButton(option.Label, true);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 0, 6);
            button.Tag = type;
            button.Click += (_, _) => ShowCommand(type);
            _commandPanel.Children.Add(button);
        }

        ShowCommand(category.Commands[0].Type);
    }

    private void ShowCommand(CommandType type)
    {
        _currentType = type;
        _commandTitle.Text = CommandCatalog.QuickLabel(type);

        if (_activeCommandButton is not null)
        {
            _activeCommandButton.Background = Brush("InputBrush");
            _activeCommandButton.BorderBrush = Brush("BorderBrushDark");
        }
        _activeCommandButton = _commandPanel.Children.OfType<Button>().FirstOrDefault(x => x.Tag is CommandType t && t == type);
        if (_activeCommandButton is not null)
        {
            _activeCommandButton.Background = Brush("ItemSelectedBrush");
            _activeCommandButton.BorderBrush = Brush("AccentBrush");
        }

        var p = _profiles[type];
        _propertiesPanel.Children.Clear();

        var reset = new Button
        {
            Content = "Reset This Command",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 14)
        };
        reset.Click += (_, _) =>
        {
            _profiles[type] = CommandDefaultsFactory.Create(type);
            ShowCommand(type);
        };
        _propertiesPanel.Children.Add(reset);

        switch (type)
        {
            case CommandType.MoveMouse:
                AddLocation(p);
                AddMouseMovement(p);
                break;

            case CommandType.Click:
            case CommandType.RightClick:
                AddLocation(p);
                AddMouseMovement(p);
                break;

            case CommandType.DoubleClick:
                AddLocation(p);
                AddMouseMovement(p);
                AddInt("Delay between clicks (ms)", p.ClickDelayMs, 20, 1000, v => p.ClickDelayMs = v);
                break;

            case CommandType.Scroll:
                AddLocation(p);
                AddInt("Wheel amount", p.ScrollAmount, -12000, 12000, v => p.ScrollAmount = v);
                AddMouseMovement(p);
                break;

            case CommandType.DragMouse:
                AddLocation(p);
                AddEndLocation(p);
                AddMouseMovement(p);
                AddInt("Drag duration (ms)", p.DragDurationMs, 1, 60000, v => p.DragDurationMs = v);
                break;

            case CommandType.PressKey:
                AddText("Key or combo", p.Key, v => p.Key = v);
                break;

            case CommandType.KeyDown:
            case CommandType.KeyUp:
                AddText("Key", p.Key, v => p.Key = v);
                break;

            case CommandType.TypeText:
                AddText("Text", p.Text, v => p.Text = v, true);
                break;

            case CommandType.HoldKey:
                AddText("Key", p.Key, v => p.Key = v);
                AddInt("Hold for (ms)", p.HoldMs, 1, 86_400_000, v => p.HoldMs = v);
                break;

            case CommandType.RepeatKey:
                AddText("Key or combo", p.Key, v => p.Key = v);
                AddInt("Repeat count", p.RepeatCount, 1, 1_000_000, v => p.RepeatCount = v);
                AddInt("Delay between presses (ms)", p.WaitMs, 0, 86_400_000, v => p.WaitMs = v);
                break;

            case CommandType.WaitUntilKeyPressed:
                AddText("Key", p.Key, v => p.Key = v);
                AddPolling(p, true);
                break;

            case CommandType.Wait:
                AddInt("Milliseconds", p.WaitMs, 0, 86_400_000, v => p.WaitMs = v);
                break;

            case CommandType.RandomWait:
                AddInt("Minimum ms", p.MinWaitMs, 0, 86_400_000, v => p.MinWaitMs = v);
                AddInt("Maximum ms", p.MaxWaitMs, 0, 86_400_000, v => p.MaxWaitMs = v);
                break;

            case CommandType.RecordedActions:
                AddText("Stop recording hotkey", p.RecordingStopHotkey, v => p.RecordingStopHotkey = v);
                var recordMovement = new CheckBox
                {
                    Content = "Record mouse movement",
                    IsChecked = p.RecordMouseMovement,
                    Margin = new Thickness(0, 3, 0, 10)
                };
                recordMovement.Checked += (_, _) => { p.RecordMouseMovement = true; ShowCommand(type); };
                recordMovement.Unchecked += (_, _) => { p.RecordMouseMovement = false; ShowCommand(type); };
                _propertiesPanel.Children.Add(recordMovement);
                if (p.RecordMouseMovement)
                    AddInt("Mouse sample interval (ms)", p.RecordMouseSampleMs, 15, 500, v => p.RecordMouseSampleMs = v);
                break;

            case CommandType.IfColor:
                AddColorDefaults(p, false);
                break;

            case CommandType.WaitUntilColor:
                AddColorDefaults(p, true);
                AddPolling(p, true);
                break;

            case CommandType.LoopWhileColor:
            case CommandType.LoopUntilColor:
                AddColorDefaults(p, true);
                AddInt("Idle poll ms (empty loop)", p.PollMs, 10, 5000, v => p.PollMs = v);
                break;

            case CommandType.ClickColor:
                AddText("Target color", p.ColorHex, v => p.ColorHex = NormalizeColor(v));
                AddInt("Color tolerance (0-255)", p.ColorTolerance, 0, 255, v => p.ColorTolerance = v);
                AddSearchAreaDefaults(p);
                AddMouseMovement(p);
                break;

            case CommandType.IfImage:
                AddImageDefaults(p);
                break;

            case CommandType.WaitUntilImage:
            case CommandType.WaitUntilImageGone:
                AddImageDefaults(p);
                AddPolling(p, true);
                break;

            case CommandType.ClickImage:
            case CommandType.DoubleClickImage:
            case CommandType.MoveToImage:
                AddImageDefaults(p);
                AddInt("Offset X", p.ImageOffsetX, -10000, 10000, v => p.ImageOffsetX = v);
                AddInt("Offset Y", p.ImageOffsetY, -10000, 10000, v => p.ImageOffsetY = v);
                AddMouseMovement(p);
                if (type == CommandType.DoubleClickImage)
                    AddInt("Delay between clicks (ms)", p.ClickDelayMs, 20, 1000, v => p.ClickDelayMs = v);
                break;

            case CommandType.LoopUntilImage:
                AddImageDefaults(p);
                AddInt("Idle poll ms (empty loop)", p.PollMs, 10, 5000, v => p.PollMs = v);
                break;

            case CommandType.FocusWindow:
                AddText("Window title contains", p.WindowTitle, v => p.WindowTitle = v);
                break;

            case CommandType.WaitForWindow:
            case CommandType.WaitForWindowGone:
                AddText("Window title contains", p.WindowTitle, v => p.WindowTitle = v);
                AddPolling(p, true);
                break;

            case CommandType.RunProgram:
                AddText("Program, file, folder, or URL", p.ProgramPath, v => p.ProgramPath = v);
                break;

            case CommandType.LoopTimes:
                AddInt("Repeat count", p.RepeatCount, 0, 1_000_000, v => p.RepeatCount = v);
                break;

            default:
                AddNoDefaults(type);
                break;
        }
    }

    private void AddNoDefaults(CommandType type)
    {
        var message = type == CommandType.RunSequence
            ? "Run Sequence chooses a sequence from the current project, so it does not use a global default target."
            : "This command has no configurable defaults.";
        _propertiesPanel.Children.Add(new Border
        {
            Background = Brush("Panel2Brush"),
            BorderBrush = Brush("BorderBrushDark"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12),
            Child = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("MutedTextBrush")
            }
        });
    }

    private void AddColorDefaults(CommandDefaultProfile p, bool includePollingLabel)
    {
        AddLabel("Comparison");
        var combo = new ComboBox
        {
            ItemsSource = Enum.GetValues<CompareMode>(),
            SelectedItem = p.CompareMode,
            Margin = new Thickness(0, 0, 0, 10)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is CompareMode mode)
                p.CompareMode = mode;
        };
        _propertiesPanel.Children.Add(combo);
        AddText("Target color", p.ColorHex, v => p.ColorHex = NormalizeColor(v));
        AddLocation(p);
        AddInt("Color tolerance (0-255)", p.ColorTolerance, 0, 255, v => p.ColorTolerance = v);
    }

    private void AddImageDefaults(CommandDefaultProfile p)
    {
        AddInt("Image tolerance (0-255)", p.ImageTolerance, 0, 255, v => p.ImageTolerance = v);
        AddSearchAreaDefaults(p);
    }

    private void AddSearchAreaDefaults(CommandDefaultProfile p)
    {
        AddLabel("Default search area (0 width/height = full screen)");
        AddFourInts(
            ("X", p.SearchX, -100000, 100000, v => p.SearchX = v),
            ("Y", p.SearchY, -100000, 100000, v => p.SearchY = v),
            ("W", p.SearchWidth, 0, 100000, v => p.SearchWidth = v),
            ("H", p.SearchHeight, 0, 100000, v => p.SearchHeight = v));
    }

    private void AddPolling(CommandDefaultProfile p, bool timeout)
    {
        AddInt("Poll every (ms)", p.PollMs, 10, 5000, v => p.PollMs = v);
        if (timeout)
            AddInt("Timeout (ms, 0 = forever)", p.TimeoutMs, 0, 86_400_000, v => p.TimeoutMs = v);
    }

    private void AddLocation(CommandDefaultProfile p)
    {
        AddLabel("Location");
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var xl = new TextBlock { Text = "X", Foreground = Brush("MutedTextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        var xb = new TextBox { Text = p.X.ToString(), Margin = new Thickness(0, 0, 10, 0) };
        var yl = new TextBlock { Text = "Y", Foreground = Brush("MutedTextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        var yb = new TextBox { Text = p.Y.ToString() };
        xb.LostFocus += (_, _) => ParseIntBox(xb, -100000, 100000, p.X, v => p.X = v);
        yb.LostFocus += (_, _) => ParseIntBox(yb, -100000, 100000, p.Y, v => p.Y = v);
        Grid.SetColumn(xl, 0); Grid.SetColumn(xb, 1); Grid.SetColumn(yl, 2); Grid.SetColumn(yb, 3);
        grid.Children.Add(xl); grid.Children.Add(xb); grid.Children.Add(yl); grid.Children.Add(yb);
        _propertiesPanel.Children.Add(grid);
    }

    private void AddEndLocation(CommandDefaultProfile p)
    {
        AddLabel("End location");
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var xl = new TextBlock { Text = "X", Foreground = Brush("MutedTextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        var xb = new TextBox { Text = p.EndX.ToString(), Margin = new Thickness(0, 0, 10, 0) };
        var yl = new TextBlock { Text = "Y", Foreground = Brush("MutedTextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        var yb = new TextBox { Text = p.EndY.ToString() };
        xb.LostFocus += (_, _) => ParseIntBox(xb, -100000, 100000, p.EndX, v => p.EndX = v);
        yb.LostFocus += (_, _) => ParseIntBox(yb, -100000, 100000, p.EndY, v => p.EndY = v);
        Grid.SetColumn(xl, 0); Grid.SetColumn(xb, 1); Grid.SetColumn(yl, 2); Grid.SetColumn(yb, 3);
        grid.Children.Add(xl); grid.Children.Add(xb); grid.Children.Add(yl); grid.Children.Add(yb);
        _propertiesPanel.Children.Add(grid);
    }

    private void AddMouseMovement(CommandDefaultProfile p)
    {
        AddLabel("Mouse movement");
        var combo = new ComboBox
        {
            ItemsSource = new[] { MouseMoveMode.Teleport, MouseMoveMode.Smooth },
            SelectedItem = p.MouseMoveMode == MouseMoveMode.Legacy ? MouseMoveMode.Smooth : p.MouseMoveMode,
            Margin = new Thickness(0, 0, 0, 10)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is MouseMoveMode mode && mode != p.MouseMoveMode)
            {
                p.MouseMoveMode = mode;
                ShowCommand(_currentType);
            }
        };
        _propertiesPanel.Children.Add(combo);
        if (p.MouseMoveMode != MouseMoveMode.Teleport)
            AddInt("Smooth move duration (ms)", p.MoveDurationMs, 1, 60000, v => p.MoveDurationMs = v);
    }

    private void AddText(string label, string current, Action<string> setter, bool multiline = false)
    {
        AddLabel(label);
        var box = new TextBox
        {
            Text = current,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 72 : 0,
            Margin = new Thickness(0, 0, 0, 10)
        };
        box.LostFocus += (_, _) => setter(box.Text.Trim());
        _propertiesPanel.Children.Add(box);
    }

    private void AddInt(string label, int current, int min, int max, Action<int> setter)
    {
        AddLabel(label);
        var box = new TextBox { Text = current.ToString(), Margin = new Thickness(0, 0, 0, 10) };
        box.LostFocus += (_, _) => ParseIntBox(box, min, max, current, setter);
        _propertiesPanel.Children.Add(box);
    }

    private void AddFourInts(params (string Label, int Current, int Min, int Max, Action<int> Setter)[] fields)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        for (var i = 0; i < fields.Length; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            var stack = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 4, 0, i == fields.Length - 1 ? 0 : 4, 0) };
            stack.Children.Add(new TextBlock { Text = field.Label, Foreground = Brush("MutedTextBrush"), Margin = new Thickness(0, 0, 0, 4) });
            var box = new TextBox { Text = field.Current.ToString() };
            var copy = field;
            box.LostFocus += (_, _) => ParseIntBox(box, copy.Min, copy.Max, copy.Current, copy.Setter);
            stack.Children.Add(box);
            Grid.SetColumn(stack, i);
            grid.Children.Add(stack);
        }
        _propertiesPanel.Children.Add(grid);
    }

    private static void ParseIntBox(TextBox box, int min, int max, int fallback, Action<int> setter)
    {
        if (!int.TryParse(box.Text, out var value))
            value = fallback;
        value = Math.Clamp(value, min, max);
        box.Text = value.ToString();
        setter(value);
    }

    private void AddLabel(string text)
    {
        _propertiesPanel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = Brush("MutedTextBrush"),
            Margin = new Thickness(0, 0, 0, 5)
        });
    }

    private static string NormalizeColor(string raw)
    {
        var cleaned = raw.Trim().Replace("#", "").Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (cleaned.Length == 6 && int.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            return "0x" + cleaned.ToUpperInvariant();
        return "0xFFFFFF";
    }

    private static Button PickerButton(string text, bool command)
    {
        return new Button
        {
            Content = text,
            Foreground = Brush("TextBrush"),
            Background = command ? Brush("InputBrush") : Brush("Panel2Brush"),
            BorderBrush = Brush("BorderBrushDark"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            MinHeight = 38,
            Cursor = Cursors.Hand
        };
    }

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
