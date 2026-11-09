using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MacroMaker;

public partial class RunLogWindow : Window
{
    internal RunLogWindow(RunTrace trace)
    {
        InitializeComponent();
        WindowTheme.Attach(this);

        var (entries, discarded) = trace.GetEntries();
        var (outcome, _) = trace.GetProgress();
        SummaryText.Text = discarded > 0
            ? $"{outcome}. Showing the latest {entries.Length} entries. {discarded} earlier entries were omitted."
            : $"{outcome}. Select an entry to go back to its step.";
        EntriesList.ItemsSource = entries;
        var initial = entries.LastOrDefault(entry => entry.Level == RunLogLevel.Error && entry.Step is not null)
            ?? entries.LastOrDefault(entry => entry.Level == RunLogLevel.Warning && entry.Step is not null)
            ?? entries.LastOrDefault(entry => entry.Step is not null);
        if (initial is not null)
        {
            EntriesList.SelectedItem = initial;
            EntriesList.ScrollIntoView(initial);
        }
    }

    internal Guid? SelectedCommandId { get; private set; }

    private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        GoToButton.IsEnabled = (EntriesList.SelectedItem as RunLogEntry)?.Step is not null;
    }

    private void EntriesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(EntriesList, e.OriginalSource as DependencyObject) is ListBoxItem)
            GoToSelectedStep();
    }

    private void GoToButton_Click(object sender, RoutedEventArgs e) => GoToSelectedStep();

    private void GoToSelectedStep()
    {
        if (EntriesList.SelectedItem is not RunLogEntry { Step: { } step })
            return;
        SelectedCommandId = step.CommandId;
        DialogResult = true;
    }
}
