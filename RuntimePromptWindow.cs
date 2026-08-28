using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MacroMaker;

internal readonly record struct PromptTextResult(bool Accepted, string Value);

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
            var no = new Button { Content = "No", Width = 92, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            no.Click += (_, _) => { Tag = false; DialogResult = false; };
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

    public static async Task<PromptTextResult> AskTextAsync(string prompt, string initialValue)
    {
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var window = new RuntimePromptWindow(prompt, initialValue, false);
            var accepted = window.ShowDialog() == true;
            return new PromptTextResult(accepted, window._textBox?.Text ?? string.Empty);
        });
    }

    public static async Task<bool> AskYesNoAsync(string prompt)
    {
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var window = new RuntimePromptWindow(prompt, string.Empty, true);
            return window.ShowDialog() == true && window.Tag is true;
        });
    }
}
