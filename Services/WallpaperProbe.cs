using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TidyYard.Interop;

namespace TidyYard.Services;

/// <summary>
/// Estimates what sits behind a transparent window as the average colour of the desktop wallpaper.
/// Edge cases (bright swirls, dark corners) average out, so text does not flip on a single bright patch.
/// </summary>
public static class WallpaperProbe
{
    private static readonly Color Fallback = Color.FromRgb(0x10, 0x14, 0x1C);
    private static string? _cachedKey;
    private static IReadOnlyList<Color> _cached = new[] { Fallback };

    public static IReadOnlyList<Color> Backdrops()
    {
        try
        {
            var path = NativeMethods.GetWallpaperPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return _cached = new[] { SolidDesktop() };
            var key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
            if (key == _cachedKey) return _cached;
            _cached = Sample(path);
            _cachedKey = key;
        }
        catch
        {
            // An unreadable wallpaper is not worth failing a theme change over.
            _cached = new[] { Fallback };
        }
        return _cached;
    }

    private static Color SolidDesktop()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");
        var parts = (key?.GetValue("Background") as string)?.Split(' ');
        return parts is { Length: 3 } && byte.TryParse(parts[0], out var r) && byte.TryParse(parts[1], out var g) && byte.TryParse(parts[2], out var b)
            ? Color.FromRgb(r, g, b)
            : Fallback;
    }

    private static IReadOnlyList<Color> Sample(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.DecodePixelWidth = 64;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);

        return new[] { Average(pixels, Enumerable.Range(0, width * height)) };
    }

    private static Color Average(byte[] pixels, IEnumerable<int> indexes)
    {
        double r = 0, g = 0, b = 0;
        var n = 0;
        foreach (var i in indexes)
        {
            b += pixels[i * 4]; g += pixels[i * 4 + 1]; r += pixels[i * 4 + 2]; n++;
        }
        return n == 0 ? Fallback : Color.FromRgb((byte)Math.Round(r / n), (byte)Math.Round(g / n), (byte)Math.Round(b / n));
    }
}
