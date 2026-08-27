using System.IO;
using System.Diagnostics;

namespace MacroMaker;

public sealed class MacroEngine
{
    private readonly MacroProject _project;
    private readonly Random _random = new();
    private CancellationTokenSource? _cts;
    private bool _paused;

    public MacroEngine(MacroProject project)
    {
        _project = project;
    }

    public bool IsRunning { get; private set; }
    public bool IsPaused => _paused;

    public event Action<string>? StatusChanged;
    public event Action? StateChanged;
    public event Action<string, Guid>? CommandStarted;

    public async Task StartAsync(string sequenceName)
    {
        if (IsRunning)
            return;

        _cts = new CancellationTokenSource();
        _paused = false;
        IsRunning = true;
        StateChanged?.Invoke();
        StatusChanged?.Invoke($"Running: {sequenceName}");

        try
        {
            await RunSequenceAsync(sequenceName, _cts.Token, 0);
            if (!_cts.IsCancellationRequested)
                StatusChanged?.Invoke("Finished");
        }
        catch (OperationCanceledException)
        {
            StatusChanged?.Invoke("Stopped");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("Error");
            throw new InvalidOperationException($"Macro stopped: {ex.Message}", ex);
        }
        finally
        {
            IsRunning = false;
            _paused = false;
            _cts.Dispose();
            _cts = null;
            StateChanged?.Invoke();
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public void TogglePause()
    {
        if (!IsRunning)
            return;

        _paused = !_paused;
        StatusChanged?.Invoke(_paused ? "Paused (F9 resumes)" : "Resumed");
        StateChanged?.Invoke();
    }

    private async Task RunSequenceAsync(string sequenceName, CancellationToken token, int depth)
    {
        if (depth > 32)
            throw new InvalidOperationException("Sequence call depth exceeded 32. Check for accidental sequence recursion.");

        var sequence = _project.Sequences.FirstOrDefault(s =>
            s.Name.Equals(sequenceName, StringComparison.OrdinalIgnoreCase));

        if (sequence is null)
            throw new InvalidOperationException($"Sequence '{sequenceName}' does not exist.");

        var signal = await ExecuteCommandsAsync(sequence.Name, sequence.Commands, token, depth);
        _ = signal; // Return is consumed at a sequence boundary.
    }

    private async Task<ExecutionSignal> ExecuteCommandsAsync(
        string sequenceName,
        IReadOnlyList<MacroCommand> commands,
        CancellationToken token,
        int depth)
    {
        foreach (var command in commands)
        {
            token.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(token);
            CommandStarted?.Invoke(sequenceName, command.Id);

            switch (command.Type)
            {
                case CommandType.Comment:
                    break;

                case CommandType.MoveMouse:
                    await InputController.MoveMouseAsync(command.X, command.Y, EffectiveMoveDuration(command), token);
                    break;

                case CommandType.Click:
                    await MoveForClickAsync(command, token);
                    InputController.LeftClick();
                    break;

                case CommandType.DoubleClick:
                    await MoveForClickAsync(command, token);
                    InputController.LeftClick();
                    await Task.Delay(Math.Clamp(command.ClickDelayMs, 20, 1000), token);
                    InputController.LeftClick();
                    break;

                case CommandType.RightClick:
                    await MoveForClickAsync(command, token);
                    InputController.RightClick();
                    break;

                case CommandType.Scroll:
                    await MoveForClickAsync(command, token);
                    InputController.Scroll(command.ScrollAmount);
                    break;

                case CommandType.DragMouse:
                    await InputController.MoveMouseAsync(command.X, command.Y, EffectiveMoveDuration(command), token);
                    InputController.LeftDown();
                    try
                    {
                        await InputController.MoveMouseAsync(command.EndX, command.EndY, Math.Clamp(command.DragDurationMs, 1, 60000), token);
                    }
                    finally
                    {
                        InputController.LeftUp();
                    }
                    break;

                case CommandType.LeftMouseDown:
                    InputController.LeftDown();
                    break;

                case CommandType.LeftMouseUp:
                    InputController.LeftUp();
                    break;

                case CommandType.RightMouseDown:
                    InputController.RightDown();
                    break;

                case CommandType.RightMouseUp:
                    InputController.RightUp();
                    break;

                case CommandType.PressKey:
                    InputController.PressKey(command.Key);
                    break;

                case CommandType.KeyDown:
                    InputController.KeyDown(command.Key);
                    break;

                case CommandType.KeyUp:
                    InputController.KeyUp(command.Key);
                    break;

                case CommandType.TypeText:
                    InputController.TypeText(command.Text);
                    break;

                case CommandType.HoldKey:
                    InputController.KeyDown(command.Key);
                    try
                    {
                        await DelayPausableAsync(Math.Max(1, command.HoldMs), token);
                    }
                    finally
                    {
                        InputController.KeyUp(command.Key);
                    }
                    break;

                case CommandType.RepeatKey:
                    for (var i = 0; i < Math.Max(0, command.RepeatCount); i++)
                    {
                        token.ThrowIfCancellationRequested();
                        InputController.PressKey(command.Key);
                        if (i + 1 < command.RepeatCount)
                            await DelayPausableAsync(Math.Max(0, command.WaitMs), token);
                    }
                    break;

                case CommandType.WaitUntilKeyPressed:
                    if (!InputController.TryGetVirtualKey(command.Key, out _))
                        throw new InvalidOperationException($"Unknown key: {command.Key}");
                    await WaitUntilAsync(
                        () => Task.FromResult(KeyIsDown(command.Key)),
                        command,
                        $"key {command.Key}",
                        token);
                    break;

                case CommandType.Wait:
                    await DelayPausableAsync(Math.Max(0, command.WaitMs), token);
                    break;

                case CommandType.RandomWait:
                {
                    var min = Math.Max(0, Math.Min(command.MinWaitMs, command.MaxWaitMs));
                    var max = Math.Max(min, Math.Max(command.MinWaitMs, command.MaxWaitMs));
                    var delay = max == min ? min : _random.Next(min, max + 1);
                    await DelayPausableAsync(delay, token);
                    break;
                }

                case CommandType.RecordedActions:
                {
                    var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                    if (signal != ExecutionSignal.None)
                        return signal;
                    break;
                }

                case CommandType.IfColor:
                {
                    var branch = ColorCondition(command) ? command.Children : command.ElseChildren;
                    var signal = await ExecuteCommandsAsync(sequenceName, branch, token, depth);
                    if (signal != ExecutionSignal.None)
                        return signal;
                    break;
                }

                case CommandType.WaitUntilColor:
                    await WaitUntilAsync(
                        () => Task.FromResult(ColorCondition(command)),
                        command,
                        "color condition",
                        token);
                    break;

                case CommandType.LoopWhileColor:
                    while (ColorCondition(command))
                    {
                        token.ThrowIfCancellationRequested();
                        await WaitWhilePausedAsync(token);
                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break)
                            break;
                        if (signal == ExecutionSignal.Return)
                            return signal;
                        if (command.Children.Count == 0)
                            await DelayPausableAsync(Math.Max(10, command.PollMs), token);
                    }
                    break;

                case CommandType.LoopUntilColor:
                    while (!ColorCondition(command))
                    {
                        token.ThrowIfCancellationRequested();
                        await WaitWhilePausedAsync(token);
                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break)
                            break;
                        if (signal == ExecutionSignal.Return)
                            return signal;
                        if (command.Children.Count == 0)
                            await DelayPausableAsync(Math.Max(10, command.PollMs), token);
                    }
                    break;

                case CommandType.ClickColor:
                {
                    var point = await ColorFinder.FindAsync(command, token);
                    if (point is null)
                    {
                        StatusChanged?.Invoke($"Color not found: {command.ColorHex}");
                        break;
                    }

                    await InputController.MoveMouseAsync(point.Value.X, point.Value.Y, EffectiveMoveDuration(command), token);
                    InputController.LeftClick();
                    break;
                }

                case CommandType.IfImage:
                {
                    var match = await ImageMatcher.FindAsync(command, token);
                    var branch = match.HasValue ? command.Children : command.ElseChildren;
                    var signal = await ExecuteCommandsAsync(sequenceName, branch, token, depth);
                    if (signal != ExecutionSignal.None)
                        return signal;
                    break;
                }

                case CommandType.WaitUntilImage:
                    await WaitUntilAsync(
                        async () => (await ImageMatcher.FindAsync(command, token)).HasValue,
                        command,
                        "image to appear",
                        token);
                    break;

                case CommandType.WaitUntilImageGone:
                    await WaitUntilAsync(
                        async () => !(await ImageMatcher.FindAsync(command, token)).HasValue,
                        command,
                        "image to disappear",
                        token);
                    break;

                case CommandType.ClickImage:
                case CommandType.DoubleClickImage:
                case CommandType.MoveToImage:
                {
                    var match = await ImageMatcher.FindAsync(command, token);
                    if (match is null)
                    {
                        StatusChanged?.Invoke($"Image not found: {Path.GetFileName(command.ImagePath)}");
                        break;
                    }

                    var x = match.Value.CenterX + command.ImageOffsetX;
                    var y = match.Value.CenterY + command.ImageOffsetY;
                    await InputController.MoveMouseAsync(x, y, EffectiveMoveDuration(command), token);
                    if (command.Type == CommandType.ClickImage)
                    {
                        InputController.LeftClick();
                    }
                    else if (command.Type == CommandType.DoubleClickImage)
                    {
                        InputController.LeftClick();
                        await DelayPausableAsync(Math.Clamp(command.ClickDelayMs, 20, 1000), token);
                        InputController.LeftClick();
                    }
                    break;
                }

                case CommandType.LoopUntilImage:
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        await WaitWhilePausedAsync(token);
                        var match = await ImageMatcher.FindAsync(command, token);
                        if (match.HasValue)
                            break;

                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break)
                            break;
                        if (signal == ExecutionSignal.Return)
                            return signal;
                        if (command.Children.Count == 0)
                            await DelayPausableAsync(Math.Max(10, command.PollMs), token);
                    }
                    break;

                case CommandType.FocusWindow:
                    if (string.IsNullOrWhiteSpace(command.WindowTitle))
                        throw new InvalidOperationException("Enter part of the window title first.");
                    if (!WindowTools.Focus(command.WindowTitle))
                        StatusChanged?.Invoke($"Window not found: {command.WindowTitle}");
                    break;

                case CommandType.WaitForWindow:
                    if (string.IsNullOrWhiteSpace(command.WindowTitle))
                        throw new InvalidOperationException("Enter part of the window title first.");
                    await WaitUntilAsync(
                        () => Task.FromResult(WindowTools.Exists(command.WindowTitle)),
                        command,
                        $"window {command.WindowTitle}",
                        token);
                    break;

                case CommandType.WaitForWindowGone:
                    if (string.IsNullOrWhiteSpace(command.WindowTitle))
                        throw new InvalidOperationException("Enter part of the window title first.");
                    await WaitUntilAsync(
                        () => Task.FromResult(!WindowTools.Exists(command.WindowTitle)),
                        command,
                        $"window {command.WindowTitle} to close",
                        token);
                    break;

                case CommandType.RunProgram:
                    WindowTools.Open(command.ProgramPath);
                    break;

                case CommandType.RunSequence:
                    await RunSequenceAsync(command.TargetSequence, token, depth + 1);
                    break;

                case CommandType.LoopTimes:
                    for (var i = 0; i < Math.Max(0, command.RepeatCount); i++)
                    {
                        token.ThrowIfCancellationRequested();
                        await WaitWhilePausedAsync(token);
                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break)
                            break;
                        if (signal == ExecutionSignal.Return)
                            return signal;
                    }
                    break;

                case CommandType.LoopForever:
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        await WaitWhilePausedAsync(token);
                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break)
                            break;
                        if (signal == ExecutionSignal.Return)
                            return signal;
                        if (command.Children.Count == 0)
                            await DelayPausableAsync(10, token);
                    }
                    break;

                case CommandType.Break:
                    return ExecutionSignal.Break;

                case CommandType.Return:
                    return ExecutionSignal.Return;

                case CommandType.StopMacro:
                    _cts?.Cancel();
                    token.ThrowIfCancellationRequested();
                    break;
            }
        }

        return ExecutionSignal.None;
    }

    private bool ColorCondition(MacroCommand command)
    {
        var matches = ScreenTools.ColorMatches(
            command.X,
            command.Y,
            command.ColorHex,
            command.ColorTolerance);
        return command.CompareMode == CompareMode.Equals ? matches : !matches;
    }

    private static bool KeyIsDown(string key)
    {
        if (!InputController.TryGetVirtualKey(key, out var vk))
            return false;
        return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    private static int EffectiveMoveDuration(MacroCommand command)
    {
        var mode = command.MouseMoveMode == MouseMoveMode.Legacy
            ? (command.MoveDurationMs > 0 ? MouseMoveMode.Smooth : MouseMoveMode.Teleport)
            : command.MouseMoveMode;
        return mode == MouseMoveMode.Smooth
            ? Math.Clamp(command.MoveDurationMs, 1, 60000)
            : 0;
    }

    private async Task MoveForClickAsync(MacroCommand command, CancellationToken token)
    {
        await InputController.MoveMouseAsync(command.X, command.Y, EffectiveMoveDuration(command), token);
    }

    private async Task WaitUntilAsync(
        Func<Task<bool>> predicate,
        MacroCommand command,
        string description,
        CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var timeout = Math.Max(0, command.TimeoutMs);
        var poll = Math.Clamp(command.PollMs, 10, 5000);

        while (true)
        {
            token.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(token);

            if (await predicate())
                return;

            if (timeout > 0 && stopwatch.ElapsedMilliseconds >= timeout)
            {
                StatusChanged?.Invoke($"Timed out waiting for {description}");
                return;
            }

            await DelayPausableAsync(poll, token);
        }
    }

    private async Task DelayPausableAsync(int milliseconds, CancellationToken token)
    {
        var remaining = milliseconds;
        while (remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(token);
            var chunk = Math.Min(remaining, 50);
            await Task.Delay(chunk, token);
            remaining -= chunk;
        }
    }

    private async Task WaitWhilePausedAsync(CancellationToken token)
    {
        while (_paused)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(50, token);
        }
    }

    private enum ExecutionSignal
    {
        None,
        Break,
        Return
    }
}
