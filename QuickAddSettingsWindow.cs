using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MacroMaker;

public sealed class QuickAddSettingsWindow : Window
{
    private readonly HashSet<CommandType> _selected;
    private readonly StackPanel _categoryPanel = new();
    private readonly StackPanel _commandPanel = new();
    private readonly TextBlock _categoryTitle = new();
    private Button? _activeCategoryButton;
    private int _currentCategory;

    public QuickAddSettingsWindow(IEnumerable<CommandType> current)
    {
        WindowTheme.Attach(this);
        _selected = current.Where(CommandCatalog.CanQuickAdd).ToHashSet();

        Title = "Quick Add Editor";
        Width = 720;
        Height = 570;
        MinWidth = 640;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("BgBrush");
        Foreground = Brush("TextBrush");

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(3, 0, 3, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = "Quick Add Editor",
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush")
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Pick the commands you want shown above the main command list.",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = Brush("MutedTextBrush")
        });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var card = new Border
        {
            Background = Brush("PanelBrush"),
            BorderBrush = Brush("BorderBrushDark"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10)
        };
        Grid.SetRow(card, 1);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

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
            Background = Brush("BorderBrushDark"),
            Margin = new Thickness(0, 2, 0, 2)
        };
        Grid.SetColumn(divider, 1);
        content.Children.Add(divider);

        var right = new Grid { Margin = new Thickness(14, 0, 0, 0) };
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _categoryTitle.FontSize = 17;
        _categoryTitle.FontWeight = FontWeights.SemiBold;
        _categoryTitle.Foreground = Brush("TextBrush");
        _categoryTitle.Margin = new Thickness(2, 4, 0, 10);
        Grid.SetRow(_categoryTitle, 0);
        right.Children.Add(_categoryTitle);

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

        card.Child = content;
        root.Children.Add(card);

        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var reset = new Button
        {
            Content = "Reset to Mouse Move · Click · Wait · Record",
            MinWidth = 250,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        reset.Click += (_, _) =>
        {
            _selected.Clear();
            _selected.Add(CommandType.MoveMouse);
            _selected.Add(CommandType.Click);
            _selected.Add(CommandType.Wait);
            _selected.Add(CommandType.RecordedActions);
            ShowCategory(_currentCategory);
        };
        DockPanel.SetDock(reset, Dock.Left);
        footer.Children.Add(reset);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var save = new Button
        {
            Content = "Save Quick Add",
            Width = 120,
            Style = (Style)Application.Current.FindResource("AccentButtonStyle"),
            IsDefault = true
        };
        save.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

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

    public IReadOnlyList<CommandType> SelectedCommands =>
        CommandCatalog.AllOptions.Where(x => _selected.Contains(x.Type)).Select(x => x.Type).ToList();

    private void BuildCategories()
    {
        for (var i = 0; i < CommandCatalog.Categories.Length; i++)
        {
            var index = i;
            var button = MakeButton(CommandCatalog.Categories[i].Name, false);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 8, 6);
            button.Click += (_, _) => ShowCategory(index);
            _categoryPanel.Children.Add(button);
        }
    }

    private void ShowCategory(int index)
    {
        if (index < 0 || index >= CommandCatalog.Categories.Length)
            return;
        _currentCategory = index;

        if (_activeCategoryButton is not null)
        {
            _activeCategoryButton.Background = Brush("Panel2Brush");
            _activeCategoryButton.BorderBrush = Brush("BorderBrushDark");
        }

        _activeCategoryButton = _categoryPanel.Children[index] as Button;
        if (_activeCategoryButton is not null)
        {
            _activeCategoryButton.Background = Brush("Panel3Brush");
            _activeCategoryButton.BorderBrush = Brush("AccentBrush");
        }

        var category = CommandCatalog.Categories[index];
        _categoryTitle.Text = category.Name;
        _commandPanel.Children.Clear();

        foreach (var option in category.Commands)
        {
            var button = MakeToggleButton(option);
            _commandPanel.Children.Add(button);
        }
    }

    private Button MakeToggleButton(CommandCatalog.Option option)
    {
        var enabled = _selected.Contains(option.Type);
        var label = new TextBlock
        {
            Text = option.Label,
            Foreground = Brush("TextBrush"),
            FontWeight = FontWeights.Medium
        };
        var labelStack = new StackPanel();
        labelStack.Children.Add(label);

        var state = new TextBlock
        {
            Text = enabled ? "✓ Shown" : "Hidden",
            Foreground = enabled ? Brush("AccentBrush") : Brush("MutedTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var dock = new DockPanel();
        DockPanel.SetDock(state, Dock.Right);
        dock.Children.Add(state);
        dock.Children.Add(labelStack);

        var button = new Button
        {
            Content = dock,
            Background = enabled ? Brush("ItemSelectedBrush") : Brush("InputBrush"),
            BorderBrush = enabled ? Brush("AccentBrush") : Brush("BorderBrushDark"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            MinHeight = 50,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 7),
            Cursor = Cursors.Hand
        };
        button.Click += (_, _) =>
        {
            if (!_selected.Add(option.Type))
                _selected.Remove(option.Type);
            ShowCategory(_currentCategory);
        };
        return button;
    }

    private static Button MakeButton(string text, bool command)
    {
        return new Button
        {
            Content = text,
            Foreground = Brush("TextBrush"),
            Background = command ? Brush("InputBrush") : Brush("Panel2Brush"),
            BorderBrush = Brush("BorderBrushDark"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 9, 12, 9),
            MinHeight = 38,
            Cursor = Cursors.Hand
        };
    }

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
