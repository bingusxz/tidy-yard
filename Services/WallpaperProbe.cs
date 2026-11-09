using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TidyYard.Interop;

namespace TidyYard.Services;

/// <summary>
/// Estimates what sits behind a transparent window: the average colour of the wallpaper under the
/// window's own area on the primary monitor, or of the whole wallpaper when that is not known.
/// </summary>
public static class WallpaperProbe
{
    private static readonly Color Fallback = Color.FromRgb(0x10, 0x14, 0x1C);
    private static string? _cachedKey;
    private static byte[]? _pixels; // BGRA, decoded small
    private static int _width;
    private static int _height;
    private static Color _solid = Fallback;

    /// <summary>Backdrop colours for a window occupying <paramref name="screenArea"/> (physical pixels).</summary>
    public static IReadOnlyList<Color> Backdrops(Rect? screenArea = null)
    {
        try
        {
            Load();
            if (_pixels is null) return new[] { _solid };
            return new[] { screenArea is { } area ? AverageUnder(area) : Average(0, 0, _width, _height) };
        }
        catch
        {
            // An unreadable wallpaper is not worth failing a theme change over.
            return new[] { Fallback };
        }
    }

    private static void Load()
    {
        var path = NativeMethods.GetWallpaperPath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _pixels = null;
            _cachedKey = null;
            _solid = SolidDesktop();
            return;
        }

        var key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
        if (key == _cachedKey) return;
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.DecodePixelWidth = 160;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        _width = converted.PixelWidth;
        _height = converted.PixelHeight;
        var pixels = new byte[_width * _height * 4];
        converted.CopyPixels(pixels, _width * 4, 0);
        _pixels = pixels;
        _cachedKey = key;
    }

    private static Color SolidDesktop()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");
        var parts = (key?.GetValue("Background") as string)?.Split(' ');
        return parts is { Length: 3 } && byte.TryParse(parts[0], out var r) && byte.TryParse(parts[1], out var g) && byte.TryParse(parts[2], out var b)
            ? Color.FromRgb(r, g, b)
            : Fallback;
    }

    private static string WallpaperStyle()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        return key?.GetValue("WallpaperStyle") as string ?? "10";
    }

    /// <summary>Maps the window onto the wallpaper the way Windows lays it out (Fill, Fit or Stretch) and averages it.</summary>
    private static Color AverageUnder(Rect area)
    {
        var (screenWidth, screenHeight) = NativeMethods.GetPrimaryScreenSize();
        if (screenWidth <= 0 || screenHeight <= 0 || area.Width <= 0 || area.Height <= 0
            || area.Left < 0 || area.Top < 0 || area.Right > screenWidth || area.Bottom > screenHeight)
        {
            // Another monitor, or a window not fully on the primary one: use the whole picture.
            return Average(0, 0, _width, _height);
        }

        double scaleX, scaleY;
        var style = WallpaperStyle();
        if (style == "2") // Stretch
        {
            scaleX = (double)screenWidth / _width;
            scaleY = (double)screenHeight / _height;
        }
        else
        {
            var fill = (double)screenWidth / _width;
            var other = (double)screenHeight / _height;
            scaleX = scaleY = style == "6" ? Math.Min(fill, other) : Math.Max(fill, other); // Fit : Fill
        }

        var offsetX = (screenWidth - _width * scaleX) / 2;
        var offsetY = (screenHeight - _height * scaleY) / 2;
        var left = (int)Math.Floor((area.Left - offsetX) / scaleX);
        var top = (int)Math.Floor((area.Top - offsetY) / scaleY);
        var right = (int)Math.Ceiling((area.Right - offsetX) / scaleX);
        var bottom = (int)Math.Ceiling((area.Bottom - offsetY) / scaleY);
        return Average(left, top, right, bottom);
    }

    private static Color Average(int left, int top, int right, int bottom)
    {
        left = Math.Clamp(left, 0, _width - 1);
        top = Math.Clamp(top, 0, _height - 1);
        right = Math.Clamp(right, left + 1, _width);
        bottom = Math.Clamp(bottom, top + 1, _height);
        double r = 0, g = 0, b = 0;
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var i = (y * _width + x) * 4;
                b += _pixels![i]; g += _pixels[i + 1]; r += _pixels[i + 2]; count++;
            }
        }
        return count == 0 ? Fallback : Color.FromRgb((byte)Math.Round(r / count), (byte)Math.Round(g / count), (byte)Math.Round(b / count));
    }
}
