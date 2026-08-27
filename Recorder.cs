using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace MacroMaker;

/// <summary>
/// Records global keyboard/mouse input using Win32 state polling instead of low-level hooks.
/// This is intentionally less clever than a hook-based recorder, but much more robust:
/// no native callback ever calls back into managed/WPF code.
/// </summary>
public sealed class GlobalInputRecorder : IDisposable
{
    private readonly RecorderSettings _settings;
    private readonly List<MacroCommand> _commands = new();
    private readonly Stopwatch _clock = new();
    private readonly object _gate = new();
    private readonly bool[] _keyStates = new bool[256];
    private readonly CancellationTokenSource _cts = new();

    private ParsedHotkey _stopHotkey;
    private Task<List<MacroCommand>>? _recordingTask;
    private long _lastActionMs;
    private long _lastMouseSampleMs;
    private NativeMethods.POINT _lastMousePoint;
    private bool _hasMousePoint;
    private bool _leftDown;
    private bool _rightDown;
    private bool _stopped;

    public GlobalInputRecorder(RecorderSettings settings)
    {
        _settings = new RecorderSettings
        {
            StopHotkey = string.IsNullOrWhiteSpace(settings.StopHotkey) ? "F7" : settings.StopHotkey.Trim(),
            RecordMouseMovement = settings.RecordMouseMovement,
            MouseSampleMs = Math.Clamp(settings.MouseSampleMs, 15, 500)
        };
    }

    public event Action<int>? CommandCountChanged;

    public static bool IsValidHotkey(string expression) => TryParseHotkey(expression, out _);

    public Task<List<MacroCommand>> StartAsync()
    {
        if (_recordingTask is not null)
            throw new InvalidOperationException("This recorder is already running.");

        if (!TryParseHotkey(_settings.StopHotkey, out _stopHotkey))
            throw new InvalidOperationException($"'{_settings.StopHotkey}' is not a valid stop-recording hotkey.");

        // Snapshot the current state so keys/buttons already held when recording begins
        // do not create fake events.
        for (var vk = 0; vk < _keyStates.Length; vk++)
            _keyStates[vk] = IsKeyDown(vk);

        if (NativeMethods.GetCursorPos(out var point))
        {
            _lastMousePoint = point;
            _hasMousePoint = true;
        }

        _leftDown = _keyStates[0x01];
        _rightDown = _keyStates[0x02];
        _clock.Restart();
        _lastActionMs = 0;
        _lastMouseSampleMs = 0;

        _recordingTask = Task.Run(PollLoopAsync);
        return _recordingTask;
    }

    public void Stop()
    {
        _cts.Cancel();
    }

    private async Task<List<MacroCommand>> PollLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var now = _clock.ElapsedMilliseconds;

                PollMouse(now);
                if (PollKeyboard(now))
                    break;

                // 8 ms is responsive enough for normal macro recording while staying light.
                await Task.Delay(8, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal external stop/dispose path.
        }
        finally
        {
            _clock.Stop();
            FinishRecording();
        }

        lock (_gate)
            return _commands.ToList();
    }

    private void PollMouse(long now)
    {
        if (!NativeMethods.GetCursorPos(out var point))
            return;

        lock (_gate)
        {
            if (_settings.RecordMouseMovement)
                RecordMouseMoveLocked(point, now);
            else
            {
                _lastMousePoint = point;
                _hasMousePoint = true;
            }

            var left = IsKeyDown(0x01);
            var right = IsKeyDown(0x02);

            if (left != _leftDown)
            {
                EnsureMousePositionLocked(point, now);
                AppendTimedLocked(new MacroCommand
                {
                    Type = left ? CommandType.LeftMouseDown : CommandType.LeftMouseUp
                }, now);
                _leftDown = left;
            }

            if (right != _rightDown)
            {
                EnsureMousePositionLocked(point, now);
                AppendTimedLocked(new MacroCommand
                {
                    Type = right ? CommandType.RightMouseDown : CommandType.RightMouseUp
                }, now);
                _rightDown = right;
            }
        }

        _keyStates[0x01] = IsKeyDown(0x01);
        _keyStates[0x02] = IsKeyDown(0x02);
    }

    /// <summary>Returns true when the stop hotkey is pressed.</summary>
    private bool PollKeyboard(long now)
    {
        var ctrl = IsAnyDown(0x11, 0xA2, 0xA3);
        var shift = IsAnyDown(0x10, 0xA0, 0xA1);
        var alt = IsAnyDown(0x12, 0xA4, 0xA5);
        var stopKeyDown = IsKeyDown(_stopHotkey.Key);
        var stopKeyWasDown = _keyStates[_stopHotkey.Key];

        if (stopKeyDown && !stopKeyWasDown
            && ctrl == _stopHotkey.Ctrl
            && shift == _stopHotkey.Shift
            && alt == _stopHotkey.Alt)
        {
            // Do not record the stop key itself.
            return true;
        }

        // Mouse buttons are handled separately. Skip undefined/reserved low values too.
        for (var vk = 0x08; vk <= 0xFE; vk++)
        {
            if (vk == 0x0A || vk == 0x0B || vk == 0x0C || (vk >= 0xA0 && vk <= 0xA5))
                continue;

            var down = IsKeyDown(vk);
            var wasDown = _keyStates[vk];
            if (down == wasDown)
                continue;

            _keyStates[vk] = down;

            // The stop key is never emitted as a recorded action.
            if (vk == _stopHotkey.Key)
                continue;

            var keyName = VirtualKeyName((uint)vk);
            if (string.IsNullOrWhiteSpace(keyName))
                continue;

            lock (_gate)
            {
                AppendTimedLocked(new MacroCommand
                {
                    Type = down ? CommandType.KeyDown : CommandType.KeyUp,
                    Key = keyName
                }, now);
            }
        }

        return false;
    }

    private void RecordMouseMoveLocked(NativeMethods.POINT point, long now)
    {
        if (!_hasMousePoint)
        {
            _lastMousePoint = point;
            _hasMousePoint = true;
            _lastMouseSampleMs = now;
            return;
        }

        var elapsed = now - _lastMouseSampleMs;
        var dx = point.X - _lastMousePoint.X;
        var dy = point.Y - _lastMousePoint.Y;
        if (elapsed < _settings.MouseSampleMs || (dx * dx + dy * dy) < 16)
            return;

        var duration = (int)Math.Clamp(elapsed, 1, 5000);
        AppendWithoutExtraWaitLocked(new MacroCommand
        {
            Type = CommandType.MoveMouse,
            X = point.X,
            Y = point.Y,
            MouseMoveMode = MouseMoveMode.Smooth,
            MoveDurationMs = duration
        }, now);

        _lastMousePoint = point;
        _lastMouseSampleMs = now;
    }

    private void EnsureMousePositionLocked(NativeMethods.POINT point, long now)
    {
        if (_hasMousePoint && _lastMousePoint.X == point.X && _lastMousePoint.Y == point.Y)
            return;

        if (_settings.RecordMouseMovement && _hasMousePoint)
        {
            var duration = (int)Math.Clamp(now - _lastMouseSampleMs, 1, 5000);
            AppendWithoutExtraWaitLocked(new MacroCommand
            {
                Type = CommandType.MoveMouse,
                X = point.X,
                Y = point.Y,
                MouseMoveMode = MouseMoveMode.Smooth,
                MoveDurationMs = duration
            }, now);
        }
        else
        {
            AppendTimedLocked(new MacroCommand
            {
                Type = CommandType.MoveMouse,
                X = point.X,
                Y = point.Y,
                MouseMoveMode = MouseMoveMode.Teleport,
                MoveDurationMs = 0
            }, now);
        }

        _lastMousePoint = point;
        _hasMousePoint = true;
        _lastMouseSampleMs = now;
    }

    private void AppendTimedLocked(MacroCommand command, long now)
    {
        if (_commands.Count > 0)
        {
            var gap = (int)Math.Clamp(now - _lastActionMs, 0, 86_400_000);
            if (gap >= 20)
                _commands.Add(new MacroCommand { Type = CommandType.Wait, WaitMs = gap });
        }

        _commands.Add(command);
        _lastActionMs = now;
        RaiseCountChangedLocked();
    }

    private void AppendWithoutExtraWaitLocked(MacroCommand command, long now)
    {
        _commands.Add(command);
        _lastActionMs = now;
        RaiseCountChangedLocked();
    }

    private void RaiseCountChangedLocked()
    {
        try
        {
            CommandCountChanged?.Invoke(_commands.Count);
        }
        catch
        {
            // Status UI is optional; recording must continue even if a listener disappears.
        }
    }

    private void FinishRecording()
    {
        lock (_gate)
        {
            if (_stopped)
                return;
            _stopped = true;

            // Close any input state that was captured as held, so playback cannot get stuck.
            if (_leftDown)
            {
                _commands.Add(new MacroCommand { Type = CommandType.LeftMouseUp });
                _leftDown = false;
            }
            if (_rightDown)
            {
                _commands.Add(new MacroCommand { Type = CommandType.RightMouseUp });
                _rightDown = false;
            }

            for (var vk = 0x08; vk <= 0xFE; vk++)
            {
                if (!_keyStates[vk] || vk == _stopHotkey.Key)
                    continue;

                var keyName = VirtualKeyName((uint)vk);
                if (!string.IsNullOrWhiteSpace(keyName))
                    _commands.Add(new MacroCommand { Type = CommandType.KeyUp, Key = keyName });
            }
        }
    }

    private static bool IsKeyDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool IsAnyDown(params int[] keys)
    {
        foreach (var key in keys)
        {
            if (IsKeyDown(key))
                return true;
        }
        return false;
    }

    private static bool TryParseHotkey(string expression, out ParsedHotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var parts = expression.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        var ctrl = false;
        var shift = false;
        var alt = false;
        string? keyPart = null;

        foreach (var part in parts)
        {
            if (part.Equals("CTRL", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("CONTROL", StringComparison.OrdinalIgnoreCase))
                ctrl = true;
            else if (part.Equals("SHIFT", StringComparison.OrdinalIgnoreCase))
                shift = true;
            else if (part.Equals("ALT", StringComparison.OrdinalIgnoreCase))
                alt = true;
            else if (keyPart is null)
                keyPart = part;
            else
                return false;
        }

        if (keyPart is null || !InputController.TryGetVirtualKey(keyPart, out var vk))
            return false;

        hotkey = new ParsedHotkey(vk, ctrl, shift, alt);
        return true;
    }

    private static string VirtualKeyName(uint vk)
    {
        if (vk is >= 0x41 and <= 0x5A)
            return ((char)vk).ToString();
        if (vk is >= 0x30 and <= 0x39)
            return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87)
            return $"F{vk - 0x6F}";
        if (vk is >= 0x60 and <= 0x69)
            return $"Numpad{vk - 0x60}";

        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x10 or 0xA0 or 0xA1 => "Shift",
            0x11 or 0xA2 or 0xA3 => "Ctrl",
            0x12 or 0xA4 or 0xA5 => "Alt",
            0x14 => "CapsLock",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            0x5B => "LWin",
            0x5C => "RWin",
            0x6A => "Multiply",
            0x6B => "Add",
            0x6D => "Subtract",
            0x6E => "Decimal",
            0x6F => "Divide",
            0x90 => "NumLock",
            0x91 => "ScrollLock",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => string.Empty
        };
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private readonly record struct ParsedHotkey(ushort Key, bool Ctrl, bool Shift, bool Alt);
}

public sealed class RecordingHudWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private readonly TextBlock _countText;

    public RecordingHudWindow(string stopHotkey)
    {
        Title = "Recording";
        Width = 310;
        Height = 112;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        Left = SystemParameters.VirtualScreenLeft + 12;
        Top = SystemParameters.VirtualScreenTop + 12;
        Background = new SolidColorBrush(Color.FromRgb(19, 24, 32));
        Foreground = Brushes.White;

        var panel = new StackPanel { Margin = new Thickness(14, 11, 14, 11) };
        panel.Children.Add(new TextBlock
        {
            Text = "● RECORDING",
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 120, 120))
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"Press {stopHotkey} to stop",
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 211, 222))
        });
        _countText = new TextBlock
        {
            Text = "0 commands captured",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(151, 160, 176))
        };
        panel.Children.Add(_countText);
        Content = panel;

        SourceInitialized += (_, _) => MakeClickThrough();
    }

    public void SetCount(int count)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => SetCount(count));
            return;
        }

        _countText.Text = $"{count} commands captured";
    }

    private void MakeClickThrough()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, style | WsExTransparent | WsExNoActivate | WsExToolWindow);
        }
        catch
        {
            // HUD remains usable even if Windows refuses the optional click-through style.
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
