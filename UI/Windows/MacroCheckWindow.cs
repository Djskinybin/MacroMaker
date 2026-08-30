using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MacroMaker;

public sealed class MacroCheckWindow : Window
{
    public MacroCheckWindow(IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        WindowTheme.Attach(this);
        Title = "Check Macro";
        Width = 620;
        Height = 520;
        MinWidth = 460;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("BgBrush");
        Foreground = Brush("TextBrush");

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var ok = errors.Count == 0 && warnings.Count == 0;
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = ok ? "Macro looks ready" : errors.Count > 0 ? "Macro needs attention" : "Macro looks usable",
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Foreground = ok ? Brush("SuccessBrush") : errors.Count > 0 ? Brush("DangerBrush") : Brush("AccentBrush")
        });
        heading.Children.Add(new TextBlock
        {
            Text = ok ? "No problems were found." : $"{errors.Count} problem(s) and {warnings.Count} warning(s) found.",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = Brush("MutedTextBrush")
        });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var body = new StackPanel();
        if (ok)
        {
            body.Children.Add(Card("Ready to run", "Images, sequence links, colors, blocks, and global hotkeys passed the basic project check.", Brush("SuccessBrush")));
        }
        else
        {
            if (errors.Count > 0)
                body.Children.Add(IssueCard("Fix these", errors, Brush("DangerBrush")));
            if (warnings.Count > 0)
                body.Children.Add(IssueCard("Warnings", warnings, Brush("AccentBrush")));
        }

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = body
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var close = new Button
        {
            Content = "Close",
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
            IsDefault = true
        };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 2);
        root.Children.Add(close);

        Content = root;
    }

    private static Border IssueCard(string title, IReadOnlyList<string> items, Brush accent)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (var item in items.Take(30))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "• " + item,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("TextBrush"),
                Margin = new Thickness(0, 0, 0, 7)
            });
        }
        if (items.Count > 30)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"…and {items.Count - 30} more",
                Foreground = Brush("MutedTextBrush")
            });
        }

        return Wrap(panel);
    }

    private static Border Card(string title, string text, Brush accent)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = accent });
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brush("MutedTextBrush"), Margin = new Thickness(0, 6, 0, 0) });
        return Wrap(panel);
    }

    private static Border Wrap(UIElement child) => new()
    {
        Background = Brush("PanelBrush"),
        BorderBrush = Brush("BorderBrushDark"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14),
        Margin = new Thickness(0, 0, 0, 10),
        Child = child
    };

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
