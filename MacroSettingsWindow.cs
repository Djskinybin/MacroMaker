using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MacroMaker;

public sealed class MacroSettingsWindow : Window
{
    private readonly List<ProjectVariable> _variables;
    private readonly List<string> _sequenceNames;
    private readonly CheckBox _useProjectSettings;
    private readonly ComboBox _startupSequence;
    private readonly CheckBox _lockMouse;
    private readonly CheckBox _showHud;
    private readonly TextBox _speedBox;
    private readonly ComboBox _hudCorner;
    private readonly TextBox _hudOpacityBox;
    private readonly CheckBox _promptVariables;
    private readonly ListBox _variableList;
    private readonly TextBox _nameBox;
    private readonly TextBox _valueBox;
    private readonly TextBox _descriptionBox;
    private readonly CheckBox _editableBox;
    private bool _updatingVariable;

    public MacroSettingsWindow(MacroProject project)
    {
        WindowTheme.Attach(this);
        Title = "Macro Settings";
        Width = 980;
        Height = 690;
        MinWidth = 840;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("BgBrush");
        Foreground = Brush("TextBrush");

        _sequenceNames = project.Sequences.Select(s => s.Name).ToList();
        _variables = (project.Variables ?? new List<ProjectVariable>()).Select(v => v.DeepClone()).ToList();
        Settings = new MacroRuntimeSettings
        {
            UseProjectRuntimeSettings = project.RuntimeSettings?.UseProjectRuntimeSettings ?? false,
            StartupSequence = project.RuntimeSettings?.StartupSequence ?? "Starting Sequence",
            LockMouseMovementWhileRunning = project.RuntimeSettings?.LockMouseMovementWhileRunning ?? false,
            ShowRunStatusHud = project.RuntimeSettings?.ShowRunStatusHud ?? true,
            PlaybackSpeedPercent = project.RuntimeSettings?.PlaybackSpeedPercent ?? 100,
            HudCorner = project.RuntimeSettings?.HudCorner ?? HudCorner.TopLeft,
            HudOpacityPercent = project.RuntimeSettings?.HudOpacityPercent ?? 92,
            PromptForVariablesOnRun = project.RuntimeSettings?.PromptForVariablesOnRun ?? false
        };

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(2, 0, 2, 14) };
        heading.Children.Add(new TextBlock { Text = "Macro Settings", FontSize = 23, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock
        {
            Text = "Settings and variables saved with this macro project.",
            Foreground = Brush("MutedTextBrush"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        root.Children.Add(heading);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        var runtimeCard = Card();
        var runtime = new StackPanel();
        runtime.Children.Add(SectionTitle("Run behavior"));
        _useProjectSettings = new CheckBox
        {
            Content = "Use these run settings for this macro",
            IsChecked = Settings.UseProjectRuntimeSettings,
            Margin = new Thickness(0, 0, 0, 12)
        };
        runtime.Children.Add(_useProjectSettings);

        AddLabel(runtime, "Sequence used by Run Start");
        _startupSequence = new ComboBox { ItemsSource = _sequenceNames, SelectedItem = Settings.StartupSequence, Margin = new Thickness(0, 0, 0, 12) };
        if (_startupSequence.SelectedItem is null && _sequenceNames.Count > 0) _startupSequence.SelectedIndex = 0;
        runtime.Children.Add(_startupSequence);

        _lockMouse = new CheckBox { Content = "Lock physical mouse movement while running", IsChecked = Settings.LockMouseMovementWhileRunning, Margin = new Thickness(0, 0, 0, 8) };
        runtime.Children.Add(_lockMouse);
        _showHud = new CheckBox { Content = "Show live status HUD", IsChecked = Settings.ShowRunStatusHud, Margin = new Thickness(0, 0, 0, 12) };
        runtime.Children.Add(_showHud);

        AddLabel(runtime, "Playback speed (%)");
        _speedBox = new TextBox { Text = Settings.PlaybackSpeedPercent.ToString(), Margin = new Thickness(0, 0, 0, 12) };
        runtime.Children.Add(_speedBox);

        AddLabel(runtime, "HUD position");
        _hudCorner = new ComboBox { ItemsSource = Enum.GetValues<HudCorner>(), SelectedItem = Settings.HudCorner, Margin = new Thickness(0, 0, 0, 12) };
        runtime.Children.Add(_hudCorner);

        AddLabel(runtime, "HUD opacity (%)");
        _hudOpacityBox = new TextBox { Text = Settings.HudOpacityPercent.ToString(), Margin = new Thickness(0, 0, 0, 12) };
        runtime.Children.Add(_hudOpacityBox);

        _promptVariables = new CheckBox
        {
            Content = "Ask for user-editable variable values before Run Start / Run Current",
            IsChecked = Settings.PromptForVariablesOnRun,
            Margin = new Thickness(0, 0, 0, 12)
        };
        runtime.Children.Add(_promptVariables);

        runtime.Children.Add(new TextBlock
        {
            Text = "If project run settings are off, MacroMaker uses your normal app Settings instead.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("MutedTextBrush")
        });
        runtimeCard.Child = runtime;
        Grid.SetColumn(runtimeCard, 0);
        content.Children.Add(runtimeCard);

        var variablesCard = Card();
        var variablesRoot = new Grid();
        variablesRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        variablesRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        variablesRoot.Children.Add(SectionTitle("Project variables"));

        var variableArea = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        variableArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) });
        variableArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        variableArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(variableArea, 1);
        variablesRoot.Children.Add(variableArea);

        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _variableList = new ListBox { Margin = new Thickness(0, 0, 0, 8) };
        _variableList.SelectionChanged += (_, _) => LoadSelectedVariable();
        left.Children.Add(_variableList);
        var varButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var add = new Button { Content = "+ Add", Width = 82 };
        add.Click += (_, _) => AddVariable();
        var delete = new Button { Content = "Delete", Width = 82, Margin = new Thickness(7, 0, 0, 0) };
        delete.Click += (_, _) => DeleteVariable();
        varButtons.Children.Add(add);
        varButtons.Children.Add(delete);
        Grid.SetRow(varButtons, 1);
        left.Children.Add(varButtons);
        variableArea.Children.Add(left);

        var editor = new StackPanel();
        AddLabel(editor, "Name");
        _nameBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        _nameBox.LostFocus += (_, _) => SaveSelectedVariable();
        editor.Children.Add(_nameBox);
        AddLabel(editor, "Default value");
        _valueBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        _valueBox.LostFocus += (_, _) => SaveSelectedVariable();
        editor.Children.Add(_valueBox);
        AddLabel(editor, "Description");
        _descriptionBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, Margin = new Thickness(0, 0, 0, 10) };
        _descriptionBox.LostFocus += (_, _) => SaveSelectedVariable();
        editor.Children.Add(_descriptionBox);
        _editableBox = new CheckBox { Content = "Show as a user-editable macro setting", Margin = new Thickness(0, 0, 0, 12) };
        _editableBox.Checked += (_, _) => SaveSelectedVariable();
        _editableBox.Unchecked += (_, _) => SaveSelectedVariable();
        editor.Children.Add(_editableBox);
        editor.Children.Add(new Border
        {
            Background = Brush("Panel2Brush"), BorderBrush = Brush("BorderBrushDark"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10),
            Child = new TextBlock
            {
                Text = "Use variables as {Name}. Numeric fields also accept simple math like {FoundX}+20. Built-ins: {MouseX}, {MouseY}, {ScreenWidth}, {ScreenHeight}, {Date}, {Time}, {Now}.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("MutedTextBrush")
            }
        });
        Grid.SetColumn(editor, 2);
        variableArea.Children.Add(editor);

        variablesCard.Child = variablesRoot;
        Grid.SetColumn(variablesCard, 2);
        content.Children.Add(variablesCard);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 92, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) => DialogResult = false;
        var save = new Button { Content = "Save", Width = 100, IsDefault = true, Style = (Style)Application.Current.FindResource("AccentButtonStyle") };
        save.Click += (_, _) => SaveAndClose();
        footer.Children.Add(cancel);
        footer.Children.Add(save);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        RefreshVariableList();
        if (_variables.Count > 0) _variableList.SelectedIndex = 0;
    }

    public MacroRuntimeSettings Settings { get; private set; }
    public IReadOnlyList<ProjectVariable> Variables => _variables.Select(v => v.DeepClone()).ToList();

    private void AddVariable()
    {
        SaveSelectedVariable();
        var baseName = "Variable";
        var i = 1;
        var name = baseName + i;
        while (_variables.Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = baseName + ++i;
        _variables.Add(new ProjectVariable { Name = name, Value = "0" });
        RefreshVariableList();
        _variableList.SelectedIndex = _variables.Count - 1;
    }

    private void DeleteVariable()
    {
        var index = _variableList.SelectedIndex;
        if (index < 0 || index >= _variables.Count) return;
        _variables.RemoveAt(index);
        RefreshVariableList();
        if (_variables.Count > 0) _variableList.SelectedIndex = Math.Min(index, _variables.Count - 1);
        else ClearVariableEditor();
    }

    private void RefreshVariableList()
    {
        _variableList.ItemsSource = null;
        _variableList.ItemsSource = _variables.Select(v => v.Name).ToList();
    }

    private void LoadSelectedVariable()
    {
        if (_updatingVariable) return;
        var index = _variableList.SelectedIndex;
        if (index < 0 || index >= _variables.Count) { ClearVariableEditor(); return; }
        _updatingVariable = true;
        var variable = _variables[index];
        _nameBox.Text = variable.Name;
        _valueBox.Text = variable.Value;
        _descriptionBox.Text = variable.Description;
        _editableBox.IsChecked = variable.UserEditable;
        _updatingVariable = false;
    }

    private void SaveSelectedVariable()
    {
        if (_updatingVariable) return;
        var index = _variableList.SelectedIndex;
        if (index < 0 || index >= _variables.Count) return;
        var old = _variables[index];
        var name = _nameBox.Text.Trim();
        if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$") || _variables.Where((_, i) => i != index).Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            _nameBox.Text = old.Name;
            return;
        }
        old.Name = name;
        old.Value = _valueBox.Text;
        old.Description = _descriptionBox.Text;
        old.UserEditable = _editableBox.IsChecked == true;
        RefreshVariableList();
        _variableList.SelectedIndex = index;
    }

    private void ClearVariableEditor()
    {
        _updatingVariable = true;
        _nameBox.Text = string.Empty;
        _valueBox.Text = string.Empty;
        _descriptionBox.Text = string.Empty;
        _editableBox.IsChecked = false;
        _updatingVariable = false;
    }

    private void SaveAndClose()
    {
        SaveSelectedVariable();
        var speed = int.TryParse(_speedBox.Text, out var speedValue) ? Math.Clamp(speedValue, 10, 400) : 100;
        var opacity = int.TryParse(_hudOpacityBox.Text, out var opacityValue) ? Math.Clamp(opacityValue, 35, 100) : 92;
        Settings = new MacroRuntimeSettings
        {
            UseProjectRuntimeSettings = _useProjectSettings.IsChecked == true,
            StartupSequence = _startupSequence.SelectedItem as string ?? "Starting Sequence",
            LockMouseMovementWhileRunning = _lockMouse.IsChecked == true,
            ShowRunStatusHud = _showHud.IsChecked == true,
            PlaybackSpeedPercent = speed,
            HudCorner = _hudCorner.SelectedItem is HudCorner corner ? corner : HudCorner.TopLeft,
            HudOpacityPercent = opacity,
            PromptForVariablesOnRun = _promptVariables.IsChecked == true
        };
        DialogResult = true;
    }

    private static Border Card() => new()
    {
        Background = Brush("PanelBrush"), BorderBrush = Brush("BorderBrushDark"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(15)
    };

    private static TextBlock SectionTitle(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold };
    private static void AddLabel(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, Foreground = Brush("MutedTextBrush"), Margin = new Thickness(0, 0, 0, 5) });
    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
