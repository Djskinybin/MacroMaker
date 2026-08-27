using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MacroMaker;

public partial class App : Application
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MacroMaker",
        "MacroMaker-crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog("AppDomain.CurrentDomain.UnhandledException", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashLog("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("DispatcherUnhandledException", e.Exception);

        MessageBox.Show(
            $"Macro Maker hit an unexpected error.\n\n{e.Exception.Message}\n\nA crash log was saved to:\n{CrashLogPath}",
            "Macro Maker Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Keep the editor alive for recoverable WPF/UI errors.
        e.Handled = true;
    }

    private static void WriteCrashLog(string source, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}" +
                $"{exception?.ToString() ?? "Unknown exception"}{Environment.NewLine}" +
                new string('-', 80) + Environment.NewLine);
        }
        catch
        {
            // Logging must never create another failure.
        }
    }
}
