using System.Diagnostics;

namespace MacroMaker;

internal static class WindowTools
{
    private const int SW_MINIMIZE = 6;
    private const int SW_MAXIMIZE = 3;
    private const int SW_RESTORE = 9;
    private const uint WM_CLOSE = 0x0010;

    public static IntPtr FindWindow(string titleContains)
    {
        var needle = (titleContains ?? string.Empty).Trim();
        if (needle.Length == 0)
            return IntPtr.Zero;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero &&
                    !string.IsNullOrWhiteSpace(process.MainWindowTitle) &&
                    process.MainWindowTitle.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return process.MainWindowHandle;
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return IntPtr.Zero;
    }

    public static bool Exists(string titleContains) => FindWindow(titleContains) != IntPtr.Zero;

    public static bool Focus(string titleContains)
    {
        var handle = FindWindow(titleContains);
        if (handle == IntPtr.Zero)
            return false;

        // Preserve the window's current state (normal/maximized/minimized).
        return NativeMethods.SetForegroundWindow(handle);
    }

    public static bool Minimize(string titleContains) => ChangeState(titleContains, SW_MINIMIZE);
    public static bool Maximize(string titleContains) => ChangeState(titleContains, SW_MAXIMIZE);
    public static bool Restore(string titleContains) => ChangeState(titleContains, SW_RESTORE);

    public static bool Close(string titleContains)
    {
        var handle = FindWindow(titleContains);
        return handle != IntPtr.Zero && NativeMethods.PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    private static bool ChangeState(string titleContains, int command)
    {
        var handle = FindWindow(titleContains);
        return handle != IntPtr.Zero && NativeMethods.ShowWindow(handle, command);
    }

    public static bool TryGetForegroundRect(out ScreenRegion region)
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (handle != IntPtr.Zero && NativeMethods.GetWindowRect(handle, out var rect))
        {
            region = new ScreenRegion(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
            return !region.IsEmpty;
        }

        region = default;
        return false;
    }

    public static void Open(string pathOrUrl, string? arguments = null, string? workingDirectory = null)
    {
        var value = (pathOrUrl ?? string.Empty).Trim();
        if (value.Length == 0)
            throw new InvalidOperationException("Choose a program, file, folder, or URL first.");

        var info = new ProcessStartInfo
        {
            FileName = value,
            UseShellExecute = true
        };
        if (!string.IsNullOrWhiteSpace(arguments))
            info.Arguments = arguments;
        if (!string.IsNullOrWhiteSpace(workingDirectory))
            info.WorkingDirectory = workingDirectory;
        Process.Start(info);
    }
}
