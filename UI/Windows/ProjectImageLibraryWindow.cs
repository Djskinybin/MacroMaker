using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MacroMaker;

public sealed class ProjectImageLibraryWindow : Window
{
    private readonly string _projectFolder;
    private readonly string _imagesRoot;
    private readonly ListBox _folderList;
    private readonly ListBox _imageList;
    private readonly TextBlock _folderTitle;
    private readonly TextBlock _folderSummary;
    private readonly Button _useFolderButton;
    private readonly Button _useImageButton;
    private readonly List<FolderEntry> _folders = new();

    public ProjectImageLibraryWindow(string projectFolder, string currentPath, bool currentIsFolder)
    {
        _projectFolder = projectFolder;
        _imagesRoot = Path.Combine(projectFolder, "Images");
        Directory.CreateDirectory(_imagesRoot);

        WindowTheme.Attach(this);
        Title = "Project Image Library";
        Width = 790;
        Height = 590;
        MinWidth = 560;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("BgBrush");
        Foreground = Brush("TextBrush");

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(2, 0, 0, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = "Project Image Library",
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush")
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Choose any imported image or folder. Nested folders stay intact inside this macro project.",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = Brush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(285) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var foldersCard = Card();
        var foldersPanel = new DockPanel();
        var foldersLabel = new TextBlock
        {
            Text = "Folders",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush"),
            Margin = new Thickness(0, 0, 0, 9)
        };
        DockPanel.SetDock(foldersLabel, Dock.Top);
        foldersPanel.Children.Add(foldersLabel);

        _folderList = new ListBox
        {
            Background = Brush("InputBrush"),
            Foreground = Brush("TextBrush"),
            BorderBrush = Brush("BorderBrushDark"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6)
        };
        _folderList.SelectionChanged += (_, _) => RefreshFolderContents();
        foldersPanel.Children.Add(_folderList);
        foldersCard.Child = foldersPanel;
        Grid.SetColumn(foldersCard, 0);
        content.Children.Add(foldersCard);

        var itemsCard = Card();
        var items = new Grid();
        items.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        items.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        items.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        items.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _folderTitle = new TextBlock
        {
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(_folderTitle, 0);
        items.Children.Add(_folderTitle);

        _folderSummary = new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 10),
            Foreground = Brush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(_folderSummary, 1);
        items.Children.Add(_folderSummary);

        _useFolderButton = new Button
        {
            Content = "Use This Folder",
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 130,
            Margin = new Thickness(0, 0, 0, 10),
            Style = (Style)Application.Current.FindResource("AccentButtonStyle")
        };
        _useFolderButton.Click += (_, _) => ChooseCurrentFolder();
        Grid.SetRow(_useFolderButton, 2);
        items.Children.Add(_useFolderButton);

        _imageList = new ListBox
        {
            Background = Brush("InputBrush"),
            Foreground = Brush("TextBrush"),
            BorderBrush = Brush("BorderBrushDark"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6)
        };
        _imageList.MouseDoubleClick += (_, _) => ChooseSelectedImage();
        Grid.SetRow(_imageList, 3);
        items.Children.Add(_imageList);

        itemsCard.Child = items;
        Grid.SetColumn(itemsCard, 2);
        content.Children.Add(itemsCard);

        Grid.SetRow(content, 1);
        root.Children.Add(content);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "Cancel", Width = 92, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        _useImageButton = new Button
        {
            Content = "Use Selected Image",
            MinWidth = 145,
            IsEnabled = false,
            Style = (Style)Application.Current.FindResource("AccentButtonStyle")
        };
        _useImageButton.Click += (_, _) => ChooseSelectedImage();
        _imageList.SelectionChanged += (_, _) => _useImageButton.IsEnabled = _imageList.SelectedItem is ImageEntry;
        actions.Children.Add(cancel);
        actions.Children.Add(_useImageButton);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        Content = root;

        LoadFolders();
        SelectInitialAsset(currentPath, currentIsFolder);
    }

    public string SelectedRelativePath { get; private set; } = string.Empty;
    public bool SelectedIsFolder { get; private set; }

    private void LoadFolders()
    {
        _folders.Clear();

        var allFolders = Directory.EnumerateDirectories(_imagesRoot, "*", SearchOption.AllDirectories)
            .Prepend(_imagesRoot)
            .Where(ContainsSupportedImage)
            .OrderBy(folder => Path.GetRelativePath(_imagesRoot, folder), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var folder in allFolders)
        {
            var relative = Path.GetRelativePath(_imagesRoot, folder);
            var depth = relative == "." ? 0 : relative.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar) + 1;
            var name = relative == "." ? "Images" : Path.GetFileName(folder);
            var direct = Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly).Count(IsImageFile);
            var total = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories).Count(IsImageFile);
            var indent = new string(' ', depth * 3);
            var countText = total == direct ? $"{total}" : $"{direct} here / {total} total";
            _folders.Add(new FolderEntry(folder, $"{indent}{name}   ({countText})"));
        }

        _folderList.ItemsSource = _folders;
        if (_folders.Count > 0)
            _folderList.SelectedIndex = 0;
    }

    private void SelectInitialAsset(string currentPath, bool currentIsFolder)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
            return;

        var full = ProjectPaths.Resolve(currentPath);
        var targetFolder = currentIsFolder ? full : Path.GetDirectoryName(full);
        if (string.IsNullOrWhiteSpace(targetFolder))
            return;

        var folderEntry = _folders.FirstOrDefault(f =>
            Path.GetFullPath(f.FullPath).Equals(Path.GetFullPath(targetFolder), StringComparison.OrdinalIgnoreCase));
        if (folderEntry is null)
            return;

        _folderList.SelectedItem = folderEntry;
        RefreshFolderContents();

        if (!currentIsFolder)
        {
            var image = _imageList.Items.OfType<ImageEntry>().FirstOrDefault(i =>
                Path.GetFullPath(i.FullPath).Equals(Path.GetFullPath(full), StringComparison.OrdinalIgnoreCase));
            if (image is not null)
                _imageList.SelectedItem = image;
        }
    }

    private void RefreshFolderContents()
    {
        if (_folderList.SelectedItem is not FolderEntry folder)
        {
            _folderTitle.Text = "No folder selected";
            _folderSummary.Text = string.Empty;
            _imageList.ItemsSource = null;
            _useFolderButton.IsEnabled = false;
            return;
        }

        var relative = Path.GetRelativePath(_imagesRoot, folder.FullPath);
        _folderTitle.Text = relative == "." ? "Images" : relative;

        var directImages = Directory.EnumerateFiles(folder.FullPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(IsImageFile)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select(path => new ImageEntry(path, Path.GetFileName(path)))
            .ToList();

        var childFolders = Directory.EnumerateDirectories(folder.FullPath, "*", SearchOption.TopDirectoryOnly)
            .Count(ContainsSupportedImage);
        var totalImages = Directory.EnumerateFiles(folder.FullPath, "*.*", SearchOption.AllDirectories).Count(IsImageFile);
        _folderSummary.Text = $"{directImages.Count} image(s) here • {childFolders} child folder(s) • {totalImages} image(s) total";
        _imageList.ItemsSource = directImages;
        _useFolderButton.IsEnabled = totalImages > 0;
        _useImageButton.IsEnabled = false;
    }

    private void ChooseCurrentFolder()
    {
        if (_folderList.SelectedItem is not FolderEntry folder)
            return;

        SelectedRelativePath = Path.GetRelativePath(_projectFolder, folder.FullPath);
        SelectedIsFolder = true;
        DialogResult = true;
    }

    private void ChooseSelectedImage()
    {
        if (_imageList.SelectedItem is not ImageEntry image)
            return;

        SelectedRelativePath = Path.GetRelativePath(_projectFolder, image.FullPath);
        SelectedIsFolder = false;
        DialogResult = true;
    }

    private static bool ContainsSupportedImage(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories).Any(IsImageFile);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsImageFile(string path)
        => new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static Border Card() => new()
    {
        Background = Brush("PanelBrush"),
        BorderBrush = Brush("BorderBrushDark"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(12)
    };

    private static Brush Brush(string key)
        => (Brush)Application.Current.FindResource(key);

    private sealed record FolderEntry(string FullPath, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ImageEntry(string FullPath, string Label)
    {
        public override string ToString() => Label;
    }
}
