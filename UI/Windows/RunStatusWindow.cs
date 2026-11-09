using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace MacroMaker;

public sealed class RunStatusWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private const int VisibleActivityLimit = 5;

    private readonly TextBlock _heading;
    private readonly TextBlock _statusText;
    private readonly TextBlock _overflowText;
    private readonly (StackPanel Panel, TextBlock Title, TextBlock Detail)[] _rows =
        new (StackPanel, TextBlock, TextBlock)[VisibleActivityLimit];
    private readonly HudCorner _corner;

    public RunStatusWindow(string stopHotkey, string pauseHotkey, HudCorner corner = HudCorner.TopLeft, int opacityPercent = 92)
    {
        _corner = corner;
        Title = "MacroMaker Running";
        Width = 420;
        MinHeight = 106;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        Background = Brushes.Transparent;
        AllowsTransparency = true;
        Opacity = Math.Clamp(opacityPercent, 35, 100) / 100.0;

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(242, 18, 22, 31)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(67, 80, 105)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 11, 14, 12)
        };

        var panel = new StackPanel();
        _heading = new TextBlock
        {
            Text = "ACTIVE STEPS",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(126, 166, 255))
        };
        panel.Children.Add(_heading);

        _statusText = new TextBlock
        {
            Text = "Starting…",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 6, 0, 6)
        };
        panel.Children.Add(_statusText);
        var detailBrush = new SolidColorBrush(Color.FromRgb(205, 215, 230));
        for (var i = 0; i < _rows.Length; i++)
        {
            var row = new StackPanel { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
            var title = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var detail = new TextBlock
            {
                FontSize = 12,
                Foreground = detailBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            row.Children.Add(title);
            row.Children.Add(detail);
            panel.Children.Add(row);
            _rows[i] = (row, title, detail);
        }

        _overflowText = new TextBlock
        {
            FontSize = 12,
            Foreground = detailBrush,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed
        };
        panel.Children.Add(_overflowText);

        var controls = $"{stopHotkey} Stop";
        if (!string.IsNullOrWhiteSpace(pauseHotkey))
            controls += $"   •   {pauseHotkey} Pause / Resume";

        panel.Children.Add(new TextBlock
        {
            Text = controls,
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(160, 169, 186))
        });

        card.Child = panel;
        Content = card;
        Loaded += (_, _) => PositionWindow();
        SizeChanged += (_, _) => PositionWindow();
        SourceInitialized += (_, _) => MakeClickThrough();
    }

    internal void UpdateStatus(string status, IReadOnlyList<RunActivity>? activities = null, bool paused = false)
    {
        var count = activities?.Count ?? 0;
        _heading.Text = paused ? "PAUSED" : "ACTIVE STEPS";
        _statusText.Text = string.IsNullOrWhiteSpace(status) ? "Running…" : status;
        _statusText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;

        for (var i = 0; i < _rows.Length; i++)
        {
            var (panel, title, detail) = _rows[i];
            if (i >= count)
            {
                panel.Visibility = Visibility.Collapsed;
                continue;
            }

            var activity = activities![i];
            title.Text = activity.Heading;
            detail.Text = activity.Detail;
            detail.Visibility = string.IsNullOrWhiteSpace(activity.Detail) ? Visibility.Collapsed : Visibility.Visible;
            panel.Visibility = Visibility.Visible;
        }

        var remaining = count - _rows.Length;
        _overflowText.Text = remaining > 0 ? $"+{remaining} more active" : string.Empty;
        _overflowText.Visibility = remaining > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PositionWindow()
    {
        const double margin = 12;
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var width = SystemParameters.VirtualScreenWidth;
        var height = SystemParameters.VirtualScreenHeight;
        var actualW = ActualWidth > 1 ? ActualWidth : Width;
        var actualH = ActualHeight > 1 ? ActualHeight : MinHeight;

        Left = _corner is HudCorner.TopRight or HudCorner.BottomRight
            ? left + width - actualW - margin
            : left + margin;
        Top = _corner is HudCorner.BottomLeft or HudCorner.BottomRight
            ? top + height - actualH - margin
            : top + margin;
    }

    private void MakeClickThrough()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, style | WsExTransparent | WsExNoActivate | WsExToolWindow);
        }
        catch { }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
