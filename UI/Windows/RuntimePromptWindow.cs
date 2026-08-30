using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MacroMaker;

internal readonly record struct PromptTextResult(bool Accepted, string Value);
internal readonly record struct PromptBoolResult(bool Accepted, bool Value);

internal sealed class RuntimePromptWindow : Window
{
    private readonly TextBox? _textBox;

    private RuntimePromptWindow(string prompt, string initialValue, bool yesNo)
    {
        Title = "MacroMaker";
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/MacroMaker.ico"));
        Width = 430;
        SizeToContent = SizeToContent.Height;
        MinHeight = 170;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Background = (Brush)Application.Current.FindResource("BgBrush");
        Foreground = (Brush)Application.Current.FindResource("TextBrush");
        WindowTheme.Attach(this);

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 14)
        });

        if (!yesNo)
        {
            _textBox = new TextBox
            {
                Text = initialValue,
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 14)
            };
            root.Children.Add(_textBox);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        if (yesNo)
        {
            var no = new Button { Content = "No", Width = 92, Margin = new Thickness(0, 0, 8, 0) };
            no.Click += (_, _) => { Tag = false; DialogResult = true; };
            var yes = new Button { Content = "Yes", Width = 92, IsDefault = true, Style = (Style)Application.Current.FindResource("AccentButtonStyle") };
            yes.Click += (_, _) => { Tag = true; DialogResult = true; };
            buttons.Children.Add(no);
            buttons.Children.Add(yes);
        }
        else
        {
            var cancel = new Button { Content = "Cancel", Width = 92, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            cancel.Click += (_, _) => DialogResult = false;
            var ok = new Button { Content = "OK", Width = 92, IsDefault = true, Style = (Style)Application.Current.FindResource("AccentButtonStyle") };
            ok.Click += (_, _) => DialogResult = true;
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
        }

        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) =>
        {
            _textBox?.Focus();
            _textBox?.SelectAll();
            Activate();
        };
    }

    public static async Task<PromptTextResult> AskTextAsync(string prompt, string initialValue, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            var window = new RuntimePromptWindow(prompt, initialValue, false);
            using var registration = CloseOnCancellation(window, token);
            var accepted = window.ShowDialog() == true;
            return new PromptTextResult(accepted, window._textBox?.Text ?? string.Empty);
        });
    }

    public static async Task<PromptTextResult> AskSelectAsync(string prompt, IReadOnlyList<string> options, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (options.Count == 0)
                return new PromptTextResult(false, string.Empty);

            var window = new Window
            {
                Title = "MacroMaker",
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/MacroMaker.ico")),
                Width = 430,
                SizeToContent = SizeToContent.Height,
                MinHeight = 180,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = true,
                Background = (Brush)Application.Current.FindResource("BgBrush"),
                Foreground = (Brush)Application.Current.FindResource("TextBrush")
            };
            WindowTheme.Attach(window);

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock
            {
                Text = prompt,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 16,
                Margin = new Thickness(0, 0, 0, 14)
            });

            var combo = new ComboBox
            {
                ItemsSource = options,
                SelectedIndex = 0,
                MinHeight = 34,
                Margin = new Thickness(0, 0, 0, 14),
                IsTextSearchEnabled = true,
                MaxDropDownHeight = 320
            };
            root.Children.Add(combo);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var cancel = new Button { Content = "Cancel", Width = 92, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            cancel.Click += (_, _) => window.DialogResult = false;
            var ok = new Button
            {
                Content = "Select",
                Width = 92,
                IsDefault = true,
                Style = (Style)Application.Current.FindResource("AccentButtonStyle")
            };
            ok.Click += (_, _) => window.DialogResult = combo.SelectedItem is not null;
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            root.Children.Add(buttons);
            window.Content = root;
            window.Loaded += (_, _) =>
            {
                combo.Focus();
                window.Activate();
            };

            using var registration = CloseOnCancellation(window, token);
            var accepted = window.ShowDialog() == true;
            return new PromptTextResult(accepted, accepted ? combo.SelectedItem?.ToString() ?? string.Empty : string.Empty);
        });
    }

    public static async Task<PromptBoolResult> AskYesNoAsync(string prompt, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            var window = new RuntimePromptWindow(prompt, string.Empty, true);
            using var registration = CloseOnCancellation(window, token);
            var accepted = window.ShowDialog() == true;
            return new PromptBoolResult(accepted, accepted && window.Tag is true);
        });
    }

    private static CancellationTokenRegistration CloseOnCancellation(Window window, CancellationToken token)
    {
        if (!token.CanBeCanceled)
            return default;

        return token.Register(() =>
        {
            _ = Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (window.IsVisible)
                        window.DialogResult = false;
                    else
                        window.Close();
                }
                catch
                {
                    try { window.Close(); } catch { }
                }
            });
        });
    }
}
