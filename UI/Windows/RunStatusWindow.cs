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

    private readonly TextBlock _statusText;
    private readonly TextBlock _detailText;
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
        panel.Children.Add(new TextBlock
        {
            Text = "CURRENT STEP",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(126, 166, 255))
        });

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
        _detailText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(205, 215, 230)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 6),
            Visibility = Visibility.Collapsed
        };
        panel.Children.Add(_detailText);

        var controls = $"{stopHotkey} Stop";
        if (!string.IsNullOrWhiteSpace(pauseHotkey))
            controls += $"   •   {pauseHotkey} Pause / Resume";

        panel.Children.Add(new TextBlock
        {
            Text = controls,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(160, 169, 186))
        });

        card.Child = panel;
        Content = card;
        Loaded += (_, _) => PositionWindow();
        SizeChanged += (_, _) => PositionWindow();
        SourceInitialized += (_, _) => MakeClickThrough();
    }

    public void UpdateStatus(string text, string detail = "")
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => UpdateStatus(text, detail));
            return;
        }
        _statusText.Text = string.IsNullOrWhiteSpace(text) ? "Running…" : text;
        _detailText.Text = detail;
        _detailText.Visibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible;
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
