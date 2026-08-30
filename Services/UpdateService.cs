using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MacroMaker;

internal sealed record GitHubRelease(
    string Tag,
    Version Version,
    string Notes,
    string ReleaseUrl,
    string InstallerUrl,
    long InstallerSize);

internal static class UpdateService
{
    private const string Owner = "Djskinybin";
    private const string Repository = "MacroMaker";
    private const string InstallerAssetName = "MacroMaker-Setup.exe";
    private static readonly Uri LatestReleaseApi = new($"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");

    private static readonly HttpClient Http = CreateHttpClient();

    public static Version CurrentVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? new Version(1, 0, 0) : new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        }
    }

    public static string CurrentVersionText => CurrentVersion.ToString(3);

    public static async Task CheckAndPromptAsync(Window owner, bool showUpToDate, CancellationToken cancellationToken = default)
    {
        GitHubRelease? release;
        try
        {
            release = await GetLatestReleaseAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            if (showUpToDate)
            {
                MessageBox.Show(owner,
                    $"MacroMaker couldn't check GitHub for updates.\n\n{ex.Message}",
                    "Update Check",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            return;
        }

        if (release.Version <= CurrentVersion)
        {
            if (showUpToDate)
            {
                MessageBox.Show(owner,
                    $"You're up to date.\n\nInstalled: {CurrentVersionText}\nLatest: {release.Version.ToString(3)}",
                    "MacroMaker Updates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            return;
        }

        var prompt = new UpdateAvailableWindow(release, CurrentVersion)
        {
            Owner = owner
        };

        if (prompt.ShowDialog() != true)
            return;

        if (string.IsNullOrWhiteSpace(release.InstallerUrl))
        {
            OpenUrl(release.ReleaseUrl);
            return;
        }

        var mainWindow = FindMainWindow(owner);
        var discardUnsavedChanges = false;
        if (mainWindow is not null && !mainWindow.TryPrepareForUpdate(owner, out discardUnsavedChanges))
            return;

        await DownloadAndLaunchInstallerAsync(owner, release, mainWindow, discardUnsavedChanges, cancellationToken);
    }

    private static MainWindow? FindMainWindow(Window owner)
    {
        Window? current = owner;
        while (current is not null)
        {
            if (current is MainWindow main)
                return main;
            current = current.Owner;
        }

        return Application.Current.MainWindow as MainWindow;
    }

    public static async Task<GitHubRelease> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(LatestReleaseApi, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagNode) ? tagNode.GetString() ?? string.Empty : string.Empty;
        if (!TryParseVersion(tag, out var version))
            throw new InvalidOperationException($"GitHub returned an invalid release tag: {tag}");

        var notes = root.TryGetProperty("body", out var bodyNode) ? bodyNode.GetString() ?? string.Empty : string.Empty;
        var releaseUrl = root.TryGetProperty("html_url", out var htmlNode) ? htmlNode.GetString() ?? string.Empty : string.Empty;
        string installerUrl = string.Empty;
        long installerSize = 0;

        if (root.TryGetProperty("assets", out var assetsNode) && assetsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsNode.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? string.Empty : string.Empty;
                if (!name.Equals(InstallerAssetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                installerUrl = asset.TryGetProperty("browser_download_url", out var urlNode)
                    ? urlNode.GetString() ?? string.Empty
                    : string.Empty;
                installerSize = asset.TryGetProperty("size", out var sizeNode) && sizeNode.TryGetInt64(out var size)
                    ? size
                    : 0;
                break;
            }
        }

        return new GitHubRelease(tag, version, notes, releaseUrl, installerUrl, installerSize);
    }

    private static async Task DownloadAndLaunchInstallerAsync(
        Window owner,
        GitHubRelease release,
        MainWindow? mainWindow,
        bool discardUnsavedChanges,
        CancellationToken cancellationToken)
    {
        var downloadWindow = new UpdateDownloadWindow(release.Version)
        {
            Owner = owner
        };
        downloadWindow.Show();
        var ownerWasEnabled = owner.IsEnabled;
        owner.IsEnabled = false;

        try
        {
            var updateFolder = Path.Combine(Path.GetTempPath(), "MacroMaker", "Updates", release.Tag.Trim());
            Directory.CreateDirectory(updateFolder);

            var installerPath = Path.Combine(updateFolder, InstallerAssetName);
            var partialPath = installerPath + ".download";

            TryDeleteFile(partialPath);
            TryDeleteFile(installerPath);

            long total;
            long downloaded = 0;

            // Keep every HTTP/file stream inside this scope. Nothing below this block
            // touches the installer until the download handle has been fully disposed.
            using (var response = await Http.GetAsync(
                       release.InstallerUrl,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                total = response.Content.Headers.ContentLength ?? release.InstallerSize;

                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var output = new FileStream(
                                 partialPath,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.Read,
                                 81920,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[81920];
                    while (true)
                    {
                        var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                        if (read <= 0)
                            break;

                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        downloaded += read;
                        downloadWindow.SetProgress(downloaded, total);
                    }

                    await output.FlushAsync(cancellationToken);
                }
            }

            if (!File.Exists(partialPath))
                throw new IOException("The downloaded installer file could not be found.");

            var downloadedSize = new FileInfo(partialPath).Length;
            if (downloadedSize <= 0)
                throw new IOException("The downloaded installer was empty.");

            if (release.InstallerSize > 0 && downloadedSize != release.InstallerSize)
            {
                throw new IOException(
                    $"The installer download was incomplete. Expected {release.InstallerSize:N0} bytes but received {downloadedSize:N0} bytes.");
            }

            File.Move(partialPath, installerPath, true);

            // Give Windows Defender / antivirus a short chance to finish scanning the
            // newly-created executable, then retry if Windows reports a temporary lock.
            downloadWindow.SetStatus("Starting installer…");
            await Task.Delay(250, cancellationToken);
            await StartInstallerWithRetryAsync(installerPath, cancellationToken);

            if (discardUnsavedChanges)
                mainWindow?.AllowUpdateShutdownWithoutSavePrompt();

            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            owner.IsEnabled = ownerWasEnabled;
            downloadWindow.Close();
        }
        catch (Exception ex)
        {
            owner.IsEnabled = ownerWasEnabled;
            downloadWindow.Close();
            MessageBox.Show(owner,
                $"The update couldn't be downloaded or started.\n\n{ex.Message}",
                "Update Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static async Task StartInstallerWithRetryAsync(string installerPath, CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(installerPath) ?? string.Empty
                });

                if (process is not null)
                    return;

                lastError = new InvalidOperationException("Windows did not start the installer process.");
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
            {
                lastError = ex;
            }

            await Task.Delay(300 * attempt, cancellationToken);
        }

        throw new InvalidOperationException(
            "MacroMaker downloaded the update, but Windows could not start the installer after several attempts.",
            lastError);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A stale file should not prevent us from trying the new download. If it is
            // truly locked, creating/replacing it later will surface the useful error.
        }
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        var cleaned = (tag ?? string.Empty).Trim();
        if (cleaned.StartsWith('v') || cleaned.StartsWith('V'))
            cleaned = cleaned[1..];

        var dash = cleaned.IndexOf('-');
        if (dash >= 0)
            cleaned = cleaned[..dash];

        if (Version.TryParse(cleaned, out var parsed) && parsed is not null)
        {
            version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MacroMaker-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}

internal sealed class UpdateAvailableWindow : Window
{
    public UpdateAvailableWindow(GitHubRelease release, Version currentVersion)
    {
        WindowTheme.Attach(this);
        Title = "MacroMaker Update";
        Width = 520;
        Height = 430;
        MinWidth = 480;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("BgBrush");
        Foreground = Brush("TextBrush");
        ResizeMode = ResizeMode.CanResize;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = "Update available",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush")
        };
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var versions = new TextBlock
        {
            Text = $"Installed {currentVersion.ToString(3)}  →  {release.Version.ToString(3)}",
            Margin = new Thickness(0, 6, 0, 14),
            Foreground = Brush("MutedTextBrush")
        };
        Grid.SetRow(versions, 1);
        root.Children.Add(versions);

        var notesBorder = new Border
        {
            Background = Brush("PanelBrush"),
            BorderBrush = Brush("BorderBrushDark"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14)
        };
        var notes = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(release.Notes) ? "A newer version of MacroMaker is available." : release.Notes.Trim(),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextBrush")
        };
        notesBorder.Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = notes
        };
        Grid.SetRow(notesBorder, 2);
        root.Children.Add(notesBorder);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var later = new Button
        {
            Content = "Later",
            Width = 100,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        later.Click += (_, _) => DialogResult = false;
        var update = new Button
        {
            Content = string.IsNullOrWhiteSpace(release.InstallerUrl) ? "Open Release" : "Update Now",
            Width = 120,
            Style = (Style)Application.Current.FindResource("AccentButtonStyle"),
            IsDefault = true
        };
        update.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(later);
        buttons.Children.Add(update);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Content = root;
    }

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}

internal sealed class UpdateDownloadWindow : Window
{
    private readonly ProgressBar _progress;
    private readonly TextBlock _status;

    public UpdateDownloadWindow(Version version)
    {
        WindowTheme.Attach(this);
        Title = "Updating MacroMaker";
        Width = 440;
        Height = 190;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("BgBrush");
        Foreground = Brush("TextBrush");
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Downloading MacroMaker {version.ToString(3)}",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush")
        });
        _status = new TextBlock
        {
            Text = "Starting download…",
            Margin = new Thickness(0, 8, 0, 12),
            Foreground = Brush("MutedTextBrush")
        };
        panel.Children.Add(_status);
        _progress = new ProgressBar
        {
            Height = 14,
            Minimum = 0,
            Maximum = 100,
            IsIndeterminate = true
        };
        panel.Children.Add(_progress);
        Content = panel;
    }

    public void SetProgress(long downloaded, long total)
    {
        if (total <= 0)
        {
            _progress.IsIndeterminate = true;
            _status.Text = $"Downloaded {FormatBytes(downloaded)}";
            return;
        }

        _progress.IsIndeterminate = false;
        _progress.Value = Math.Clamp(downloaded * 100.0 / total, 0, 100);
        _status.Text = $"{FormatBytes(downloaded)} of {FormatBytes(total)}";
    }

    public void SetStatus(string text)
    {
        _progress.IsIndeterminate = true;
        _status.Text = text;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L)
            return $"{bytes / 1024d / 1024d:0.0} MB";
        if (bytes >= 1024L)
            return $"{bytes / 1024d:0.0} KB";
        return $"{bytes} B";
    }

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
