using System.Windows;
using System.Windows.Threading;

namespace MacroMaker;

public partial class MainWindow
{
    private RunTrace? _lastRunTrace;
    private bool _runInProgress;

    private async Task RunFromUi(string label, Func<MacroEngine, Task> start, bool testing = false)
    {
        if (_engine is not { } engine || engine.IsRunning || _runInProgress || _isRecording)
            return;

        if (!testing)
        {
            if (!TryPrepareRunOverrides(out var runValues))
                return;
            engine.SetRunOverrides(runValues);
        }

        _runInProgress = true;
        UpdateRunButtons();

        var progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        progressTimer.Tick += (_, _) => UpdateRunProgress(engine);

        Exception? error = null;
        try
        {
            Hide();
            FocusLastExternalWindow();
            engine.PlaybackSpeedPercent = EffectivePlaybackSpeed;
            if (EffectiveShowHud)
            {
                _runStatusWindow = new RunStatusWindow(
                    _appSettings.StopMacroHotkey, _appSettings.PauseMacroHotkey, EffectiveHudCorner, EffectiveHudOpacity);
                _runStatusWindow.Show();
                _runStatusWindow.UpdateStatus(testing ? "Starting test…" : $"Starting {label}…");
            }

            if (EffectiveLockMouse)
                EnableMouseMovementLock();

            await Task.Delay(120);
            var run = start(engine);
            progressTimer.Start();
            UpdateRunProgress(engine);
            await run;
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            progressTimer.Stop();
            _lastRunTrace = engine.LastRunTrace;
            UpdateRunProgress(engine);
            DisableMouseMovementLock();
            _runStatusWindow?.Close();
            _runStatusWindow = null;
            _runInProgress = false;
            UpdateRunButtons();
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        if (error is not null)
        {
            var message = FriendlyErrorMessage(error, testing ? "testing the selected step" : $"running {label}");
            if (_lastRunTrace is not null)
                message += "\n\nOpen Run Log to see what happened and go to the step.";
            MessageBox.Show(this, message, testing ? "Command test stopped" : "Macro stopped", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateRunProgress(MacroEngine engine)
    {
        if (engine.LastRunTrace is not { } trace)
            return;

        var (heading, detail) = trace.GetProgress();
        StatusText.Text = engine.IsPaused ? "Paused" : heading;
        _runStatusWindow?.UpdateStatus(StatusText.Text, engine.IsPaused ? heading : detail);
    }

    private void RunLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastRunTrace is not { } trace)
            return;

        var dialog = new RunLogWindow(trace) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedCommandId is Guid commandId)
            GoToRunStep(commandId);
    }

    private void GoToRunStep(Guid commandId)
    {
        var tab = _project.Sequences.FirstOrDefault(sequence => RevealCommand(sequence.Commands, commandId));
        if (tab is null)
        {
            StatusText.Text = "That step is no longer in this project";
            return;
        }

        if (tab != _currentSequence)
            SelectSequence(tab);
        RefreshCommandList(commandId, new[] { commandId });
        CommandList.Focus();
    }

    private bool RevealCommand(IEnumerable<MacroCommand> commands, Guid commandId)
    {
        foreach (var command in commands)
        {
            if (command.Id == commandId)
                return true;
            if (RevealCommand(command.Children, commandId) || RevealCommand(command.ElseChildren, commandId))
            {
                _collapsedBlocks.Remove(command.Id);
                return true;
            }
        }
        return false;
    }
}
