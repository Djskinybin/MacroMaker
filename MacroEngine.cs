using System.Diagnostics;
using System.IO;
using System.Windows;

namespace MacroMaker;

public sealed class MacroEngine
{
    private readonly MacroProject _project;
    private readonly Random _random = new();
    private readonly RuntimeValues _values;
    private IReadOnlyDictionary<string, string>? _runOverrides;
    private CancellationTokenSource? _cts;
    private bool _paused;

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
            EndRun();
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
            if (!_cts.IsCancellationRequested)
                StatusChanged?.Invoke(finishedText);
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
            EndRun();
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
        IsRunning = true;
        PlaybackSpeedPercent = Math.Clamp(PlaybackSpeedPercent, 10, 400);
        StateChanged?.Invoke();
        StatusChanged?.Invoke(status);
    }

    private void EndRun()
    {
        IsRunning = false;
        _paused = false;
        _cts?.Dispose();
        _cts = null;
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

        var sequenceName = _values.ResolveText(sequenceNameRaw);
        var sequence = _project.Sequences.FirstOrDefault(s =>
            s.Name.Equals(sequenceName, StringComparison.OrdinalIgnoreCase));

        if (sequence is null)
            throw new InvalidOperationException($"Sequence '{sequenceName}' does not exist.");

        var signal = await ExecuteCommandsAsync(sequence.Name, sequence.Commands, token, depth);
        _ = signal;
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
            if (!command.Enabled)
                continue;

            CommandStarted?.Invoke(sequenceName, command.Id);

            switch (command.Type)
            {
                case CommandType.Comment:
                    break;

                case CommandType.MoveMouse:
                {
                    var point = CoordinateResolver.ResolvePoint(command, _values);
                    await InputController.MoveMouseAsync(point.X, point.Y, EffectiveMoveDuration(command), token);
                    break;
                }

                case CommandType.Click:
                {
                    await MoveForClickAsync(command, token);
                    InputController.LeftClick();
                    break;
                }

                case CommandType.DoubleClick:
                {
                    await MoveForClickAsync(command, token);
                    InputController.LeftClick();
                    await DelayPausableAsync(Math.Clamp(command.ClickDelayMs, 20, 1000), token);
                    InputController.LeftClick();
                    break;
                }

                case CommandType.RightClick:
                    await MoveForClickAsync(command, token);
                    InputController.RightClick();
                    break;

                case CommandType.Scroll:
                    await MoveForClickAsync(command, token);
                    InputController.Scroll(command.ScrollAmount);
                    break;

                case CommandType.DragMouse:
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
                    break;
                }

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
                    InputController.PressKey(_values.ResolveText(command.Key));
                    break;
                case CommandType.KeyDown:
                    InputController.KeyDown(_values.ResolveText(command.Key));
                    break;
                case CommandType.KeyUp:
                    InputController.KeyUp(_values.ResolveText(command.Key));
                    break;
                case CommandType.TypeText:
                    InputController.TypeText(_values.ResolveText(command.Text));
                    break;

                case CommandType.HoldKey:
                {
                    var key = _values.ResolveText(command.Key);
                    InputController.KeyDown(key);
                    try
                    {
                        await DelayPausableAsync(Math.Max(1, command.HoldMs), token);
                    }
                    finally
                    {
                        InputController.KeyUp(key);
                    }
                    break;
                }

                case CommandType.RepeatKey:
                {
                    var count = ResolveRepeatCount(command);
                    var key = _values.ResolveText(command.Key);
                    for (var i = 0; i < count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        InputController.PressKey(key);
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
                    var branch = KeyIsDown(key) ? command.Children : command.ElseChildren;
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
                        if (signal == ExecutionSignal.Return) return signal;
                        if (command.Children.Count == 0) await DelayPausableAsync(Math.Max(10, command.PollMs), token);
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
                    var delay = max == min ? min : _random.Next(min, max + 1);
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
                    var (matched, current, point, target) = ColorCondition(command);
                    StatusChanged?.Invoke($"Checking {target} at {point.X}, {point.Y}  •  Current {current}");
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
                        var (matched, current, point, target) = ColorCondition(command);
                        StatusChanged?.Invoke($"Looping while {point.X}, {point.Y} matches condition {target}  •  Current {current}");
                        if (!matched)
                            break;
                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break) break;
                        if (signal == ExecutionSignal.Return) return signal;
                        if (command.Children.Count == 0) await DelayPausableAsync(Math.Max(10, command.PollMs), token);
                    }
                    break;
                }

                case CommandType.LoopUntilColor:
                {
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        await WaitWhilePausedAsync(token);
                        var (matched, current, point, target) = ColorCondition(command);
                        StatusChanged?.Invoke($"Waiting until {point.X}, {point.Y} matches condition {target}  •  Current {current}");
                        if (matched)
                            break;
                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break) break;
                        if (signal == ExecutionSignal.Return) return signal;
                        if (command.Children.Count == 0) await DelayPausableAsync(Math.Max(10, command.PollMs), token);
                    }
                    break;
                }

                case CommandType.ClickColor:
                case CommandType.FindColorToVariables:
                {
                    var search = CoordinateResolver.ResolveImageSearch(command, _values);
                    search.ColorHex = _values.ResolveText(command.ColorHex);
                    var point = await RunFindColorWithPolicyAsync(command, search, token, depth);
                    if (point is null)
                        break;

                    StoreFoundPoint(point.Value.X, point.Value.Y, command, "LastColorX", "LastColorY");
                    if (command.Type == CommandType.ClickColor)
                    {
                        await InputController.MoveMouseAsync(point.Value.X, point.Value.Y, EffectiveMoveDuration(command), token);
                        InputController.LeftClick();
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
                    var branch = match.HasValue ? command.Children : command.ElseChildren;
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
                        if (signal == ExecutionSignal.Return) return signal;
                        if (command.Children.Count == 0) await DelayPausableAsync(Math.Max(10, command.PollMs), token);
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
                        if (signal == ExecutionSignal.Return) return signal;
                        if (command.Children.Count == 0) await DelayPausableAsync(Math.Max(10, command.PollMs), token);
                    }
                    break;
                }

                case CommandType.IfWindow:
                {
                    var title = _values.ResolveText(command.WindowTitle);
                    var branch = WindowTools.Exists(title) ? command.Children : command.ElseChildren;
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
                    _values.Set(command.VariableName, _values.ResolveText(command.VariableValue));
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
                    var number = min == max ? min : _random.Next(min, max + 1);
                    _values.Set(command.VariableName, number.ToString());
                    break;
                }

                case CommandType.IfVariable:
                {
                    var branch = _values.Compare(command.VariableName, command.VariableCompareMode, command.VariableValue)
                        ? command.Children
                        : command.ElseChildren;
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
                        if (signal == ExecutionSignal.Return) return signal;
                        if (command.Children.Count == 0) await DelayPausableAsync(Math.Max(10, command.PollMs), token);
                    }
                    break;

                case CommandType.LoopUntilVariable:
                    while (!_values.Compare(command.VariableName, command.VariableCompareMode, command.VariableValue))
                    {
                        token.ThrowIfCancellationRequested();
                        var signal = await ExecuteCommandsAsync(sequenceName, command.Children, token, depth);
                        if (signal == ExecutionSignal.Break) break;
                        if (signal == ExecutionSignal.Return) return signal;
                        if (command.Children.Count == 0) await DelayPausableAsync(Math.Max(10, command.PollMs), token);
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
                    var result = await RuntimePromptWindow.AskTextAsync(prompt, initial);
                    if (result.Accepted)
                        _values.Set(command.VariableName, result.Value);
                    break;
                }

                case CommandType.PromptYesNo:
                {
                    var prompt = _values.ResolveText(command.PromptText);
                    var yes = await RuntimePromptWindow.AskYesNoAsync(prompt);
                    _values.Set(command.VariableName, yes ? "true" : "false");
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
                    await RunSequenceAsync(command.TargetSequence, token, depth + 1);
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
                        if (signal == ExecutionSignal.Return) return signal;
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
                        if (signal == ExecutionSignal.Return) return signal;
                        if (command.Children.Count == 0) await DelayPausableAsync(10, token);
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

    private (bool Matched, string Current, (int X, int Y) Point, string Target) ColorCondition(MacroCommand command)
    {
        var point = CoordinateResolver.ResolvePoint(command, _values);
        var target = _values.ResolveText(command.ColorHex);
        var current = ScreenTools.GetPixelHex(point.X, point.Y);
        var matches = ScreenTools.ColorMatches(point.X, point.Y, target, command.ColorTolerance);
        var result = command.CompareMode == CompareMode.Equals ? matches : !matches;
        return (result, current, point, target);
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
            var (matched, current, point, target) = ColorCondition(command);
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

    private async Task<(int X, int Y)?> RunFindColorWithPolicyAsync(MacroCommand command, MacroCommand resolved, CancellationToken token, int depth)
    {
        (int X, int Y)? point = null;
        await RunFailureAwareAsync(command, "color search", async () =>
        {
            StatusChanged?.Invoke($"Searching for {resolved.ColorHex} in the selected area");
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
        Return
    }
}
