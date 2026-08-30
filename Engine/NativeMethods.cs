using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace MacroMaker;

internal static class NativeMethods
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MOUSEWHEEL = 0x020A;

    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint SRCCOPY = 0x00CC0020;

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);


    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, int dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern short VkKeyScan(char ch);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("winmm.dll")]
    public static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    public static extern uint timeEndPeriod(uint uPeriod);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
        IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookExW")]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookExW")]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}

internal static class InputController
{
    private static readonly Dictionary<string, ushort> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BACKSPACE"] = 0x08,
        ["TAB"] = 0x09,
        ["ENTER"] = 0x0D,
        ["RETURN"] = 0x0D,
        ["SHIFT"] = 0x10,
        ["CTRL"] = 0x11,
        ["CONTROL"] = 0x11,
        ["ALT"] = 0x12,
        ["PAUSE"] = 0x13,
        ["CAPSLOCK"] = 0x14,
        ["ESC"] = 0x1B,
        ["ESCAPE"] = 0x1B,
        ["SPACE"] = 0x20,
        ["PAGEUP"] = 0x21,
        ["PAGEDOWN"] = 0x22,
        ["END"] = 0x23,
        ["HOME"] = 0x24,
        ["LEFT"] = 0x25,
        ["UP"] = 0x26,
        ["RIGHT"] = 0x27,
        ["DOWN"] = 0x28,
        ["INSERT"] = 0x2D,
        ["DELETE"] = 0x2E,
        ["0"] = 0x30, ["1"] = 0x31, ["2"] = 0x32, ["3"] = 0x33, ["4"] = 0x34,
        ["5"] = 0x35, ["6"] = 0x36, ["7"] = 0x37, ["8"] = 0x38, ["9"] = 0x39,
        ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44, ["E"] = 0x45,
        ["F"] = 0x46, ["G"] = 0x47, ["H"] = 0x48, ["I"] = 0x49, ["J"] = 0x4A,
        ["K"] = 0x4B, ["L"] = 0x4C, ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F,
        ["P"] = 0x50, ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53, ["T"] = 0x54,
        ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58, ["Y"] = 0x59, ["Z"] = 0x5A,
        ["WIN"] = 0x5B,
        ["WINDOWS"] = 0x5B,
        ["LWIN"] = 0x5B,
        ["RWIN"] = 0x5C,
        ["NUMPAD0"] = 0x60, ["NUMPAD1"] = 0x61, ["NUMPAD2"] = 0x62, ["NUMPAD3"] = 0x63,
        ["NUMPAD4"] = 0x64, ["NUMPAD5"] = 0x65, ["NUMPAD6"] = 0x66, ["NUMPAD7"] = 0x67,
        ["NUMPAD8"] = 0x68, ["NUMPAD9"] = 0x69,
        ["MULTIPLY"] = 0x6A, ["ADD"] = 0x6B, ["SUBTRACT"] = 0x6D, ["DECIMAL"] = 0x6E, ["DIVIDE"] = 0x6F,
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
        ["NUMLOCK"] = 0x90,
        ["SCROLLLOCK"] = 0x91
    };

    public static readonly UIntPtr MacroMouseInputTag = new(0x4D4D4B52u);

    public static async Task MoveMouseAsync(int x, int y, int durationMs, CancellationToken token)
    {
        if (!NativeMethods.GetCursorPos(out var start))
        {
            SendAbsoluteMouseMove(x, y);
            return;
        }

        if (durationMs <= 0)
        {
            SendAbsoluteMouseMove(x, y);
            return;
        }

        var dx = x - start.X;
        var dy = y - start.Y;
        var distance = Math.Sqrt((double)dx * dx + (double)dy * dy);
        if (distance < 1)
            return;

        // Respect the duration the user selected. A 50 ms smooth move should take
        // about 50 ms even across a long distance. We still submit intermediate
        // absolute mouse input at high frequency so Windows sees a continuous glide.
        var smoothDurationMs = Math.Clamp(durationMs, 1, 60_000);

        await Task.Run(() =>
        {
            NativeMethods.timeBeginPeriod(1);
            try
            {
                var sw = Stopwatch.StartNew();
                var lastX = start.X;
                var lastY = start.Y;

                while (sw.ElapsedMilliseconds < smoothDurationMs)
                {
                    token.ThrowIfCancellationRequested();
                    var t = Math.Clamp(sw.Elapsed.TotalMilliseconds / smoothDurationMs, 0.0, 1.0);
                    // Smoothstep removes the harsh start/stop of a linear cursor path.
                    var eased = t * t * (3.0 - 2.0 * t);
                    var nx = (int)Math.Round(start.X + dx * eased);
                    var ny = (int)Math.Round(start.Y + dy * eased);

                    if (nx != lastX || ny != lastY)
                    {
                        SendAbsoluteMouseMove(nx, ny);
                        lastX = nx;
                        lastY = ny;
                    }

                    Thread.Sleep(1);
                }

                SendAbsoluteMouseMove(x, y);
            }
            finally
            {
                NativeMethods.timeEndPeriod(1);
            }
        }, token);
    }

    private static void SendAbsoluteMouseMove(int x, int y)
    {
        const int SM_XVIRTUALSCREEN = 76;
        const int SM_YVIRTUALSCREEN = 77;
        const int SM_CXVIRTUALSCREEN = 78;
        const int SM_CYVIRTUALSCREEN = 79;

        var left = NativeMethods.GetSystemMetrics(SM_XVIRTUALSCREEN);
        var top = NativeMethods.GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = Math.Max(2, NativeMethods.GetSystemMetrics(SM_CXVIRTUALSCREEN));
        var height = Math.Max(2, NativeMethods.GetSystemMetrics(SM_CYVIRTUALSCREEN));

        var normalizedX = (int)Math.Round((x - left) * 65535.0 / (width - 1));
        var normalizedY = (int)Math.Round((y - top) * 65535.0 / (height - 1));
        normalizedX = Math.Clamp(normalizedX, 0, 65535);
        normalizedY = Math.Clamp(normalizedY, 0, 65535);

        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = normalizedX,
                    dy = normalizedY,
                    dwFlags = NativeMethods.MOUSEEVENTF_MOVE
                              | NativeMethods.MOUSEEVENTF_ABSOLUTE
                              | NativeMethods.MOUSEEVENTF_VIRTUALDESK,
                    dwExtraInfo = MacroMouseInputTag
                }
            }
        };

        if (NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>()) == 0)
            NativeMethods.SetCursorPos(x, y);
    }

    public static void LeftDown() => NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);

    public static void LeftUp() => NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

    public static void RightDown() => NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);

    public static void RightUp() => NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);

    public static void LeftClick()
    {
        LeftDown();
        LeftUp();
    }

    public static void RightClick()
    {
        RightDown();
        RightUp();
    }

    public static void Scroll(int amount)
    {
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, amount, UIntPtr.Zero);
    }

    public static void PressKey(string keyExpression)
    {
        var parts = keyExpression.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return;

        var modifiers = new List<ushort>();
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!TryGetVirtualKey(parts[i], out var modifier))
                throw new InvalidOperationException($"Unknown key in combo: {parts[i]}");
            KeyDown(modifier);
            modifiers.Add(modifier);
        }

        try
        {
            // Punctuation such as !, @ and ? may require Shift/Alt/Ctrl according
            // to the active keyboard layout. VkKeyScan includes those modifier bits.
            var last = parts[^1];
            if (last.Length == 1 && !NamedKeys.ContainsKey(last))
            {
                PressCharacterWithLayoutModifiers(last[0]);
            }
            else if (TryGetVirtualKey(last, out var key))
            {
                KeyDown(key);
                KeyUp(key);
            }
            else if (last.Length == 1)
            {
                TypeText(last);
            }
            else
            {
                throw new InvalidOperationException($"Unknown key: {last}");
            }
        }
        finally
        {
            // Never leave an explicit Ctrl/Shift/Alt/Win modifier held if an
            // unusual keyboard-layout mapping or SendInput call fails midway.
            for (var i = modifiers.Count - 1; i >= 0; i--)
                KeyUp(modifiers[i]);
        }
    }

    private static void PressCharacterWithLayoutModifiers(char ch)
    {
        var scan = NativeMethods.VkKeyScan(ch);
        if (scan == -1)
        {
            TypeText(ch.ToString());
            return;
        }

        var key = (ushort)(scan & 0xFF);
        var flags = (scan >> 8) & 0xFF;
        var modifiers = new List<ushort>();
        if ((flags & 1) != 0) modifiers.Add(0x10); // Shift
        if ((flags & 2) != 0) modifiers.Add(0x11); // Ctrl
        if ((flags & 4) != 0) modifiers.Add(0x12); // Alt

        foreach (var modifier in modifiers) KeyDown(modifier);
        try
        {
            KeyDown(key);
            KeyUp(key);
        }
        finally
        {
            for (var i = modifiers.Count - 1; i >= 0; i--)
                KeyUp(modifiers[i]);
        }
    }

    public static void KeyDown(string key)
    {
        if (TryGetVirtualKey(key, out var vk))
            KeyDown(vk);
    }

    public static void KeyUp(string key)
    {
        if (TryGetVirtualKey(key, out var vk))
            KeyUp(vk);
    }

    public static void TypeText(string text)
    {
        foreach (var ch in text)
        {
            var down = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                U = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = ch,
                        dwFlags = NativeMethods.KEYEVENTF_UNICODE
                    }
                }
            };

            var up = down;
            up.U.ki.dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP;
            var inputs = new[] { down, up };
            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        }
    }

    public static bool TryGetVirtualKey(string value, out ushort vk)
    {
        var text = value.Trim();
        if (NamedKeys.TryGetValue(text, out vk))
            return true;

        if (text.Length == 1)
        {
            var result = NativeMethods.VkKeyScan(text[0]);
            if (result != -1)
            {
                vk = (ushort)(result & 0xFF);
                return true;
            }
        }

        vk = 0;
        return false;
    }

    private static void KeyDown(ushort vk)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT { wVk = vk }
            }
        };
        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void KeyUp(ushort vk)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT { wVk = vk, dwFlags = NativeMethods.KEYEVENTF_KEYUP }
            }
        };
        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }
}
