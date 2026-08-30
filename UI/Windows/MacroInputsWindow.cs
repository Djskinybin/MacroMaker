using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MacroMaker;

public sealed class MacroInputsWindow : Window
{
    private readonly Dictionary<string, TextBox> _boxes = new(StringComparer.OrdinalIgnoreCase);

    public MacroInputsWindow(string macroName, IEnumerable<ProjectVariable> variables)
    {
        WindowTheme.Attach(this);
        Title = $"{macroName} — Run Settings";
        Width = 520;
        Height = 560;
        MinWidth = 440;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("BgBrush");
        Foreground = Brush("TextBrush");

        var editable = variables.Where(v => v.UserEditable).Select(v => v.DeepClone()).ToList();

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(2, 0, 0, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = macroName,
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush")
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Choose this run's values. These do not change the saved defaults.",
            Foreground = Brush("MutedTextBrush"),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(heading);

        var panel = new StackPanel();
        foreach (var variable in editable)
        {
            var card = new Border
            {
                Background = Brush("PanelBrush"),
                BorderBrush = Brush("BorderBrushDark"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 9)
            };
            var inside = new StackPanel();
            inside.Children.Add(new TextBlock
            {
                Text = variable.Name,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("TextBrush")
            });
            if (!string.IsNullOrWhiteSpace(variable.Description))
            {
                inside.Children.Add(new TextBlock
                {
                    Text = variable.Description,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush("MutedTextBrush"),
                    Margin = new Thickness(0, 3, 0, 7)
                });
            }
            var box = new TextBox { Text = variable.Value, Padding = new Thickness(8, 6, 8, 6) };
            _boxes[variable.Name] = box;
            inside.Children.Add(box);
            card.Child = inside;
            panel.Children.Add(card);
        }

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "Cancel", Width = 92, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var run = new Button
        {
            Content = "Run",
            Width = 100,
            IsDefault = true,
            Style = (Style)Application.Current.FindResource("AccentButtonStyle")
        };
        run.Click += (_, _) =>
        {
            Values = _boxes.ToDictionary(x => x.Key, x => x.Value.Text, StringComparer.OrdinalIgnoreCase);
            DialogResult = true;
        };
        footer.Children.Add(cancel);
        footer.Children.Add(run);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        Values = editable.ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string> Values { get; private set; }

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
