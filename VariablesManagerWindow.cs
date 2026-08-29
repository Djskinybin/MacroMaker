using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MacroMaker;

public sealed class VariablesManagerWindow : Window
{
    private readonly List<ProjectVariable> _variables;
    private readonly ListBox _list = new();
    private readonly TextBox _nameBox = new();
    private readonly TextBox _valueBox = new();
    private readonly TextBox _descriptionBox = new();
    private readonly CheckBox _userEditable = new();
    private bool _loading;

    public VariablesManagerWindow(IEnumerable<ProjectVariable> variables)
    {
        _variables = variables.Select(v => v.DeepClone()).ToList();

        Title = "Variables";
        Width = 760;
        Height = 540;
        MinWidth = 660;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("BgBrush");
        Foreground = (Brush)Application.Current.FindResource("TextBrush");
        FontFamily = new FontFamily("Segoe UI");
        WindowTheme.Attach(this);

        var root = new Grid { Margin = new Thickness(18) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(235) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = "Variables",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold
        });
        Grid.SetColumnSpan(heading, 3);
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var left = Card();
        Grid.SetColumn(left, 0);
        Grid.SetRow(left, 1);
        var leftPanel = new Grid();
        leftPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        leftPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _list.DisplayMemberPath = "Name";
        _list.SelectionChanged += (_, _) => LoadSelected();
        Grid.SetRow(_list, 0);
        leftPanel.Children.Add(_list);

        var listButtons = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        listButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        listButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var add = new Button { Content = "+ New", Margin = new Thickness(0, 0, 4, 0) };
        var remove = new Button { Content = "Delete", Margin = new Thickness(4, 0, 0, 0) };
        add.Click += (_, _) => AddVariable();
        remove.Click += (_, _) => RemoveSelected();
        Grid.SetColumn(add, 0);
        Grid.SetColumn(remove, 1);
        listButtons.Children.Add(add);
        listButtons.Children.Add(remove);
        Grid.SetRow(listButtons, 1);
        leftPanel.Children.Add(listButtons);
        left.Child = leftPanel;
        root.Children.Add(left);

        var editor = Card();
        Grid.SetColumn(editor, 2);
        Grid.SetRow(editor, 1);
        var editorPanel = new StackPanel();

        AddLabel(editorPanel, "Name");
        _nameBox.Margin = new Thickness(0, 0, 0, 4);
        _nameBox.TextChanged += (_, _) => SaveCurrent();
        editorPanel.Children.Add(_nameBox);

        AddLabel(editorPanel, "Starting value");
        _valueBox.Margin = new Thickness(0, 0, 0, 4);
        _valueBox.TextChanged += (_, _) => SaveCurrent();
        editorPanel.Children.Add(_valueBox);

        AddLabel(editorPanel, "Note (optional)");
        _descriptionBox.AcceptsReturn = true;
        _descriptionBox.Height = 72;
        _descriptionBox.TextWrapping = TextWrapping.Wrap;
        _descriptionBox.Margin = new Thickness(0, 0, 0, 12);
        _descriptionBox.TextChanged += (_, _) => SaveCurrent();
        editorPanel.Children.Add(_descriptionBox);

        _userEditable.Content = "Ask for this value before the macro runs";
        _userEditable.Margin = new Thickness(0, 0, 0, 8);
        _userEditable.Checked += (_, _) => SaveCurrent();
        _userEditable.Unchecked += (_, _) => SaveCurrent();
        editorPanel.Children.Add(_userEditable);

        editor.Child = editorPanel;
        root.Children.Add(editor);

        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 94, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "Save Variables", Width = 124, IsDefault = true };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        save.Click += (_, _) => SaveAndClose();
        DockPanel.SetDock(cancel, Dock.Right);
        DockPanel.SetDock(save, Dock.Right);
        footer.Children.Add(save);
        footer.Children.Add(cancel);
        Grid.SetColumnSpan(footer, 3);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        RefreshList();
        if (_variables.Count > 0)
            _list.SelectedIndex = 0;
        else
            SetEditorEnabled(false);
    }

    public IReadOnlyList<ProjectVariable> Variables => _variables.Select(v => v.DeepClone()).ToList();

    private static Border Card() => new()
    {
        Background = (Brush)Application.Current.FindResource("PanelBrush"),
        BorderBrush = (Brush)Application.Current.FindResource("BorderBrushSoft"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(13)
    };

    private static void AddLabel(Panel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 5)
        });
    }


    private void RefreshList(ProjectVariable? select = null)
    {
        _loading = true;
        _list.ItemsSource = null;
        _list.ItemsSource = _variables;
        if (select is not null)
            _list.SelectedItem = select;
        _loading = false;
    }

    private void AddVariable()
    {
        var used = _variables.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var index = 1;
        var name = "Variable";
        while (used.Contains(name))
            name = $"Variable{++index}";

        var variable = new ProjectVariable { Name = name, Value = "0", UserEditable = false };
        _variables.Add(variable);
        RefreshList(variable);
        LoadSelected();
        _nameBox.Focus();
        _nameBox.SelectAll();
    }

    private void RemoveSelected()
    {
        if (_list.SelectedItem is not ProjectVariable variable)
            return;
        var index = _variables.IndexOf(variable);
        _variables.Remove(variable);
        RefreshList();
        if (_variables.Count > 0)
            _list.SelectedIndex = Math.Clamp(index, 0, _variables.Count - 1);
        else
        {
            _list.SelectedIndex = -1;
            SetEditorEnabled(false);
            _loading = true;
            _nameBox.Text = _valueBox.Text = _descriptionBox.Text = string.Empty;
            _userEditable.IsChecked = false;
            _loading = false;
        }
    }

    private void LoadSelected()
    {
        if (_list.SelectedItem is not ProjectVariable variable)
        {
            SetEditorEnabled(false);
            return;
        }

        SetEditorEnabled(true);
        _loading = true;
        _nameBox.Text = variable.Name;
        _valueBox.Text = variable.Value;
        _descriptionBox.Text = variable.Description;
        _userEditable.IsChecked = variable.UserEditable;
        _loading = false;
    }

    private void SaveCurrent()
    {
        if (_loading || _list.SelectedItem is not ProjectVariable variable)
            return;

        variable.Name = _nameBox.Text.Trim();
        variable.Value = _valueBox.Text;
        variable.Description = _descriptionBox.Text.Trim();
        variable.UserEditable = _userEditable.IsChecked == true;

        // Refresh the displayed name without changing the selected object.
        var selected = variable;
        RefreshList(selected);
    }

    private void SaveAndClose()
    {
        SaveCurrent();
        foreach (var variable in _variables)
        {
            variable.Name = RuntimeValues.NormalizeName(variable.Name);
            if (!Regex.IsMatch(variable.Name, "^[A-Za-z_][A-Za-z0-9_]*$"))
            {
                MessageBox.Show(this,
                    $"'{variable.Name}' is not a valid variable name.\n\nUse letters, numbers, and underscores. The first character must be a letter or underscore.",
                    "Fix Variable Name", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        var duplicate = _variables.GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            MessageBox.Show(this, $"The variable name '{duplicate.Key}' is used more than once.", "Duplicate Variable", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void SetEditorEnabled(bool enabled)
    {
        _nameBox.IsEnabled = enabled;
        _valueBox.IsEnabled = enabled;
        _descriptionBox.IsEnabled = enabled;
        _userEditable.IsEnabled = enabled;
    }
}
