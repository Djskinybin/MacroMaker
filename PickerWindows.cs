using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MacroMaker;


internal static class PickerBrushes
{
    public static Brush Get(string key) => (Brush)Application.Current.FindResource(key);
}

public sealed class PointPickerWindow : Window
{
    private readonly bool _sampleColor;
    private readonly TextBlock _positionText;
    private readonly TextBlock _colorText;
    private readonly Border _colorPreview;
    private readonly DispatcherTimer _updateTimer;

    public PointPickerWindow(bool sampleColor)
    {
        _sampleColor = sampleColor;
        Title = sampleColor ? "Pick Color + Location" : "Pick Location";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Cursor = Cursors.Cross;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        var root = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))
        };

        var overlay = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12)
        };

        var instruction = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 19, 24, 32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(90, 120, 190)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = sampleColor
                    ? "Click anywhere to capture its screen color + location   •   Esc cancels"
                    : "Click anywhere to capture the screen location   •   Esc cancels",
                Foreground = PickerBrushes.Get("TextBrush"),
                FontSize = 14
            }
        };

        _positionText = new TextBlock
        {
            Text = "X: 0   Y: 0",
            Foreground = PickerBrushes.Get("TextBrush"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };

        _colorText = new TextBlock
        {
            Text = "Color: 0x000000",
            Foreground = PickerBrushes.Get("TextBrush"),
            FontSize = 14,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        _colorPreview = new Border
        {
            Width = 26,
            Height = 26,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 88, 104)),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Colors.Black),
            VerticalAlignment = VerticalAlignment.Center
        };

        var colorRow = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        colorRow.Children.Add(_colorText);
        colorRow.Children.Add(_colorPreview);

        var info = new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(235, 19, 24, 32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(90, 120, 190)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            IsHitTestVisible = false,
            Child = new StackPanel
            {
                Children =
                {
                    _positionText,
                    colorRow
                }
            }
        };

        overlay.Children.Add(instruction);
        overlay.Children.Add(info);
        root.Children.Add(overlay);
        Content = root;

        PreviewMouseLeftButtonDown += PickPoint;
        PreviewMouseMove += (_, _) => UpdateMouseInfo();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        };
        Loaded += (_, _) =>
        {
            Focus();
            Activate();
            UpdateMouseInfo();
        };

        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        _updateTimer.Tick += (_, _) => UpdateMouseInfo();
        _updateTimer.Start();
        Closed += (_, _) => _updateTimer.Stop();
    }

    public int PickedX { get; private set; }
    public int PickedY { get; private set; }
    public string PickedColor { get; private set; } = "0xFFFFFF";

    private void UpdateMouseInfo()
    {
        if (!NativeMethods.GetCursorPos(out var point))
            return;

        var hex = ScreenTools.GetPixelHex(point.X, point.Y);
        _positionText.Text = $"X: {point.X}   Y: {point.Y}";
        _colorText.Text = $"Color: {hex}";
        _colorPreview.Background = new SolidColorBrush(ParseHexColor(hex));
    }

    private static Color ParseHexColor(string hex)
    {
        var cleaned = hex.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase).Replace("#", "");
        if (cleaned.Length != 6)
            return Colors.Black;

        return byte.TryParse(cleaned.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
            && byte.TryParse(cleaned.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
            && byte.TryParse(cleaned.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b)
            ? Color.FromRgb(r, g, b)
            : Colors.Black;
    }

    private void PickPoint(object sender, MouseButtonEventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            DialogResult = false;
            Close();
            return;
        }

        PickedX = point.X;
        PickedY = point.Y;
        PickedColor = ScreenTools.GetPixelHex(PickedX, PickedY);

        DialogResult = true;
        Close();
    }
}

public sealed class RegionPickerWindow : Window
{
    private readonly Canvas _canvas;
    private readonly Rectangle _selection;
    private readonly TextBlock _selectionInfo;
    private readonly DispatcherTimer _escapeTimer;
    private Point? _startLocal;
    private NativeMethods.POINT? _startScreen;
    private bool _escapeWasDown;

    public RegionPickerWindow()
    {
        Title = "Pick Search Area";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Cursor = Cursors.Cross;
        Focusable = true;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _canvas = new Canvas
        {
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Focusable = true
        };

        _selection = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(108, 140, 255)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(42, 108, 140, 255)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(_selection);

        var hud = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(242, 14, 20, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(52, 72, 101)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(13, 10, 13, 10),
            IsHitTestVisible = false
        };

        var hudStack = new StackPanel();
        hudStack.Children.Add(new TextBlock
        {
            Text = "Drag anywhere to select the search area",
            Foreground = PickerBrushes.Get("TextBrush"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        });
        hudStack.Children.Add(new TextBlock
        {
            Text = "Esc cancels",
            Foreground = new SolidColorBrush(Color.FromRgb(151, 164, 184)),
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0)
        });
        _selectionInfo = new TextBlock
        {
            Text = "",
            Foreground = new SolidColorBrush(Color.FromRgb(108, 140, 255)),
            FontSize = 12,
            Margin = new Thickness(0, 5, 0, 0)
        };
        hudStack.Children.Add(_selectionInfo);
        hud.Child = hudStack;
        Canvas.SetLeft(hud, 12);
        Canvas.SetTop(hud, 12);
        _canvas.Children.Add(hud);

        Content = _canvas;

        PreviewMouseLeftButtonDown += StartDrag;
        PreviewMouseMove += UpdateDrag;
        PreviewMouseLeftButtonUp += EndDrag;
        PreviewKeyDown += HandleKeyDown;
        KeyDown += HandleKeyDown;

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            Keyboard.Focus(_canvas);
            _escapeWasDown = IsEscapeDown();
        };

        _escapeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        _escapeTimer.Tick += (_, _) =>
        {
            var down = IsEscapeDown();
            if (down && !_escapeWasDown)
                CancelPicker();
            _escapeWasDown = down;
        };
        _escapeTimer.Start();
        Closed += (_, _) => _escapeTimer.Stop();
    }

    public ScreenRegion Region { get; private set; }

    private static bool IsEscapeDown() => (NativeMethods.GetAsyncKeyState(0x1B) & 0x8000) != 0;

    private void HandleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        CancelPicker();
    }

    private void CancelPicker()
    {
        if (!IsVisible)
            return;

        try { _canvas.ReleaseMouseCapture(); } catch { }
        DialogResult = false;
        Close();
    }

    private void StartDrag(object sender, MouseButtonEventArgs e)
    {
        _startLocal = e.GetPosition(_canvas);
        if (!NativeMethods.GetCursorPos(out var screenPoint))
            return;

        _startScreen = screenPoint;
        _selection.Visibility = Visibility.Visible;
        Canvas.SetLeft(_selection, _startLocal.Value.X);
        Canvas.SetTop(_selection, _startLocal.Value.Y);
        _selection.Width = 1;
        _selection.Height = 1;
        _selectionInfo.Text = $"Start: {screenPoint.X}, {screenPoint.Y}";
        Mouse.Capture(_canvas, CaptureMode.Element);
        e.Handled = true;
    }

    private void UpdateDrag(object sender, MouseEventArgs e)
    {
        if (_startLocal is null || _startScreen is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(_canvas);
        var left = Math.Min(_startLocal.Value.X, current.X);
        var top = Math.Min(_startLocal.Value.Y, current.Y);
        var width = Math.Abs(current.X - _startLocal.Value.X);
        var height = Math.Abs(current.Y - _startLocal.Value.Y);
        Canvas.SetLeft(_selection, left);
        Canvas.SetTop(_selection, top);
        _selection.Width = width;
        _selection.Height = height;

        if (NativeMethods.GetCursorPos(out var screenPoint))
        {
            var pxWidth = Math.Abs(screenPoint.X - _startScreen.Value.X);
            var pxHeight = Math.Abs(screenPoint.Y - _startScreen.Value.Y);
            _selectionInfo.Text = $"{pxWidth} × {pxHeight} px";
        }
    }

    private void EndDrag(object sender, MouseButtonEventArgs e)
    {
        if (_startScreen is null)
            return;

        try { _canvas.ReleaseMouseCapture(); } catch { }

        if (!NativeMethods.GetCursorPos(out var end))
            return;

        var left = Math.Min(_startScreen.Value.X, end.X);
        var top = Math.Min(_startScreen.Value.Y, end.Y);
        var right = Math.Max(_startScreen.Value.X, end.X);
        var bottom = Math.Max(_startScreen.Value.Y, end.Y);

        if (right - left < 3 || bottom - top < 3)
        {
            _startLocal = null;
            _startScreen = null;
            _selection.Visibility = Visibility.Collapsed;
            _selectionInfo.Text = "Drag a larger area";
            return;
        }

        Region = new ScreenRegion(left, top, right - left, bottom - top);
        DialogResult = true;
        Close();
    }
}

public sealed class TextPromptWindow : Window
{
    private readonly TextBox _textBox;

    public TextPromptWindow(string title, string prompt, string startingValue)
    {
        Title = title;
        Width = 430;
        Height = 190;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = PickerBrushes.Get("PanelBrush");
        Foreground = PickerBrushes.Get("TextBrush");

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) };
        _textBox = new TextBox { Text = startingValue, Padding = new Thickness(8, 6, 8, 6) };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var ok = new Button { Content = "OK", Width = 90, IsDefault = true };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        Grid.SetRow(label, 0);
        Grid.SetRow(_textBox, 1);
        Grid.SetRow(buttons, 2);
        root.Children.Add(label);
        root.Children.Add(_textBox);
        root.Children.Add(buttons);
        Content = root;

        Loaded += (_, _) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
        };
    }

    public string Value => _textBox.Text;
}

public sealed class CommandPickerWindow : Window
{
    private readonly StackPanel _categoryPanel;
    private readonly StackPanel _commandPanel;
    private readonly TextBlock _categoryTitle;
    private Button? _activeCategoryButton;

    private readonly (string Name, (string Label, CommandType Type)[] Commands)[] _categories =
        CommandCatalog.Categories
            .Select(category => (
                category.Name,
                category.Commands.Select(command => (command.Label, command.Type)).ToArray()))
            .ToArray();

    public CommandPickerWindow()
    {
        Title = "Add Command";
        Width = 650;
        Height = 500;
        MinWidth = 590;
        MinHeight = 430;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = PickerBrushes.Get("BgBrush");
        Foreground = PickerBrushes.Get("TextBrush");

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel { Margin = new Thickness(3, 2, 3, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = "Add Command",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = PickerBrushes.Get("TextBrush")
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Choose a category, then choose the action to add.",
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 13,
            Foreground = PickerBrushes.Get("MutedTextBrush")
        });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var contentBorder = new Border
        {
            Background = PickerBrushes.Get("PanelBrush"),
            BorderBrush = PickerBrushes.Get("BorderBrushDark"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10)
        };
        Grid.SetRow(contentBorder, 1);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _categoryPanel = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        var categoryScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _categoryPanel
        };
        Grid.SetColumn(categoryScroll, 0);
        content.Children.Add(categoryScroll);

        var divider = new Border
        {
            Width = 1,
            Background = PickerBrushes.Get("BorderBrushDark"),
            Margin = new Thickness(0, 2, 0, 2)
        };
        Grid.SetColumn(divider, 1);
        content.Children.Add(divider);

        var right = new Grid { Margin = new Thickness(14, 0, 0, 0) };
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _categoryTitle = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = PickerBrushes.Get("TextBrush"),
            Margin = new Thickness(2, 4, 0, 10)
        };
        Grid.SetRow(_categoryTitle, 0);
        right.Children.Add(_categoryTitle);

        _commandPanel = new StackPanel();
        var commandScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _commandPanel
        };
        Grid.SetRow(commandScroll, 1);
        right.Children.Add(commandScroll);
        Grid.SetColumn(right, 2);
        content.Children.Add(right);

        contentBorder.Child = content;
        root.Children.Add(contentBorder);
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

    public CommandType? SelectedType { get; private set; }

    private void BuildCategories()
    {
        for (var i = 0; i < _categories.Length; i++)
        {
            var index = i;
            var button = CreatePickerButton(_categories[i].Name, false);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 0, 6);
            button.Click += (_, _) => ShowCategory(index);
            _categoryPanel.Children.Add(button);
        }
    }

    private void ShowCategory(int index)
    {
        if (index < 0 || index >= _categories.Length)
            return;

        if (_activeCategoryButton is not null)
        {
            _activeCategoryButton.Background = PickerBrushes.Get("Panel2Brush");
            _activeCategoryButton.BorderBrush = PickerBrushes.Get("BorderBrushDark");
        }

        _activeCategoryButton = _categoryPanel.Children[index] as Button;
        if (_activeCategoryButton is not null)
        {
            _activeCategoryButton.Background = PickerBrushes.Get("Panel3Brush");
            _activeCategoryButton.BorderBrush = PickerBrushes.Get("AccentBrush");
        }

        var category = _categories[index];
        _categoryTitle.Text = category.Name;
        _commandPanel.Children.Clear();

        foreach (var (label, type) in category.Commands)
        {
            var button = CreatePickerButton(label, true);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 0, 7);
            button.Click += (_, _) =>
            {
                SelectedType = type;
                DialogResult = true;
                Close();
            };
            _commandPanel.Children.Add(button);
        }
    }

    private static Button CreatePickerButton(string text, bool command)
    {
        var button = new Button
        {
            Content = text,
            Foreground = PickerBrushes.Get("TextBrush"),
            Background = command ? PickerBrushes.Get("InputBrush") : PickerBrushes.Get("Panel2Brush"),
            BorderBrush = PickerBrushes.Get("BorderBrushDark"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 9, 12, 9),
            MinHeight = 38,
            Cursor = Cursors.Hand
        };

        return button;
    }

}
