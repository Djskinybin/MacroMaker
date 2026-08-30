using System.Diagnostics;
using System.IO;
using System.Windows;

namespace MacroMaker;

public sealed class MacroEngine
{
    private readonly MacroProject _project;
    private readonly RuntimeValues _values;
    private readonly object _parallelLock = new();
    private readonly Dictionary<string, Task> _parallelSequences = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeSequences = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _inputGate = new(1, 1);
    private readonly SemaphoreSlim _promptGate = new(1, 1);
    private readonly object _heldInputLock = new();
    private readonly object _cooldownLock = new();
    private readonly Dictionary<Guid, long> _conditionLastTriggered = new();
    private readonly HashSet<string> _heldKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _leftMouseHeld;
    private bool _rightMouseHeld;
    private Exception? _parallelFailure;
    private IReadOnlyDictionary<string, string>? _runOverrides;
    private CancellationTokenSource? _cts;
    private volatile bool _paused;

    public MacroEngine(MacroProject project)
    {
        _project = project;
        _values = new RuntimeValues(project);
    }

    public bool IsRunning { get; private set; }
    public bool IsPaused => _paused;
    public int PlaybackSpeedPercent { get; set; } = 100;
    public IReadOnlyDictionary<string, string> RuntimeSnapshot => _values.Snapshot();

    public void SetRunOverrides(IReadOnlyDictionary<string, string>? values)
    {
        _runOverrides = values is null
            ? null
            : new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }

    public event Action<string>? StatusChanged;
    public event Action? StateChanged;
    public event Action<string, Guid>? CommandStarted;

    public async Task StartAsync(string sequenceName)
    {
        if (IsRunning)
            return;

        BeginRun($"Running: {sequenceName}");
        try
        {
            await RunSequenceAsync(sequenceName, _cts!.Token, 0);
            await WaitForParallelSequencesAsync(_cts.Token);
            if (!_cts.IsCancellationRequested)
                StatusChanged?.Invoke("Finished");
        }
        catch (OperationCanceledException)
        {
            var parallelFailure = GetParallelFailure();
            if (parallelFailure is not null)
            {
                StatusChanged?.Invoke("Error");
                throw new InvalidOperationException($"A simultaneously running tab failed: {parallelFailure.Message}", parallelFailure);
            }
            StatusChanged?.Invoke("Stopped");
        }
        catch (Exception ex)
        {
            _cts?.Cancel();
            StatusChanged?.Invoke("Error");
            throw new InvalidOperationException($"Macro stopped: {ex.Message}", ex);
        }
        finally
        {
            await ShutdownRunAsync();
        }
    }

    public async Task StartCommandAsync(MacroCommand command, string label = "Selected Command")
    {
        await StartCommandsAsync(new[] { command }, label, "Test finished");
    }

    public async Task StartCommandsAsync(IReadOnlyList<MacroCommand> commands, string label = "Commands", string finishedText = "Finished")
    {
        if (IsRunning)
            return;

        BeginRun($"Running: {label}");
        try
        {
            await ExecuteCommandsAsync(label, commands, _cts!.Token, 0);
            await WaitForParallelSequencesAsync(_cts.Token);
            if (!_cts.IsCancellationRequested)
                StatusChanged?.Invoke(finishedText);
        }
        catch (OperationCanceledException)
        {
            var parallelFailure = GetParallelFailure();
            if (parallelFailure is not null)
            {
                StatusChanged?.Invoke("Error");
                throw new InvalidOperationException($"A simultaneously running tab failed: {parallelFailure.Message}", parallelFailure);
            }
            StatusChanged?.Invoke("Stopped");
        }
        catch (Exception ex)
        {
            _cts?.Cancel();
            StatusChanged?.Invoke("Error");
            throw new InvalidOperationException($"Macro stopped: {ex.Message}", ex);
        }
        finally
        {
            await ShutdownRunAsync();
        }
    }

    private void BeginRun(string status)
    {
        _values.Reset(_project);
        if (_runOverrides is not null)
        {
            foreach (var pair in _runOverrides)
                _values.Set(pair.Key, pair.Value);
        }
        _runOverrides = null;
        _cts = new CancellationTokenSource();
        _paused = false;
        lock (_parallelLock)
        {
            _parallelSequences.Clear();
            _activeSequences.Clear();
            _parallelFailure = null;
        }
        lock (_heldInputLock)
        {
            _heldKeys.Clear();
            _leftMouseHeld = false;
            _rightMouseHeld = false;
        }
        lock (_cooldownLock)
            _conditionLastTriggered.Clear();
        IsRunning = true;
        PlaybackSpeedPercent = Math.Clamp(PlaybackSpeedPercent, 10, 400);
        StateChanged?.Invoke();
        StatusChanged?.Invoke(status);
    }

    private async Task ShutdownRunAsync()
    {
        try
        {
            _cts?.Cancel();
            await DrainParallelSequencesAsync();
        }
        finally
        {
            await ReleaseHeldInputsAsync();
            EndRun();
        }
    }

    private void EndRun()
    {
        IsRunning = false;
        _paused = false;
        _cts?.Dispose();
        _cts = null;
        lock (_parallelLock)
        {
            _parallelSequences.Clear();
            _activeSequences.Clear();
            _parallelFailure = null;
        }
        StateChanged?.Invoke();
    }

    public void Stop() => _cts?.Cancel();

    public void TogglePause()
    {
        if (!IsRunning)
            return;

        _paused = !_paused;
        StatusChanged?.Invoke(_paused ? "Paused" : "Resumed");
        StateChanged?.Invoke();
    }

    private async Task RunSequenceAsync(string sequenceNameRaw, CancellationToken token, int depth)
    {
        if (depth > 32)
            throw new InvalidOperationException("Sequence call depth exceeded 32. Check for accidental sequence recursion.");

        var sequenceName = (sequenceNameRaw ?? string.Empty).Trim();
        var sequence = _project.Sequences.FirstOrDefault(s =>
            s.Name.Equals(sequenceName, StringComparison.OrdinalIgnoreCase));

        if (sequence is null)
            throw new InvalidOperationException($"Sequence '{sequenceName}' does not exist.");

        if (!sequence.Enabled && !sequence.Name.Equals("Starting Sequence", StringComparison.OrdinalIgnoreCase))
        {
            StatusChanged?.Invoke($"Skipped disabled tab: {sequence.Name}");
            return;
        }

        lock (_parallelLock)
        {
            if (!_activeSequences.Add(sequence.Name))
                throw new InvalidOperationException($"Sequence '{sequence.Name}' is already running. A tab cannot start another copy of itself while it is active.");
        }

        try
        {
            while (true)
            {
                var signal = await ExecuteCommandsAsync(sequence.Name, sequence.Commands, token, depth);
                if (signal != ExecutionSignal.Restart)
                    break;

                // Restart only this tab invocation. Other simultaneous tabs keep
                // running and all tabs continue sharing the same variables,
                // Pause state, Stop token, and macro session.
                await YieldLoopAsync(0, token);
            }
        }
        finally
        {
            lock (_parallelLock)
                _activeSequences.Remove(sequence.Name);
        }
    }

    private async Task<ExecutionSignal> ExecuteCommandsAsync(
        string sequenceName,
        IReadOnlyList<MacroCommand> commands,
        CancellationToken token,
        int depth)
    {
        foreach (var sourceCommand in commands)
        {
            token.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(token);
            if (!sourceCommand.Enabled)
                continue;

            var command = _values.ResolveNumericExpressions(sourceCommand);
            CommandStarted?.Invoke(sequenceName, command.Id);

            switch (command.Type)
            {
                case CommandType.Comment:
                    break;

                case CommandType.MoveMouse:
                {
                    await WithInputGateAsync(async () =>
                    {
                        var point = CoordinateResolver.ResolvePoint(command, _values);
                        await InputController.MoveMouseAsync(point.X, point.Y, EffectiveMoveDuration(command), token);
                    }, token);
                    break;
                }

                case CommandType.Click:
                {
                    await WithInputGateAsync(async () =>
                    {
                        if (ShouldMoveBeforeClick(command))
                            await MoveForClickAsync(command, token);
                        InputController.LeftClick();
                    }, token);
                    break;
                }

                case CommandType.DoubleClick:
                {
                    await WithInputGateAsync(async () =>
                    {
                        if (ShouldMoveBeforeClick(command))
                            await MoveForClickAsync(command, token);
                        InputController.LeftClick();
                        await DelayPausableAsync(Math.Clamp(command.ClickDelayMs, 20, 1000), token);
                        InputController.LeftClick();
                    }, token);
                    break;
                }

                case CommandType.RightClick:
                    await WithInputGateAsync(async () =>
                    {
                        if (ShouldMoveBeforeClick(command))
                            await MoveForClickAsync(command, token);
                        InputController.RightClick();
                    }, token);
                    break;

                case CommandType.Scroll:
                    await WithInputGateAsync(async () =>
                    {
                        await MoveForClickAsync(command, token);
                        InputController.Scroll(command.ScrollAmount);
                    }, token);
                    break;

                case CommandType.DragMouse:
                {
                    await WithInputGateAsync(async () =>
                    {
                        var start = CoordinateResolver.ResolvePoint(command, _values);
                        var end = CoordinateResolver.ResolvePoint(command, _values, true);
                        await InputController.MoveMouseAsync(start.X, start.Y, EffectiveMoveDuration(command), token);
                        InputController.LeftDown();
                        try
                        {
                            await InputController.MoveMouseAsync(end.X, end.Y, ScaleDuration(Math.Clamp(command.DragDurationMs, 1, 60000)), token);
                        }
                        finally
                        {
                            InputController.LeftUp();
                        }
                    }, token);
                    break;
                }

                case CommandType.LeftMouseDown:
                    await WithInputGateAsync(() =>
                    {
                        InputController.LeftDown();
                        lock (_heldInputLock) _leftMouseHeld = true;
                    }, token);
                    break;
                case CommandType.LeftMouseUp:
                    await WithInputGateAsync(() =>
                    {
                        InputController.LeftUp();
                        lock (_heldInputLock) _leftMouseHeld = false;
                    }, token);
                    break;
                case CommandType.RightMouseDown:
                    await WithInputGateAsync(() =>
                    {
                        InputController.RightDown();
                        lock (_heldInputLock) _rightMouseHeld = true;
                    }, token);
                    break;
                case CommandType.RightMouseUp:
                    await WithInputGateAsync(() =>
                    {
                        InputController.RightUp();
                        lock (_heldInputLock) _rightMouseHeld = false;
                    }, token);
                    break;

                case CommandType.PressKey:
                    await WithInputGateAsync(() => InputController.PressKey(_values.ResolveText(command.Key)), token);
                    break;
                case CommandType.KeyDown:
                {
                    var key = _values.ResolveText(command.Key);
                    if (!InputController.TryGetVirtualKey(key, out _))
                        throw new InvalidOperationException($"Unknown key: {key}");
                    await WithInputGateAsync(() =>
                    {
                        InputController.KeyDown(key);
                        lock (_heldInputLock) _heldKeys.Add(key);
                    }, token);
                    break;
                }
                case CommandType.KeyUp:
                {
                    var key = _values.ResolveText(command.Key);
                    if (!InputController.TryGetVirtualKey(key, out _))
                        throw new InvalidOperationException($"Unknown key: {key}");
                    await WithInputGateAsync(() =>
                    {
                        InputController.KeyUp(key);
                        lock (_heldInputLock) _heldKeys.Remove(key);
                    }, token);
                    break;
                }
                case CommandType.TypeText:
                    await WithInputGateAsync(() => InputController.TypeText(_values.ResolveText(command.Text)), token);
                    break;

                case CommandType.HoldKey:
                {
                    var key = _values.ResolveText(command.Key);
                    if (!InputController.TryGetVirtualKey(key, out _))
                        throw new InvalidOperationException($"Unknown key: {key}");
                    await WithInputGateAsync(async () =>
                    {
                        InputController.KeyDown(key);
                        try
                        {
                            await DelayPausableAsync(Math.Max(1, command.HoldMs), token);
                        }
                        finally
                        {
                            InputController.KeyUp(key);
                        }
                    }, token);
                    break;
                }

                case CommandType.RepeatKey:
                {
                    var count = ResolveRepeatCount(command);
                    var key = _values.ResolveText(command.Key);
                    for (var i = 0; i < count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        await WithInputGateAsync(() => InputController.PressKey(key), token);
                        if (i + 1 < count)
                            await DelayPausableAsync(Math.Max(0, command.WaitMs), token);
                    }
                    break;
                }

                case CommandType.WaitUntilKeyPressed:
                case CommandType.WaitUntilKeyReleased:
                {
                    var key = _values.ResolveText(command.Key);
                    if (!InputController.TryGetVirtualKey(key, out _))
                        throw new InvalidOperationException($"Unknown key: {key}");
                    var wantDown = command.Type == CommandType.WaitUntilKeyPressed;
                    await RunFailureAwareAsync(command, $"key {key}",
                        () => WaitUntilAsync(() => Task.FromResult(KeyIsDown(key) == wantDown), command,
                            wantDown ? $"key {key} to be pressed" : $"key {key} to be released", token), token, depth);
                    break;
                }

                case CommandType.IfKeyPressed:
                {
                    var key = _values.ResolveText(command.Key);
                    if (!InputController.TryGetVirtualKey(key, out _))
                        throw new InvalidOperationException($"Unknown key: {key}");
                    var matched = KeyIsDown(key);
                    if (matched && !TryConsumeConditionCooldown(command))
                        break;
                    var branch = matched ? command.Children : command.ElseChildren;
                    var signal = await ExecuteCommandsAsync(sequenceName, branch, token, depth);
                    if (signal != ExecutionSignal.None)
                        return signal;
                    break;
                }

                case CommandType.LoopWhileKeyPressed:
                {
                    var key = _values.ResolveText(command.Key);
                    if (!InputController.TryGetVirtualKey(key, out _))
                        throw new InvalidOperationException($"Unknown key: {key}");
                    while (KeyIsDown(key))
                    {
                        token.ThrowIfCancellationRequested();
                        await WaitWhilePausedAsync(token);
                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break) break;
                        if (signal != ExecutionSignal.None) return signal;
                        await YieldLoopAsync(Math.Max(0, command.PollMs), token);
                    }
                    break;
                }

                case CommandType.Wait:
                {
                    var wait = Math.Max(0, _values.ResolveInt(command.WaitExpression, command.WaitMs));
                    StatusChanged?.Invoke($"Waiting {wait} ms");
                    await DelayPausableAsync(wait, token);
                    break;
                }

                case CommandType.RandomWait:
                {
                    var min = Math.Max(0, Math.Min(command.MinWaitMs, command.MaxWaitMs));
                    var max = Math.Max(min, Math.Max(command.MinWaitMs, command.MaxWaitMs));
                    var delay = NextInclusive(min, max);
                    StatusChanged?.Invoke($"Waiting {delay} ms (random)");
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
                    var (matched, current, point, target) = await ColorConditionAsync(command, token);
                    StatusChanged?.Invoke($"Checking {target}  •  Current {current}");
                    if (matched && !TryConsumeConditionCooldown(command))
                        break;
                    var branch = matched ? command.Children : command.ElseChildren;
                    var signal = await ExecuteCommandsAsync(sequenceName, branch, token, depth);
                    if (signal != ExecutionSignal.None)
                        return signal;
                    break;
                }

                case CommandType.WaitUntilColor:
                {
                    await RunFailureAwareAsync(command, "color wait",
                        () => WaitUntilColorAsync(command, token), token, depth);
                    break;
                }

                case CommandType.LoopWhileColor:
                {
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        await WaitWhilePausedAsync(token);
                        var (matched, current, point, target) = await ColorConditionAsync(command, token);
                        StatusChanged?.Invoke($"Looping while {point.X}, {point.Y} matches condition {target}  •  Current {current}");
                        if (!matched)
                            break;
                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break) break;
                        if (signal != ExecutionSignal.None) return signal;
                        await YieldLoopAsync(Math.Max(0, command.PollMs), token);
                    }
                    break;
                }

                case CommandType.LoopUntilColor:
                {
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        await WaitWhilePausedAsync(token);
                        var (matched, current, point, target) = await ColorConditionAsync(command, token);
                        StatusChanged?.Invoke($"Waiting until {point.X}, {point.Y} matches condition {target}  •  Current {current}");
                        if (matched)
                            break;
                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break) break;
                        if (signal != ExecutionSignal.None) return signal;
                        await YieldLoopAsync(Math.Max(0, command.PollMs), token);
                    }
                    break;
                }

                case CommandType.ClickColor:
                case CommandType.FindColorToVariables:
                {
                    var search = ResolveColorSearch(command);
                    var point = await RunFindColorWithPolicyAsync(command, search, token, depth);
                    if (point is null)
                        break;

                    StoreFoundPoint(point.Value.X, point.Value.Y, command, "LastColorX", "LastColorY");
                    if (command.Type == CommandType.ClickColor)
                    {
                        await WithInputGateAsync(async () =>
                        {
                            await InputController.MoveMouseAsync(point.Value.X, point.Value.Y, EffectiveMoveDuration(command), token);
                            InputController.LeftClick();
                        }, token);
                    }
                    break;
                }

                case CommandType.SampleColorToVariable:
                {
                    var point = CoordinateResolver.ResolvePoint(command, _values);
                    var color = ScreenTools.GetPixelHex(point.X, point.Y);
                    var variable = string.IsNullOrWhiteSpace(command.StoreTextVariable) ? "PixelColor" : command.StoreTextVariable;
                    _values.Set(variable, color);
                    StatusChanged?.Invoke($"{variable} = {color} at {point.X}, {point.Y}");
                    break;
                }

                case CommandType.IfImage:
                {
                    var resolved = CoordinateResolver.ResolveImageSearch(command, _values);
                    StatusChanged?.Invoke($"Checking image: {DescribeImageSource(resolved)}");
                    var match = await ImageMatcher.FindAsync(resolved, token, text => StatusChanged?.Invoke(text));
                    if (match.HasValue)
                        StoreImageMatch(match.Value);
                    var matched = match.HasValue;
                    if (matched && !TryConsumeConditionCooldown(command))
                        break;
                    var branch = matched ? command.Children : command.ElseChildren;
                    var signal = await ExecuteCommandsAsync(sequenceName, branch, token, depth);
                    if (signal != ExecutionSignal.None)
                        return signal;
                    break;
                }

                case CommandType.WaitUntilImage:
                    await RunFailureAwareAsync(command, "image wait", () => WaitUntilImageStateAsync(command, true, token), token, depth);
                    break;
                case CommandType.WaitUntilImageGone:
                    await RunFailureAwareAsync(command, "image disappear wait", () => WaitUntilImageStateAsync(command, false, token), token, depth);
                    break;

                case CommandType.ClickImage:
                case CommandType.DoubleClickImage:
                case CommandType.MoveToImage:
                case CommandType.FindImageToVariables:
                {
                    var match = await RunFindImageWithPolicyAsync(command, token, depth);
                    if (match is null)
                        break;

                    StoreImageMatch(match.Value);
                    StoreFoundPoint(match.Value.CenterX, match.Value.CenterY, command, "LastImageX", "LastImageY");
                    if (command.Type == CommandType.FindImageToVariables)
                        break;

                    var x = match.Value.CenterX + command.ImageOffsetX;
                    var y = match.Value.CenterY + command.ImageOffsetY;
                    await WithInputGateAsync(async () =>
                    {
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
                    }, token);
                    break;
                }

                case CommandType.LoopUntilImage:
                {
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        await WaitWhilePausedAsync(token);
                        var resolved = CoordinateResolver.ResolveImageSearch(command, _values);
                        StatusChanged?.Invoke($"Waiting for image: {DescribeImageSource(resolved)}");
                        var match = await ImageMatcher.FindAsync(resolved, token, text => StatusChanged?.Invoke(text));
                        if (match.HasValue)
                        {
                            StoreImageMatch(match.Value);
                            break;
                        }
                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break) break;
                        if (signal != ExecutionSignal.None) return signal;
                        await YieldLoopAsync(Math.Max(0, command.PollMs), token);
                    }
                    break;
                }

                case CommandType.LoopWhileImage:
                {
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        await WaitWhilePausedAsync(token);
                        var resolved = CoordinateResolver.ResolveImageSearch(command, _values);
                        StatusChanged?.Invoke($"Looping while image exists: {DescribeImageSource(resolved)}");
                        var match = await ImageMatcher.FindAsync(resolved, token, text => StatusChanged?.Invoke(text));
                        if (!match.HasValue)
                            break;
                        StoreImageMatch(match.Value);
                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break) break;
                        if (signal != ExecutionSignal.None) return signal;
                        await YieldLoopAsync(Math.Max(0, command.PollMs), token);
                    }
                    break;
                }

                case CommandType.IfWindow:
                {
                    var title = _values.ResolveText(command.WindowTitle);
                    var matched = WindowTools.Exists(title);
                    if (matched && !TryConsumeConditionCooldown(command))
                        break;
                    var branch = matched ? command.Children : command.ElseChildren;
                    var signal = await ExecuteCommandsAsync(sequenceName, branch, token, depth);
                    if (signal != ExecutionSignal.None)
                        return signal;
                    break;
                }

                case CommandType.FocusWindow:
                case CommandType.MinimizeWindow:
                case CommandType.MaximizeWindow:
                case CommandType.RestoreWindow:
                case CommandType.CloseWindow:
                {
                    var title = _values.ResolveText(command.WindowTitle);
                    if (string.IsNullOrWhiteSpace(title))
                        throw new InvalidOperationException("Enter part of the window title first.");
                    await RunFailureAwareAsync(command, $"window {title}", () => Task.FromResult(command.Type switch
                    {
                        CommandType.FocusWindow => WindowTools.Focus(title),
                        CommandType.MinimizeWindow => WindowTools.Minimize(title),
                        CommandType.MaximizeWindow => WindowTools.Maximize(title),
                        CommandType.RestoreWindow => WindowTools.Restore(title),
                        CommandType.CloseWindow => WindowTools.Close(title),
                        _ => false
                    }), token, depth);
                    break;
                }

                case CommandType.WaitForWindow:
                case CommandType.WaitForWindowGone:
                {
                    var title = _values.ResolveText(command.WindowTitle);
                    if (string.IsNullOrWhiteSpace(title))
                        throw new InvalidOperationException("Enter part of the window title first.");
                    var wantExists = command.Type == CommandType.WaitForWindow;
                    await RunFailureAwareAsync(command, $"window {title}",
                        () => WaitUntilAsync(() => Task.FromResult(WindowTools.Exists(title) == wantExists), command,
                            wantExists ? $"window {title}" : $"window {title} to close", token), token, depth);
                    break;
                }

                case CommandType.RunProgram:
                {
                    var value = _values.ResolveText(command.ProgramPath);
                    await RunFailureAwareAsync(command, $"open {value}", async () =>
                    {
                        try
                        {
                            WindowTools.Open(value, _values.ResolveText(command.ProgramArguments), _values.ResolveText(command.WorkingDirectory));
                            await Task.CompletedTask;
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    }, token, depth);
                    break;
                }

                case CommandType.SetVariable:
                    _values.Set(command.VariableName, _values.ResolveValue(command.VariableValue));
                    StatusChanged?.Invoke($"{RuntimeValues.NormalizeName(command.VariableName)} = {_values.Get(command.VariableName)}");
                    break;

                case CommandType.AddVariable:
                    _values.Add(command.VariableName, command.VariableValue);
                    StatusChanged?.Invoke($"{RuntimeValues.NormalizeName(command.VariableName)} = {_values.Get(command.VariableName)}");
                    break;

                case CommandType.RandomNumber:
                {
                    var min = _values.ResolveInt(command.VariableValue, 0);
                    var max = _values.ResolveInt(command.VariableValue2, 100);
                    if (max < min) (min, max) = (max, min);
                    var number = NextInclusive(min, max);
                    _values.Set(command.VariableName, number.ToString());
                    break;
                }

                case CommandType.IfVariable:
                {
                    var matched = _values.Compare(command.VariableName, command.VariableCompareMode, command.VariableValue);
                    if (matched && !TryConsumeConditionCooldown(command))
                        break;
                    var branch = matched ? command.Children : command.ElseChildren;
                    var signal = await ExecuteCommandsAsync(sequenceName, branch, token, depth);
                    if (signal != ExecutionSignal.None)
                        return signal;
                    break;
                }

                case CommandType.WaitUntilVariable:
                    await RunFailureAwareAsync(command, $"variable {command.VariableName}",
                        () => WaitUntilAsync(() => Task.FromResult(_values.Compare(command.VariableName, command.VariableCompareMode, command.VariableValue)),
                            command, $"variable {command.VariableName}", token), token, depth);
                    break;

                case CommandType.LoopWhileVariable:
                    while (_values.Compare(command.VariableName, command.VariableCompareMode, command.VariableValue))
                    {
                        token.ThrowIfCancellationRequested();
                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break) break;
                        if (signal != ExecutionSignal.None) return signal;
                        await YieldLoopAsync(Math.Max(0, command.PollMs), token);
                    }
                    break;

                case CommandType.LoopUntilVariable:
                    while (!_values.Compare(command.VariableName, command.VariableCompareMode, command.VariableValue))
                    {
                        token.ThrowIfCancellationRequested();
                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break) break;
                        if (signal != ExecutionSignal.None) return signal;
                        await YieldLoopAsync(Math.Max(0, command.PollMs), token);
                    }
                    break;

                case CommandType.SetClipboard:
                {
                    var text = _values.ResolveText(command.Text);
                    await Application.Current.Dispatcher.InvokeAsync(() => Clipboard.SetText(text));
                    break;
                }

                case CommandType.ClipboardToVariable:
                {
                    var text = await Application.Current.Dispatcher.InvokeAsync(() => Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty);
                    _values.Set(command.VariableName, text);
                    break;
                }

                case CommandType.ReadTextFile:
                {
                    string? result = null;
                    var ok = await RunFailureAwareAsync(command, "read text file", async () =>
                    {
                        try
                        {
                            var path = ResolveFilePath(command.FilePath);
                            result = await File.ReadAllTextAsync(path, token);
                            return true;
                        }
                        catch when (!token.IsCancellationRequested)
                        {
                            return false;
                        }
                    }, token, depth);
                    if (ok) _values.Set(command.VariableName, result ?? string.Empty);
                    break;
                }

                case CommandType.WriteTextFile:
                {
                    await RunFailureAwareAsync(command, "write text file", async () =>
                    {
                        try
                        {
                            var path = ResolveFilePath(command.FilePath);
                            var text = _values.ResolveText(command.Text);
                            var directory = Path.GetDirectoryName(path);
                            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                            if (command.AppendFile) await File.AppendAllTextAsync(path, text, token);
                            else await File.WriteAllTextAsync(path, text, token);
                            return true;
                        }
                        catch when (!token.IsCancellationRequested)
                        {
                            return false;
                        }
                    }, token, depth);
                    break;
                }

                case CommandType.PromptText:
                {
                    var prompt = _values.ResolveText(command.PromptText);
                    var initial = _values.ResolveText(command.VariableValue);
                    await _promptGate.WaitAsync(token);
                    try
                    {
                        var result = await RuntimePromptWindow.AskTextAsync(prompt, initial, token);
                        if (!result.Accepted)
                        {
                            _cts?.Cancel();
                            token.ThrowIfCancellationRequested();
                        }
                        _values.Set(command.VariableName, result.Value);
                    }
                    finally
                    {
                        _promptGate.Release();
                    }
                    break;
                }

                case CommandType.PromptSelect:
                {
                    var prompt = _values.ResolveText(command.PromptText);
                    // Dropdown entries are literal choices. The editor promises that
                    // the exact selected option text is saved, so an option that happens
                    // to match a variable name must not be substituted at runtime.
                    var options = (command.PromptOptions ?? new List<string>())
                        .Where(option => !string.IsNullOrWhiteSpace(option))
                        .ToList();
                    if (options.Count == 0)
                        throw new InvalidOperationException("Ask User from Dropdown needs at least one option.");

                    await _promptGate.WaitAsync(token);
                    try
                    {
                        var result = await RuntimePromptWindow.AskSelectAsync(prompt, options, token);
                        if (!result.Accepted)
                        {
                            _cts?.Cancel();
                            token.ThrowIfCancellationRequested();
                        }
                        _values.Set(command.VariableName, result.Value);
                    }
                    finally
                    {
                        _promptGate.Release();
                    }
                    break;
                }

                case CommandType.PromptYesNo:
                {
                    var prompt = _values.ResolveText(command.PromptText);
                    await _promptGate.WaitAsync(token);
                    try
                    {
                        var result = await RuntimePromptWindow.AskYesNoAsync(prompt, token);
                        if (!result.Accepted)
                        {
                            _cts?.Cancel();
                            token.ThrowIfCancellationRequested();
                        }
                        _values.Set(command.VariableName, result.Value ? "true" : "false");
                    }
                    finally
                    {
                        _promptGate.Release();
                    }
                    break;
                }

                case CommandType.Group:
                {
                    var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                    if (signal != ExecutionSignal.None)
                        return signal;
                    break;
                }

                case CommandType.RunSequence:
                    if (command.RunSequenceMode == SequenceRunMode.RunSimultaneously)
                    {
                        StartParallelSequence(command.TargetSequence, sequenceName, token, depth + 1);
                        // Let the new background sequence begin before a following tight loop
                        // in the current sequence can monopolize this execution context.
                        await YieldLoopAsync(0, token);
                    }
                    else
                    {
                        await RunSequenceAsync(command.TargetSequence, token, depth + 1);
                    }
                    break;

                case CommandType.LoopTimes:
                {
                    var count = ResolveRepeatCount(command);
                    for (var i = 0; i < count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        await WaitWhilePausedAsync(token);
                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break) break;
                        if (signal != ExecutionSignal.None) return signal;
                        if (i + 1 < count)
                            await YieldLoopAsync(0, token);
                    }
                    break;
                }

                case CommandType.LoopForever:
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        await WaitWhilePausedAsync(token);
                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break) break;
                        if (signal != ExecutionSignal.None) return signal;

                        // A 0 ms interval means "no intentional delay", not "never yield".
                        // Yielding here is important when Starting Sequence and one or more
                        // simultaneous tabs each contain their own Forever loops.
                        var interval = Math.Clamp(command.LoopIntervalMs, 0, 60000);
                        await YieldLoopAsync(interval, token);
                    }
                    break;

                case CommandType.Break:
                    return ExecutionSignal.Break;
                case CommandType.Return:
                    return ExecutionSignal.Return;
                case CommandType.RestartCurrentTab:
                    return ExecutionSignal.Restart;
                case CommandType.StopMacro:
                    _cts?.Cancel();
                    token.ThrowIfCancellationRequested();
                    break;
            }
        }

        return ExecutionSignal.None;
    }

    private Task<(bool Matched, string Current, (int X, int Y) Point, string Target)> ColorConditionAsync(MacroCommand command, CancellationToken token)
        => Task.Run(() => ColorCondition(command, token), token);

    private (bool Matched, string Current, (int X, int Y) Point, string Target) ColorCondition(MacroCommand command, CancellationToken token)
    {
        var targets = ResolveColorTargets(command);
        var regions = ResolveColorConditionRegions(command);
        var useAll = command.ColorLocationGroupMode == LocationGroupMode.All && regions.Count > 1;

        (int X, int Y, string Hex)? firstFound = null;
        var perRegion = new List<bool>(regions.Count);
        foreach (var item in regions)
        {
            token.ThrowIfCancellationRequested();
            var found = ScreenTools.FindColorInRegion(item.Region, targets, token);
            perRegion.Add(found.HasValue);
            if (firstFound is null && found.HasValue)
                firstFound = found;
        }

        var perRegionConditions = command.CompareMode == CompareMode.Equals
            ? perRegion
            : perRegion.Select(value => !value).ToList();
        var result = useAll
            ? perRegionConditions.All(value => value)
            : perRegionConditions.Any(value => value);

        var first = regions[0];
        var point = firstFound.HasValue
            ? (firstFound.Value.X, firstFound.Value.Y)
            : first.ReferencePoint;
        var current = firstFound.HasValue
            ? firstFound.Value.Hex
            : first.IsPixel ? ScreenTools.GetPixelHex(first.ReferencePoint.X, first.ReferencePoint.Y) : "No match";

        var colorText = string.Join(" OR ", targets.Select(target => $"{target.Hex} ±{target.Tolerance}"));
        var regionJoin = useAll ? " AND " : " OR ";
        var regionText = string.Join(regionJoin, regions.Select(item => item.Description));
        return (result, current, point, $"{colorText} {regionText}");
    }

    private List<(ScreenRegion Region, string Description, (int X, int Y) ReferencePoint, bool IsPixel)> ResolveColorConditionRegions(MacroCommand command)
    {
        var list = new List<(ScreenRegion, string, (int X, int Y), bool)>();

        // Backward compatibility for projects saved by the short-lived radius build.
        if (command.ColorSearchMode is null && command.ColorSearchRadius > 0)
        {
            var center = CoordinateResolver.ResolvePoint(command, _values);
            var radius = Math.Clamp(command.ColorSearchRadius, 0, 5000);
            list.Add((
                new ScreenRegion(center.X - radius, center.Y - radius, radius * 2 + 1, radius * 2 + 1),
                $"within {radius}px of {center.X}, {center.Y}",
                center,
                false));
            return list;
        }

        var mode = command.ColorSearchMode ?? ColorConditionSearchMode.Pixel;
        if (mode == ColorConditionSearchMode.FullScreen)
        {
            var full = ScreenTools.VirtualScreenRegion();
            list.Add((full, "on full screen", (full.X + full.Width / 2, full.Y + full.Height / 2), false));
            return list;
        }

        if (mode == ColorConditionSearchMode.SearchArea)
        {
            var resolved = CoordinateResolver.ResolveImageSearch(command, _values);
            var width = Math.Max(1, resolved.SearchWidth);
            var height = Math.Max(1, resolved.SearchHeight);
            var primary = new ScreenRegion(resolved.SearchX, resolved.SearchY, width, height);
            list.Add((primary, $"in area {primary.X}, {primary.Y}, {primary.Width}×{primary.Height}",
                (primary.X + primary.Width / 2, primary.Y + primary.Height / 2), false));

            foreach (var target in command.ColorSearchTargets ?? new List<ColorSearchTarget>())
            {
                var area = ResolveExtraSearchArea(target, command.CoordinateMode);
                list.Add((area, $"in area {area.X}, {area.Y}, {area.Width}×{area.Height}",
                    (area.X + area.Width / 2, area.Y + area.Height / 2), false));
            }
        }
        else
        {
            var primary = CoordinateResolver.ResolvePoint(command, _values);
            list.Add((new ScreenRegion(primary.X, primary.Y, 1, 1), $"at {primary.X}, {primary.Y}", primary, true));
            foreach (var target in command.ColorSearchTargets ?? new List<ColorSearchTarget>())
            {
                var point = ResolveExtraPoint(target, command.CoordinateMode);
                list.Add((new ScreenRegion(point.X, point.Y, 1, 1), $"at {point.X}, {point.Y}", point, true));
            }
        }

        return list;
    }

    private (int X, int Y) ResolveExtraPoint(ColorSearchTarget target, CoordinateMode mode)
    {
        var x = _values.ResolveInt(target.XExpression, target.X);
        var y = _values.ResolveInt(target.YExpression, target.Y);
        return ResolveCoordinatePair(x, y, mode);
    }

    private ScreenRegion ResolveExtraSearchArea(ColorSearchTarget target, CoordinateMode mode)
    {
        var x = _values.ResolveInt(target.SearchXExpression, target.SearchX);
        var y = _values.ResolveInt(target.SearchYExpression, target.SearchY);
        var width = Math.Max(1, _values.ResolveInt(target.SearchWidthExpression, target.SearchWidth));
        var height = Math.Max(1, _values.ResolveInt(target.SearchHeightExpression, target.SearchHeight));
        var point = ResolveCoordinatePair(x, y, mode);
        return new ScreenRegion(point.X, point.Y, width, height);
    }

    private static (int X, int Y) ResolveCoordinatePair(int x, int y, CoordinateMode mode)
    {
        return mode switch
        {
            CoordinateMode.ActiveWindow when WindowTools.TryGetForegroundRect(out var region) => (region.X + x, region.Y + y),
            CoordinateMode.RelativeToMouse when NativeMethods.GetCursorPos(out var mouse) => (mouse.X + x, mouse.Y + y),
            _ => (x, y)
        };
    }

    private List<(string Hex, int Tolerance)> ResolveColorTargets(MacroCommand command)
    {
        var targets = new List<(string Hex, int Tolerance)>();

        void AddTarget(string rawHex, int tolerance)
        {
            var hex = _values.ResolveText(rawHex).Trim();
            if (!ScreenTools.TryParseColor(hex, out _, out _, out _))
                throw new InvalidOperationException($"Color '{hex}' is not valid. Use a value like 0xFFFFFF or a variable containing a valid color.");
            targets.Add((hex, Math.Clamp(tolerance, 0, 255)));
        }

        AddTarget(command.ColorHex, command.ColorTolerance);
        foreach (var option in command.ColorAlternatives ?? new List<ColorMatchOption>())
        {
            var tolerance = _values.ResolveInt(option.ToleranceExpression, option.Tolerance);
            AddTarget(option.ColorHex, tolerance);
        }

        return targets;
    }

    private MacroCommand ResolveColorSearch(MacroCommand command)
    {
        var search = CoordinateResolver.ResolveImageSearch(command, _values);
        var targets = ResolveColorTargets(command);
        search.ColorHex = targets[0].Hex;
        search.ColorTolerance = targets[0].Tolerance;
        search.ColorAlternatives = targets.Skip(1)
            .Select(target => new ColorMatchOption
            {
                ColorHex = target.Hex,
                Tolerance = target.Tolerance,
                ToleranceExpression = string.Empty
            })
            .ToList();
        return search;
    }

    private bool TryConsumeConditionCooldown(MacroCommand command)
    {
        var cooldown = Math.Clamp(command.CooldownMs, 0, 86_400_000);
        if (cooldown <= 0)
            return true;

        var now = Environment.TickCount64;
        lock (_cooldownLock)
        {
            if (_conditionLastTriggered.TryGetValue(command.Id, out var last) && now - last < cooldown)
                return false;
            _conditionLastTriggered[command.Id] = now;
            return true;
        }
    }

    private static bool KeyIsDown(string key)
    {
        if (!InputController.TryGetVirtualKey(key, out var vk))
            return false;
        return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    private int EffectiveMoveDuration(MacroCommand command)
    {
        var mode = command.MouseMoveMode == MouseMoveMode.Legacy
            ? (command.MoveDurationMs > 0 ? MouseMoveMode.Smooth : MouseMoveMode.Teleport)
            : command.MouseMoveMode;
        return mode == MouseMoveMode.Smooth ? ScaleDuration(Math.Clamp(command.MoveDurationMs, 1, 60000)) : 0;
    }

    private int ScaleDuration(int milliseconds)
    {
        if (milliseconds <= 0)
            return 0;
        return Math.Max(1, (int)Math.Round(milliseconds * 100.0 / Math.Clamp(PlaybackSpeedPercent, 10, 400)));
    }

    private int ResolveRepeatCount(MacroCommand command) =>
        Math.Clamp(_values.ResolveInt(command.RepeatExpression, command.RepeatCount), 0, 1_000_000);

    private static bool ShouldMoveBeforeClick(MacroCommand command)
    {
        // null is the legacy value for old macros, which always moved to X/Y first.
        return command.MoveBeforeClick ?? true;
    }

    private async Task MoveForClickAsync(MacroCommand command, CancellationToken token)
    {
        var point = CoordinateResolver.ResolvePoint(command, _values);
        await InputController.MoveMouseAsync(point.X, point.Y, EffectiveMoveDuration(command), token);
    }

    private async Task<bool> WaitUntilAsync(
        Func<Task<bool>> predicate,
        MacroCommand command,
        string description,
        CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var timeout = Math.Max(0, command.TimeoutMs);
        var poll = Math.Clamp(command.PollMs, 10, 5000);
        StatusChanged?.Invoke($"Waiting for {description}");

        while (true)
        {
            token.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(token);
            if (await predicate())
                return true;
            if (timeout > 0 && stopwatch.ElapsedMilliseconds >= timeout)
            {
                StatusChanged?.Invoke($"Timed out waiting for {description}");
                return false;
            }
            await DelayPausableAsync(poll, token);
        }
    }

    private async Task<bool> WaitUntilColorAsync(MacroCommand command, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var timeout = Math.Max(0, command.TimeoutMs);
        var poll = Math.Clamp(command.PollMs, 10, 5000);
        var relation = command.CompareMode == CompareMode.Equals ? "is" : "is not";

        while (true)
        {
            token.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(token);
            var (matched, current, point, target) = await ColorConditionAsync(command, token);
            StatusChanged?.Invoke($"Waiting until {point.X}, {point.Y} {relation} {target}  •  Current {current}");
            if (matched)
                return true;
            if (timeout > 0 && stopwatch.ElapsedMilliseconds >= timeout)
            {
                StatusChanged?.Invoke($"Timed out waiting for {target} at {point.X}, {point.Y}");
                return false;
            }
            await DelayPausableAsync(poll, token);
        }
    }

    private async Task<bool> WaitUntilImageStateAsync(MacroCommand command, bool shouldExist, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var timeout = Math.Max(0, command.TimeoutMs);
        var poll = Math.Clamp(command.PollMs, 10, 5000);

        while (true)
        {
            token.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(token);
            var resolved = CoordinateResolver.ResolveImageSearch(command, _values);
            var source = DescribeImageSource(resolved);
            StatusChanged?.Invoke(shouldExist ? $"Waiting for image: {source}" : $"Waiting for image to disappear: {source}");
            var match = await ImageMatcher.FindAsync(resolved, token, text => StatusChanged?.Invoke(text));
            var found = match.HasValue;
            if (match.HasValue) StoreImageMatch(match.Value);
            if (found == shouldExist)
                return true;
            if (timeout > 0 && stopwatch.ElapsedMilliseconds >= timeout)
            {
                StatusChanged?.Invoke($"Timed out waiting for image: {source}");
                return false;
            }
            await DelayPausableAsync(poll, token);
        }
    }

    private async Task<ImageMatch?> RunFindImageWithPolicyAsync(MacroCommand command, CancellationToken token, int depth)
    {
        ImageMatch? found = null;
        await RunFailureAwareAsync(command, "image search", async () =>
        {
            var resolved = CoordinateResolver.ResolveImageSearch(command, _values);
            StatusChanged?.Invoke($"Looking for image: {DescribeImageSource(resolved)}");
            found = await ImageMatcher.FindAsync(resolved, token, text => StatusChanged?.Invoke(text));
            return found.HasValue;
        }, token, depth);
        return found;
    }

    private static string DescribeColorTargets(MacroCommand command)
    {
        var items = new List<string> { $"{command.ColorHex} ±{command.ColorTolerance}" };
        items.AddRange((command.ColorAlternatives ?? new List<ColorMatchOption>())
            .Select(option => $"{option.ColorHex} ±{option.Tolerance}"));
        return string.Join(" OR ", items);
    }

    private async Task<(int X, int Y)?> RunFindColorWithPolicyAsync(MacroCommand command, MacroCommand resolved, CancellationToken token, int depth)
    {
        (int X, int Y)? point = null;
        await RunFailureAwareAsync(command, "color search", async () =>
        {
            StatusChanged?.Invoke($"Searching for {DescribeColorTargets(resolved)} in the selected area");
            point = await ColorFinder.FindAsync(resolved, token);
            return point.HasValue;
        }, token, depth);
        return point;
    }

    private async Task<bool> RunFailureAwareAsync(
        MacroCommand command,
        string description,
        Func<Task<bool>> attempt,
        CancellationToken token,
        int depth)
    {
        var attempts = command.FailureAction == FailureAction.Retry
            ? Math.Clamp(command.FailureRetryCount, 0, 100) + 1
            : 1;

        for (var i = 0; i < attempts; i++)
        {
            token.ThrowIfCancellationRequested();
            if (await attempt())
                return true;
            if (i + 1 < attempts)
            {
                StatusChanged?.Invoke($"Retry {i + 1}/{attempts - 1}: {description}");
                await DelayPausableAsync(Math.Clamp(command.FailureRetryDelayMs, 0, 60000), token);
            }
        }

        StatusChanged?.Invoke($"Failed: {description}");
        switch (command.FailureAction)
        {
            case FailureAction.StopMacro:
                throw new InvalidOperationException($"Command failed: {description}");
            case FailureAction.RunSequence:
                if (!string.IsNullOrWhiteSpace(command.FailureSequence))
                    await RunSequenceAsync(command.FailureSequence, token, depth + 1);
                break;
        }
        return false;
    }

    private void StoreFoundPoint(int x, int y, MacroCommand command, string fallbackX, string fallbackY)
    {
        var xName = string.IsNullOrWhiteSpace(command.StoreXVariable) ? fallbackX : command.StoreXVariable;
        var yName = string.IsNullOrWhiteSpace(command.StoreYVariable) ? fallbackY : command.StoreYVariable;
        _values.Set(xName, x.ToString());
        _values.Set(yName, y.ToString());
        _values.Set(fallbackX, x.ToString());
        _values.Set(fallbackY, y.ToString());
    }

    private void StoreImageMatch(ImageMatch match)
    {
        _values.Set("LastImageX", match.CenterX.ToString());
        _values.Set("LastImageY", match.CenterY.ToString());
        _values.Set("LastImageName", Path.GetFileName(match.SourcePath));
        _values.Set("LastImagePath", match.SourcePath);
    }

    private string ResolveFilePath(string raw)
    {
        var expanded = _values.ResolveText(raw).Trim();
        if (string.IsNullOrWhiteSpace(expanded))
            throw new InvalidOperationException("Choose a file path first.");
        return ProjectPaths.Resolve(expanded);
    }

    private static string DescribeImageSource(MacroCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.ImageFolder)) return command.ImageFolder.Replace('\\', '/');
        if (!string.IsNullOrWhiteSpace(command.ImagePath)) return command.ImagePath.Replace('\\', '/');
        return "image";
    }

    private async Task WithInputGateAsync(Func<Task> action, CancellationToken token)
    {
        await _inputGate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            await action();
        }
        finally
        {
            _inputGate.Release();
        }
    }

    private async Task WithInputGateAsync(Action action, CancellationToken token)
    {
        await _inputGate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            action();
        }
        finally
        {
            _inputGate.Release();
        }
    }

    private async Task ReleaseHeldInputsAsync()
    {
        await _inputGate.WaitAsync();
        try
        {
            List<string> keys;
            bool left;
            bool right;
            lock (_heldInputLock)
            {
                keys = _heldKeys.ToList();
                _heldKeys.Clear();
                left = _leftMouseHeld;
                right = _rightMouseHeld;
                _leftMouseHeld = false;
                _rightMouseHeld = false;
            }

            foreach (var key in keys)
            {
                try { InputController.KeyUp(key); } catch { }
            }
            if (left)
            {
                try { InputController.LeftUp(); } catch { }
            }
            if (right)
            {
                try { InputController.RightUp(); } catch { }
            }
        }
        finally
        {
            _inputGate.Release();
        }
    }

    private async Task DrainParallelSequencesAsync()
    {
        while (true)
        {
            Task[] tasks;
            lock (_parallelLock)
            {
                foreach (var completed in _parallelSequences
                             .Where(pair => pair.Value.IsCompleted)
                             .Select(pair => pair.Key)
                             .ToList())
                    _parallelSequences.Remove(completed);

                if (_parallelSequences.Count == 0)
                    return;

                tasks = _parallelSequences.Values.ToArray();
            }

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
                // Individual background errors are captured in _parallelFailure.
                // Shutdown still waits for every task so no tab survives the run.
            }
        }
    }

    private static int NextInclusive(int min, int max)
    {
        if (max <= min)
            return min;
        return (int)Random.Shared.NextInt64(min, (long)max + 1L);
    }

    private void StartParallelSequence(string sequenceNameRaw, string callerSequenceName, CancellationToken token, int depth)
    {
        var sequenceName = (sequenceNameRaw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sequenceName))
            throw new InvalidOperationException("Choose a sequence to run.");

        lock (_parallelLock)
        {
            if (sequenceName.Equals(callerSequenceName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Sequence '{sequenceName}' cannot launch itself.");

            // Repeated simultaneous launches from a fast/Forever loop are a no-op
            // while that target tab is already active. This prevents duplicate clicks
            // without turning a harmless repeated launch into a macro-stopping error.
            if (_activeSequences.Contains(sequenceName))
                return;

            foreach (var completed in _parallelSequences
                         .Where(pair => pair.Value.IsCompleted)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                _parallelSequences.Remove(completed);
            }

            // Only one simultaneous instance of a tab may be active.
            // The old guard was keyed by the Run Sequence command ID, so two
            // different commands could start the same target tab at once and
            // duplicate actions such as Click.
            if (_parallelSequences.TryGetValue(sequenceName, out var existing) &&
                !existing.IsCompleted)
            {
                return;
            }

            if (_parallelSequences.Count >= 64)
                throw new InvalidOperationException(
                    "Too many tabs are running simultaneously. Stop the macro or reduce parallel Run Sequence commands.");

            var task = Task.Run(async () =>
            {
                try
                {
                    await RunSequenceAsync(sequenceName, token, depth).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Normal Stop or cancellation caused by another parallel failure.
                }
                catch (Exception ex)
                {
                    lock (_parallelLock)
                    {
                        _parallelFailure ??= ex;
                    }
                    _cts?.Cancel();
                }
            }, CancellationToken.None);

            _parallelSequences[sequenceName] = task;
        }
    }

    private async Task WaitForParallelSequencesAsync(CancellationToken token)
    {
        while (true)
        {
            Task[] tasks;
            lock (_parallelLock)
            {
                foreach (var completed in _parallelSequences.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToList())
                    _parallelSequences.Remove(completed);

                if (_parallelSequences.Count == 0)
                    break;

                tasks = _parallelSequences.Values.ToArray();
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            var failure = GetParallelFailure();
            if (failure is not null)
                throw new InvalidOperationException($"A simultaneously running tab failed: {failure.Message}", failure);

            token.ThrowIfCancellationRequested();
        }

        var finalFailure = GetParallelFailure();
        if (finalFailure is not null)
            throw new InvalidOperationException($"A simultaneously running tab failed: {finalFailure.Message}", finalFailure);

        token.ThrowIfCancellationRequested();
    }

    private Exception? GetParallelFailure()
    {
        lock (_parallelLock)
            return _parallelFailure;
    }

    private async Task YieldLoopAsync(int milliseconds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await WaitWhilePausedAsync(token);

        if (milliseconds > 0)
        {
            await DelayPausableAsync(milliseconds, token);
            return;
        }

        // Task.Yield gives other simultaneous sequences (and the UI thread for the
        // Starting Sequence) a chance to run without adding a user-visible delay.
        await Task.Yield();
        token.ThrowIfCancellationRequested();
        await WaitWhilePausedAsync(token);
    }

    private async Task DelayPausableAsync(int milliseconds, CancellationToken token)
    {
        var remaining = ScaleDuration(milliseconds);
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
        Return,
        Restart
    }
}
