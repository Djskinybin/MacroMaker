using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace MacroMaker;

public partial class MainWindow
{
    private void AddMouseDestinationFields(MacroCommand command, string locationLabel = "Location")
    {
        AddLabel("Destination");
        var mode = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        mode.Items.Add(new ComboBoxItem { Content = "Exact point", Tag = MouseTargetMode.Exact });
        mode.Items.Add(new ComboBoxItem { Content = "Random point in an area", Tag = MouseTargetMode.RandomArea });
        mode.Items.Add(new ComboBoxItem { Content = "Random from saved points", Tag = MouseTargetMode.RandomSavedPoint });
        mode.SelectedItem = mode.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is MouseTargetMode target && target == command.MouseTargetMode);
        AutomationProperties.SetName(mode, "Destination");
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedItem is not ComboBoxItem { Tag: MouseTargetMode selected } || selected == command.MouseTargetMode)
                return;

            command.MouseTargetMode = selected;
            CommitPropertySelection(command, mode);
        };
        PropertiesPanel.Children.Add(mode);

        switch (command.MouseTargetMode)
        {
            case MouseTargetMode.Exact:
                AddLocationFields(command, locationLabel);
                break;
            case MouseTargetMode.RandomArea:
                AddMouseTargetArea(command);
                break;
            case MouseTargetMode.RandomSavedPoint:
                AddMouseTargetPoints(command);
                break;
        }
    }

    private void AddMouseTargetArea(MacroCommand command)
    {
        AddInfo(command.MouseTargetArea is { IsEmpty: false } area
            ? $"Screen area at {area.X}, {area.Y} ({area.Width} × {area.Height})"
            : "Pick the screen area to choose from.");
        var button = new Button { Content = "Pick Area", Margin = new Thickness(0, 0, 0, 10) };
        button.Click += (_, _) =>
        {
            var picker = new RegionPickerWindow();
            if (ReadMousePicker(picker, () => picker.Region) is not { IsEmpty: false } selected)
                return;

            command.MouseTargetArea = selected;
            MarkDirtyAndRefresh(command.Id);
        };
        PropertiesPanel.Children.Add(button);
    }

    private void AddMouseTargetPoints(MacroCommand command)
    {
        var points = command.MouseTargetPoints;
        var list = new ListBox
        {
            ItemsSource = points?.Select(point => $"{point.X}, {point.Y}").ToArray(),
            MaxHeight = 140,
            Margin = new Thickness(0, 0, 0, 7)
        };
        AutomationProperties.SetName(list, "Saved destination points");
        if (points is { Count: > 0 })
        {
            list.SelectedIndex = points.Count - 1;
            PropertiesPanel.Children.Add(list);
        }
        else
        {
            AddInfo("Add screen points to choose from.");
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var add = new Button { Content = "Add Point", Margin = new Thickness(0, 0, 6, 0) };
        add.Click += (_, _) =>
        {
            if (ReadMousePoint() is not { } point)
                return;

            command.MouseTargetPoints ??= new List<MousePoint>();
            command.MouseTargetPoints.Add(point);
            MarkDirtyAndRefresh(command.Id);
        };
        buttons.Children.Add(add);

        if (points is { Count: > 0 })
        {
            var remove = new Button { Content = "Remove Selected" };
            remove.Click += (_, _) =>
            {
                if (list.SelectedIndex < 0)
                    return;

                points.RemoveAt(list.SelectedIndex);
                MarkDirtyAndRefresh(command.Id);
            };
            buttons.Children.Add(remove);
        }

        PropertiesPanel.Children.Add(buttons);
    }

    private void AddMouseCurveFields(MacroCommand command)
    {
        AddInfo(command.MouseCurvePoint is { } point
            ? $"Path curves toward screen point {point.X}, {point.Y}."
            : "Path follows a straight line.");
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var pick = new Button { Content = "Pick Curve Point", Margin = new Thickness(0, 0, 6, 0) };
        pick.Click += (_, _) =>
        {
            if (ReadMousePoint() is not { } selected)
                return;

            command.MouseCurvePoint = selected;
            MarkDirtyAndRefresh(command.Id);
        };
        buttons.Children.Add(pick);
        if (command.MouseCurvePoint.HasValue)
        {
            var clear = new Button { Content = "Use Straight Path" };
            clear.Click += (_, _) =>
            {
                command.MouseCurvePoint = null;
                MarkDirtyAndRefresh(command.Id);
            };
            buttons.Children.Add(clear);
        }

        PropertiesPanel.Children.Add(buttons);
    }

    private MousePoint? ReadMousePoint()
    {
        var picker = new PointPickerWindow(false);
        return ReadMousePicker(picker, () => new MousePoint(picker.PickedX, picker.PickedY));
    }

    private T? ReadMousePicker<T>(Window picker, Func<T> readValue) where T : struct
    {
        Hide();
        try
        {
            return picker.ShowDialog() == true ? readValue() : null;
        }
        finally
        {
            Show();
            Activate();
        }
    }
}
