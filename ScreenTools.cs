using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacroMaker;

internal static class ScreenTools
{
    public static string GetPixelHex(int x, int y)
    {
        var hdc = NativeMethods.GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            return "0x000000";

        try
        {
            var colorRef = NativeMethods.GetPixel(hdc, x, y);
            var r = (byte)(colorRef & 0xFF);
            var g = (byte)((colorRef >> 8) & 0xFF);
            var b = (byte)((colorRef >> 16) & 0xFF);
            return $"0x{r:X2}{g:X2}{b:X2}";
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    public static bool ColorMatches(int x, int y, string targetHex, int tolerance)
    {
        if (!TryParseColor(targetHex, out var tr, out var tg, out var tb))
            return false;

        var hdc = NativeMethods.GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            return false;

        try
        {
            var colorRef = NativeMethods.GetPixel(hdc, x, y);
            var r = (int)(colorRef & 0xFF);
            var g = (int)((colorRef >> 8) & 0xFF);
            var b = (int)((colorRef >> 16) & 0xFF);
            tolerance = Math.Clamp(tolerance, 0, 255);
            return Math.Abs(r - tr) <= tolerance &&
                   Math.Abs(g - tg) <= tolerance &&
                   Math.Abs(b - tb) <= tolerance;
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    public static bool TryParseColor(string value, out int r, out int g, out int b)
    {
        r = g = b = 0;
        var text = value.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        else if (text.StartsWith('#'))
            text = text[1..];

        if (text.Length != 6 || !int.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var rgb))
            return false;

        r = (rgb >> 16) & 0xFF;
        g = (rgb >> 8) & 0xFF;
        b = rgb & 0xFF;
        return true;
    }

    public static ScreenRegion VirtualScreenRegion()
    {
        const int SM_XVIRTUALSCREEN = 76;
        const int SM_YVIRTUALSCREEN = 77;
        const int SM_CXVIRTUALSCREEN = 78;
        const int SM_CYVIRTUALSCREEN = 79;
        return new ScreenRegion(
            NativeMethods.GetSystemMetrics(SM_XVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(SM_YVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(SM_CXVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(SM_CYVIRTUALSCREEN));
    }

    public static BitmapSource CaptureRegion(ScreenRegion region)
    {
        if (region.Width <= 0 || region.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(region));

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            throw new InvalidOperationException("Could not access the screen.");

        var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        var bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, region.Width, region.Height);
        if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
        {
            if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            throw new InvalidOperationException("Could not create screen capture bitmap.");
        }

        var oldObject = NativeMethods.SelectObject(memoryDc, bitmap);
        try
        {
            if (!NativeMethods.BitBlt(memoryDc, 0, 0, region.Width, region.Height,
                    screenDc, region.X, region.Y, NativeMethods.SRCCOPY))
                throw new InvalidOperationException("Screen capture failed.");

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (oldObject != IntPtr.Zero)
                NativeMethods.SelectObject(memoryDc, oldObject);
            NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public static void SavePng(BitmapSource source, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static BitmapSource LoadImage(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    public static PixelBuffer ToBgra32(BitmapSource source)
    {
        BitmapSource converted = source;
        if (source.Format != PixelFormats.Bgra32)
        {
            var format = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            format.Freeze();
            converted = format;
        }

        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return new PixelBuffer(converted.PixelWidth, converted.PixelHeight, stride, pixels);
    }
}

internal readonly record struct PixelBuffer(int Width, int Height, int Stride, byte[] Pixels);

internal static class ImageMatcher
{
    private static readonly object TemplateCacheLock = new();
    private static readonly Dictionary<string, (DateTime Stamp, PixelBuffer Buffer)> TemplateCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] SupportedImageExtensions = [".png", ".jpg", ".jpeg", ".bmp"];

    public static Task<ImageMatch?> FindAsync(MacroCommand command, CancellationToken token, Action<string>? progress = null)
    {
        return Task.Run(() => Find(command, token, progress), token);
    }

    public static ImageMatch? Find(MacroCommand command, CancellationToken token, Action<string>? progress = null)
    {
        var candidates = GetCandidatePaths(command);
        if (candidates.Count == 0)
            return null;

        var region = GetSearchRegion(command);
        var full = ScreenTools.VirtualScreenRegion();
        region = ClampRegion(region, full);
        if (region.IsEmpty)
            return null;

        // Capture once, then test every priority image against the same frame.
        // This keeps folder-based priority checks much faster than recapturing
        // the screen for every image.
        var screen = ScreenTools.ToBgra32(ScreenTools.CaptureRegion(region));
        var tolerance = Math.Clamp(command.ImageTolerance, 0, 255);

        for (var i = 0; i < candidates.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var imagePath = candidates[i];
            if (!File.Exists(imagePath))
                continue;

            progress?.Invoke($"Searching image {i + 1}/{candidates.Count}: {Path.GetFileName(imagePath)}");

            var template = LoadTemplateCached(imagePath);
            var match = FindTemplate(screen, template, region, tolerance, token);
            if (match.HasValue)
            {
                progress?.Invoke($"Found image: {Path.GetFileName(imagePath)}");
                return match.Value with { SourcePath = imagePath };
            }
        }

        return null;
    }

    public static List<string> GetCandidatePaths(MacroCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.ImageFolder))
        {
            var folder = ProjectPaths.Resolve(command.ImageFolder);
            if (!Directory.Exists(folder))
                return new List<string>();

            var searchOption = command.ImageIncludeSubfolders
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            var all = Directory.EnumerateFiles(folder, "*.*", searchOption)
                .Where(IsSupportedImage)
                .ToDictionary(path => Path.GetRelativePath(folder, path), path => path, StringComparer.OrdinalIgnoreCase);

            var ordered = new List<string>();
            foreach (var item in command.ImagePriority ?? new List<string>())
            {
                var relative = item.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                if (all.TryGetValue(relative, out var path) && !ordered.Contains(path, StringComparer.OrdinalIgnoreCase))
                    ordered.Add(path);
            }

            // Any new images appear after the saved priority list until the user
            // moves them and saves the project.
            ordered.AddRange(all
                .Where(pair => !ordered.Contains(pair.Value, StringComparer.OrdinalIgnoreCase))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Value));
            return ordered;
        }

        var single = ProjectPaths.Resolve(command.ImagePath);
        return !string.IsNullOrWhiteSpace(single) && File.Exists(single)
            ? new List<string> { single }
            : new List<string>();
    }

    private static bool IsSupportedImage(string path)
        => SupportedImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static ImageMatch? FindTemplate(PixelBuffer screen, PixelBuffer template, ScreenRegion region, int tolerance, CancellationToken token)
    {
        if (template.Width <= 0 || template.Height <= 0 ||
            template.Width > screen.Width || template.Height > screen.Height)
            return null;

        var anchors = BuildAnchors(template);
        if (anchors.Count == 0)
            return null;

        var maxX = screen.Width - template.Width;
        var maxY = screen.Height - template.Height;
        for (var y = 0; y <= maxY; y++)
        {
            if ((y & 15) == 0)
                token.ThrowIfCancellationRequested();

            for (var x = 0; x <= maxX; x++)
            {
                if (!AnchorsMatch(screen, template, anchors, x, y, tolerance))
                    continue;
                if (VerifyMatch(screen, template, x, y, tolerance))
                    return new ImageMatch(region.X + x, region.Y + y, template.Width, template.Height);
            }
        }
        return null;
    }

    private static PixelBuffer LoadTemplateCached(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var stamp = File.GetLastWriteTimeUtc(fullPath);

        lock (TemplateCacheLock)
        {
            if (TemplateCache.TryGetValue(fullPath, out var cached) && cached.Stamp == stamp)
                return cached.Buffer;

            var buffer = ScreenTools.ToBgra32(ScreenTools.LoadImage(fullPath));
            TemplateCache[fullPath] = (stamp, buffer);
            return buffer;
        }
    }

    private static ScreenRegion GetSearchRegion(MacroCommand command)
    {
        if (command.SearchWidth <= 0 || command.SearchHeight <= 0)
            return ScreenTools.VirtualScreenRegion();

        return new ScreenRegion(command.SearchX, command.SearchY, command.SearchWidth, command.SearchHeight);
    }

    private static ScreenRegion ClampRegion(ScreenRegion region, ScreenRegion bounds)
    {
        var left = Math.Max(region.X, bounds.X);
        var top = Math.Max(region.Y, bounds.Y);
        var right = Math.Min(region.X + region.Width, bounds.X + bounds.Width);
        var bottom = Math.Min(region.Y + region.Height, bounds.Y + bounds.Height);
        return new ScreenRegion(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static List<(int X, int Y)> BuildAnchors(PixelBuffer template)
    {
        var anchors = new List<(int X, int Y)>();
        const int grid = 5;

        for (var gy = 0; gy < grid; gy++)
        {
            for (var gx = 0; gx < grid; gx++)
            {
                var x = Math.Clamp((int)Math.Round((gx + 0.5) * template.Width / grid), 0, template.Width - 1);
                var y = Math.Clamp((int)Math.Round((gy + 0.5) * template.Height / grid), 0, template.Height - 1);
                var index = y * template.Stride + x * 4;
                if (template.Pixels[index + 3] >= 32)
                    anchors.Add((x, y));
            }
        }

        if (anchors.Count < 6)
        {
            for (var y = 0; y < template.Height && anchors.Count < 16; y += Math.Max(1, template.Height / 8))
            {
                for (var x = 0; x < template.Width && anchors.Count < 16; x += Math.Max(1, template.Width / 8))
                {
                    var index = y * template.Stride + x * 4;
                    if (template.Pixels[index + 3] >= 32)
                        anchors.Add((x, y));
                }
            }
        }

        return anchors;
    }

    private static bool AnchorsMatch(PixelBuffer screen, PixelBuffer template,
        List<(int X, int Y)> anchors, int offsetX, int offsetY, int tolerance)
    {
        foreach (var (x, y) in anchors)
        {
            var ti = y * template.Stride + x * 4;
            if (template.Pixels[ti + 3] < 32)
                continue;

            var si = (offsetY + y) * screen.Stride + (offsetX + x) * 4;
            if (!PixelNear(screen.Pixels, si, template.Pixels, ti, tolerance))
                return false;
        }

        return true;
    }

    private static bool VerifyMatch(PixelBuffer screen, PixelBuffer template, int offsetX, int offsetY, int tolerance)
    {
        var step = Math.Max(1, Math.Min(template.Width, template.Height) / 45);
        var checkedPixels = 0;
        var mismatches = 0;

        for (var y = 0; y < template.Height; y += step)
        {
            for (var x = 0; x < template.Width; x += step)
            {
                var ti = y * template.Stride + x * 4;
                if (template.Pixels[ti + 3] < 32)
                    continue;

                checkedPixels++;
                var si = (offsetY + y) * screen.Stride + (offsetX + x) * 4;
                if (!PixelNear(screen.Pixels, si, template.Pixels, ti, tolerance))
                {
                    mismatches++;
                    if (mismatches > Math.Max(2, checkedPixels / 20 + 1))
                        return false;
                }
            }
        }

        return checkedPixels > 0 && mismatches <= Math.Max(2, checkedPixels / 20 + 1);
    }

    private static bool PixelNear(byte[] a, int ai, byte[] b, int bi, int tolerance)
    {
        return Math.Abs(a[ai] - b[bi]) <= tolerance &&
               Math.Abs(a[ai + 1] - b[bi + 1]) <= tolerance &&
               Math.Abs(a[ai + 2] - b[bi + 2]) <= tolerance;
    }
}

internal static class ColorFinder
{
    public static Task<(int X, int Y)?> FindAsync(MacroCommand command, CancellationToken token)
    {
        return Task.Run(() => Find(command, token), token);
    }

    public static (int X, int Y)? Find(MacroCommand command, CancellationToken token)
    {
        if (!ScreenTools.TryParseColor(command.ColorHex, out var tr, out var tg, out var tb))
            return null;

        var full = ScreenTools.VirtualScreenRegion();
        var region = command.SearchWidth <= 0 || command.SearchHeight <= 0
            ? full
            : new ScreenRegion(command.SearchX, command.SearchY, command.SearchWidth, command.SearchHeight);

        var left = Math.Max(region.X, full.X);
        var top = Math.Max(region.Y, full.Y);
        var right = Math.Min(region.X + region.Width, full.X + full.Width);
        var bottom = Math.Min(region.Y + region.Height, full.Y + full.Height);
        region = new ScreenRegion(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        if (region.IsEmpty)
            return null;

        var buffer = ScreenTools.ToBgra32(ScreenTools.CaptureRegion(region));
        var tolerance = Math.Clamp(command.ColorTolerance, 0, 255);

        for (var y = 0; y < buffer.Height; y++)
        {
            if ((y & 31) == 0)
                token.ThrowIfCancellationRequested();

            for (var x = 0; x < buffer.Width; x++)
            {
                var i = y * buffer.Stride + x * 4;
                var b = buffer.Pixels[i];
                var g = buffer.Pixels[i + 1];
                var r = buffer.Pixels[i + 2];
                if (Math.Abs(r - tr) <= tolerance &&
                    Math.Abs(g - tg) <= tolerance &&
                    Math.Abs(b - tb) <= tolerance)
                    return (region.X + x, region.Y + y);
            }
        }

        return null;
    }
}
