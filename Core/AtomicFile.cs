using System.Text;
using System.IO;

namespace MacroMaker;

internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents, bool keepBackup = true)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        var backupPath = path + ".bak";

        try
        {
            File.WriteAllText(tempPath, contents, new UTF8Encoding(false));
            Commit(tempPath, path, backupPath, keepBackup);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public static async Task WriteAllTextAsync(string path, string contents, bool keepBackup = true, CancellationToken token = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        var backupPath = path + ".bak";

        try
        {
            await File.WriteAllTextAsync(tempPath, contents, new UTF8Encoding(false), token);
            token.ThrowIfCancellationRequested();
            Commit(tempPath, path, backupPath, keepBackup);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static void Commit(string tempPath, string path, string backupPath, bool keepBackup)
    {
        if (!File.Exists(path))
        {
            File.Move(tempPath, path, overwrite: true);
            return;
        }

        try
        {
            File.Replace(tempPath, path, keepBackup ? backupPath : null, ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            FallbackReplace(tempPath, path, backupPath, keepBackup);
        }
        catch (IOException)
        {
            FallbackReplace(tempPath, path, backupPath, keepBackup);
        }
    }

    private static void FallbackReplace(string tempPath, string path, string backupPath, bool keepBackup)
    {
        if (keepBackup && File.Exists(path))
            File.Copy(path, backupPath, overwrite: true);
        File.Move(tempPath, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}
