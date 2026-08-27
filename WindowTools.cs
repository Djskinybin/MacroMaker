using System.Diagnostics;

namespace MacroMaker;

internal static class WindowTools
{
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
        // Focus should not resize, restore, minimize, or maximize it.
        return NativeMethods.SetForegroundWindow(handle);
    }

    public static void Open(string pathOrUrl)
    {
        var value = (pathOrUrl ?? string.Empty).Trim();
        if (value.Length == 0)
            throw new InvalidOperationException("Choose a program, file, folder, or URL first.");

        Process.Start(new ProcessStartInfo
        {
            FileName = value,
            UseShellExecute = true
        });
    }
}
