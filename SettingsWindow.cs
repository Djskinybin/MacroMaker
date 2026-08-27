using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MacroMaker;

public sealed class SettingsWindow : Window
{
    private readonly ComboBox _themeCombo;

    public SettingsWindow(AppSettings current)
    {
        Settings = Clone(current);

        Title = "Macro Maker Settings";
        Width = 620;
        Height = 500;
        MinWidth = 560;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("BgBrush");
        Foreground = Brush("TextBrush");

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(2, 0, 0, 16) };
        heading.Children.Add(new TextBlock
        {
            Text = "Settings",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush")
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Appearance, Quick Add, and command defaults.",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = Brush("MutedTextBrush")
        });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var content = new StackPanel();

        content.Children.Add(SectionCard("Appearance", "Choose the overall Macro Maker theme.", out var appearancePanel));
        AddLabel(appearancePanel, "Theme");
        _themeCombo = new ComboBox
        {
            ItemsSource = Enum.GetValues<AppTheme>(),
            SelectedItem = current.Theme,
            Margin = new Thickness(0, 0, 0, 2)
        };
        appearancePanel.Children.Add(_themeCombo);

        content.Children.Add(SectionCard("Quick Add", "Choose exactly which shortcuts show above the command list.", out var quickPanel));
        var quickButton = new Button
        {
            Content = "Edit Quick Add…",
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 150
        };
        quickButton.Click += (_, _) =>
        {
            var dialog = new QuickAddSettingsWindow(Settings.QuickAddCommands)
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true)
                Settings.QuickAddCommands = dialog.SelectedCommands.ToList();
        };
        quickPanel.Children.Add(quickButton);

        content.Children.Add(SectionCard("Command Defaults", "Set defaults separately for every command in one editor.", out var defaultsPanel));
        var defaultsButton = new Button
        {
            Content = "Edit Command Defaults…",
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 180
        };
        defaultsButton.Click += (_, _) =>
        {
            var dialog = new CommandDefaultsWindow(Settings.CommandDefaults)
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true)
                Settings.CommandDefaults = dialog.Profiles.Select(x => x.DeepClone()).ToList();
        };
        defaultsPanel.Children.Add(defaultsButton);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button { Content = "Cancel", Width = 92, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var save = new Button
        {
            Content = "Save Settings",
            Width = 120,
            Style = (Style)Application.Current.FindResource("AccentButtonStyle"),
            IsDefault = true
        };
        save.Click += (_, _) =>
        {
            Settings.Theme = _themeCombo.SelectedItem is AppTheme theme ? theme : AppTheme.Dark;
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
    }

    public AppSettings Settings { get; private set; }

    private static Border SectionCard(string title, string description, out StackPanel body)
    {
        var outer = new Border
        {
            Background = Brush("PanelBrush"),
            BorderBrush = Brush("BorderBrushDark"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush")
        });
        panel.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("MutedTextBrush"),
            Margin = new Thickness(0, 4, 0, 12)
        });
        body = new StackPanel();
        panel.Children.Add(body);
        outer.Child = panel;
        return outer;
    }

    private static void AddLabel(Panel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = Brush("MutedTextBrush"),
            Margin = new Thickness(0, 0, 0, 5)
        });
    }

    private static AppSettings Clone(AppSettings source) => new()
    {
        Theme = source.Theme,
        QuickAddCommands = source.QuickAddCommands.ToList(),
        CommandDefaults = source.CommandDefaults.Select(x => x.DeepClone()).ToList(),
        DefaultMouseMoveMode = source.DefaultMouseMoveMode,
        DefaultSmoothMoveMs = source.DefaultSmoothMoveMs,
        DefaultColorTolerance = source.DefaultColorTolerance,
        DefaultPollMs = source.DefaultPollMs
    };

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
