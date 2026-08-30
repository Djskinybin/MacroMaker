using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MacroMaker;

public enum WelcomeAction
{
    ContinueNew,
    OpenFolder,
    OpenRecent
}

public sealed class WelcomeWindow : Window
{
    private readonly ListBox _recentList = new();
    private readonly List<string> _recentProjects;

    public WelcomeWindow(IEnumerable<string> recentProjects)
    {
        _recentProjects = recentProjects
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        Title = "MacroMaker";
        Width = 650;
        Height = 500;
        MinWidth = 500;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = (Brush)Application.Current.FindResource("BgBrush");
        Foreground = (Brush)Application.Current.FindResource("TextBrush");
        FontFamily = new FontFamily("Segoe UI");
        WindowTheme.Attach(this);

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new TextBlock
        {
            Text = "MacroMaker",
            FontSize = 30,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "Create a new macro or continue where you left off.",
            Foreground = (Brush)Application.Current.FindResource("MutedTextBrush"),
            Margin = new Thickness(0, 5, 0, 0)
        });
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var actions = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var newButton = new Button
        {
            Content = "+ New Macro",
            Height = 48,
            Margin = new Thickness(0, 0, 6, 0),
            Style = (Style)Application.Current.FindResource("AccentButtonStyle")
        };
        var openButton = new Button
        {
            Content = "Open Project",
            Height = 48,
            Margin = new Thickness(6, 0, 0, 0)
        };
        newButton.Click += (_, _) => Finish(WelcomeAction.ContinueNew);
        openButton.Click += (_, _) => Finish(WelcomeAction.OpenFolder);
        Grid.SetColumn(newButton, 0);
        Grid.SetColumn(openButton, 1);
        actions.Children.Add(newButton);
        actions.Children.Add(openButton);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);

        var recentCard = new Border
        {
            Background = (Brush)Application.Current.FindResource("PanelBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14)
        };
        var recentGrid = new Grid();
        recentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        recentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        recentGrid.Children.Add(new TextBlock
        {
            Text = "Recent Projects",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(1, 0, 0, 10)
        });

        if (_recentProjects.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "Projects you save or open will appear here.",
                Foreground = (Brush)Application.Current.FindResource("MutedTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(10)
            };
            Grid.SetRow(empty, 1);
            recentGrid.Children.Add(empty);
        }
        else
        {
            _recentList.ItemsSource = _recentProjects.Select(path => new RecentProjectItem(path)).ToList();
            _recentList.MouseDoubleClick += (_, _) => OpenSelectedRecent();
            _recentList.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    OpenSelectedRecent();
                    e.Handled = true;
                }
            };
            Grid.SetRow(_recentList, 1);
            recentGrid.Children.Add(_recentList);
        }

        recentCard.Child = recentGrid;
        Grid.SetRow(recentCard, 2);
        root.Children.Add(recentCard);

        var hint = new TextBlock
        {
            Text = "Tip: a MacroMaker project is a folder containing macro.json and its Images folder.",
            Foreground = (Brush)Application.Current.FindResource("MutedTextBrush"),
            FontSize = 11,
            Margin = new Thickness(2, 12, 0, 0)
        };
        Grid.SetRow(hint, 3);
        root.Children.Add(hint);

        Content = root;
    }

    public WelcomeAction Action { get; private set; } = WelcomeAction.ContinueNew;
    public string? SelectedProjectPath { get; private set; }

    private void OpenSelectedRecent()
    {
        if (_recentList.SelectedItem is not RecentProjectItem item)
            return;
        SelectedProjectPath = item.Path;
        Finish(WelcomeAction.OpenRecent);
    }

    private void Finish(WelcomeAction action)
    {
        Action = action;
        DialogResult = true;
        Close();
    }

    private sealed class RecentProjectItem
    {
        public RecentProjectItem(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            Folder = path;
        }

        public string Path { get; }
        public string Name { get; }
        public string Folder { get; }

        public override string ToString() => $"{Name}    —    {Folder}";
    }
}
