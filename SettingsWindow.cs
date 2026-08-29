using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MacroMaker;

public sealed class SettingsWindow : Window
{
    private readonly ComboBox _themeCombo;
    private readonly CheckBox _autoUpdateCheck;
    private readonly TextBox _stopHotkeyBox;
    private readonly TextBox _pauseHotkeyBox;
    private readonly TextBox _runStartHotkeyBox;
    private readonly TextBox _runCurrentHotkeyBox;
    private readonly CheckBox _lockMouseCheck;
    private readonly CheckBox _showRunHudCheck;
    private readonly TextBox _playbackSpeedBox;
    private readonly ComboBox _hudCornerCombo;
    private readonly TextBox _hudOpacityBox;
    private readonly CheckBox _autoSaveCheck;

    public SettingsWindow(AppSettings current)
    {
        Settings = Clone(current);
        WindowTheme.Attach(this);

        Title = "Macro Maker Settings";
        Width = 660;
        Height = 680;
        MinWidth = 580;
        MinHeight = 520;
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
            Text = "Appearance, Quick Add, command defaults, and updates.",
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

        content.Children.Add(SectionCard("Editor", "Project editing and saving behavior.", out var editorPanel));
        _autoSaveCheck = new CheckBox
        {
            Content = "Auto-save saved projects after changes",
            IsChecked = current.AutoSaveProjectChanges
        };
        editorPanel.Children.Add(_autoSaveCheck);

        content.Children.Add(SectionCard("Controls & Running", "Set global macro hotkeys and run behavior.", out var controlsPanel));

        AddLabel(controlsPanel, "Stop macro hotkey");
        _stopHotkeyBox = new TextBox
        {
            Text = current.StopMacroHotkey,
            Margin = new Thickness(0, 0, 0, 10)
        };
        controlsPanel.Children.Add(_stopHotkeyBox);

        AddLabel(controlsPanel, "Pause / resume hotkey");
        _pauseHotkeyBox = new TextBox
        {
            Text = current.PauseMacroHotkey,
            Margin = new Thickness(0, 0, 0, 10)
        };
        controlsPanel.Children.Add(_pauseHotkeyBox);

        AddLabel(controlsPanel, "Run Macro hotkey (optional)");
        _runStartHotkeyBox = new TextBox
        {
            Text = current.RunStartHotkey,
            Margin = new Thickness(0, 0, 0, 10)
        };
        controlsPanel.Children.Add(_runStartHotkeyBox);

        AddLabel(controlsPanel, "Run Current Tab hotkey (optional)");
        _runCurrentHotkeyBox = new TextBox
        {
            Text = current.RunCurrentHotkey,
            Margin = new Thickness(0, 0, 0, 12)
        };
        controlsPanel.Children.Add(_runCurrentHotkeyBox);

        _lockMouseCheck = new CheckBox
        {
            Content = "Lock physical mouse movement while a macro is running",
            IsChecked = current.LockMouseMovementWhileRunning,
            Margin = new Thickness(0, 0, 0, 8)
        };
        controlsPanel.Children.Add(_lockMouseCheck);

        _showRunHudCheck = new CheckBox
        {
            Content = "Show live macro status in the top-left while running",
            IsChecked = current.ShowRunStatusHud
        };
        controlsPanel.Children.Add(_showRunHudCheck);

        AddLabel(controlsPanel, "Default playback speed (%)");
        _playbackSpeedBox = new TextBox
        {
            Text = current.PlaybackSpeedPercent.ToString(),
            Margin = new Thickness(0, 0, 0, 10)
        };
        controlsPanel.Children.Add(_playbackSpeedBox);

        AddLabel(controlsPanel, "HUD position");
        _hudCornerCombo = new ComboBox
        {
            ItemsSource = Enum.GetValues<HudCorner>(),
            SelectedItem = current.RunHudCorner,
            Margin = new Thickness(0, 0, 0, 10)
        };
        controlsPanel.Children.Add(_hudCornerCombo);

        AddLabel(controlsPanel, "HUD opacity (%)");
        _hudOpacityBox = new TextBox
        {
            Text = current.RunHudOpacityPercent.ToString(),
            Margin = new Thickness(0, 0, 0, 10)
        };
        controlsPanel.Children.Add(_hudOpacityBox);

        controlsPanel.Children.Add(new TextBlock
        {
            Text = "Hotkeys can be a key like F8 or a combo like Ctrl+F8. Leave optional run hotkeys blank to disable them.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("MutedTextBrush"),
            Margin = new Thickness(0, 10, 0, 0)
        });

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

        content.Children.Add(SectionCard("Updates", "Keep MacroMaker current using GitHub Releases.", out var updatesPanel));
        _autoUpdateCheck = new CheckBox
        {
            Content = "Check for updates when MacroMaker starts",
            IsChecked = current.CheckForUpdatesOnStartup,
            Margin = new Thickness(0, 0, 0, 10)
        };
        updatesPanel.Children.Add(_autoUpdateCheck);

        var versionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        versionRow.Children.Add(new TextBlock
        {
            Text = $"Installed version: {UpdateService.CurrentVersionText}",
            Foreground = Brush("MutedTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        });
        var checkNow = new Button
        {
            Content = "Check for Updates",
            MinWidth = 140
        };
        checkNow.Click += async (_, _) =>
        {
            checkNow.IsEnabled = false;
            try
            {
                await UpdateService.CheckAndPromptAsync(this, true);
            }
            finally
            {
                checkNow.IsEnabled = true;
            }
        };
        versionRow.Children.Add(checkNow);
        updatesPanel.Children.Add(versionRow);

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
            Settings.StopMacroHotkey = _stopHotkeyBox.Text.Trim();
            Settings.PauseMacroHotkey = _pauseHotkeyBox.Text.Trim();
            Settings.RunStartHotkey = _runStartHotkeyBox.Text.Trim();
            Settings.RunCurrentHotkey = _runCurrentHotkeyBox.Text.Trim();
            Settings.LockMouseMovementWhileRunning = _lockMouseCheck.IsChecked == true;
            Settings.ShowRunStatusHud = _showRunHudCheck.IsChecked == true;
            Settings.PlaybackSpeedPercent = int.TryParse(_playbackSpeedBox.Text, out var speed) ? Math.Clamp(speed, 10, 400) : 100;
            Settings.RunHudCorner = _hudCornerCombo.SelectedItem is HudCorner corner ? corner : HudCorner.TopLeft;
            Settings.RunHudOpacityPercent = int.TryParse(_hudOpacityBox.Text, out var opacity) ? Math.Clamp(opacity, 35, 100) : 92;
            Settings.AutoSaveProjectChanges = _autoSaveCheck.IsChecked == true;
            Settings.CheckForUpdatesOnStartup = _autoUpdateCheck.IsChecked == true;
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
        DefaultsRevision = source.DefaultsRevision,
        CheckForUpdatesOnStartup = source.CheckForUpdatesOnStartup,
        LastSuccessfulUpdateCheckUtc = source.LastSuccessfulUpdateCheckUtc,
        StopMacroHotkey = source.StopMacroHotkey,
        PauseMacroHotkey = source.PauseMacroHotkey,
        RunStartHotkey = source.RunStartHotkey,
        RunCurrentHotkey = source.RunCurrentHotkey,
        LockMouseMovementWhileRunning = source.LockMouseMovementWhileRunning,
        ShowRunStatusHud = source.ShowRunStatusHud,
        PlaybackSpeedPercent = source.PlaybackSpeedPercent,
        RunHudCorner = source.RunHudCorner,
        RunHudOpacityPercent = source.RunHudOpacityPercent,
        ShowAdvancedCommands = true,
        AutoSaveProjectChanges = source.AutoSaveProjectChanges,
        QuickAddCommands = source.QuickAddCommands.ToList(),
        CommandDefaults = source.CommandDefaults.Select(x => x.DeepClone()).ToList(),
        DefaultMouseMoveMode = source.DefaultMouseMoveMode,
        DefaultSmoothMoveMs = source.DefaultSmoothMoveMs,
        DefaultColorTolerance = source.DefaultColorTolerance,
        DefaultPollMs = source.DefaultPollMs
    };

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
