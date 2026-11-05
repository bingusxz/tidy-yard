using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TidyYard.Interop;

namespace TidyYard.Services;

/// <summary>
/// Draws the tray glyph: a flat row of three fence posts on two rails, one colour, no shading.
/// The colour follows the taskbar (light or dark) so the glyph stays legible.
/// </summary>
public static class TrayIconRenderer
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48];

    public static System.Drawing.Icon Create()
    {
        var color = IsTaskbarLight()
            ? Color.FromRgb(0x22, 0x20, 0x1C)
            : Color.FromRgb(0xF3, 0xEF, 0xE6);

        using var stream = new MemoryStream();
        WriteIco(stream, Sizes.Select(size => (size, Render(size, color))).ToList());
        stream.Position = 0;

        var target = (int)Math.Round(16 * NativeMethods.GetSystemDpiScale());
        return new System.Drawing.Icon(stream, target, target);
    }

    private static bool IsTaskbarLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
    }

    /// <summary>Glyph in a 16-unit box: posts 2 wide at x = 2, 7, 12 with pointed tops; rails at y = 6 and 10.</summary>
    private static Geometry BuildGlyph()
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var x in new[] { 2.0, 7.0, 12.0 })
        {
            var post = new StreamGeometry();
            using (var ctx = post.Open())
            {
                ctx.BeginFigure(new Point(x, 3), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(x + 1, 1.5), true, false);
                ctx.LineTo(new Point(x + 2, 3), true, false);
                ctx.LineTo(new Point(x + 2, 14.5), true, false);
                ctx.LineTo(new Point(x, 14.5), true, false);
            }

            group.Children.Add(post);
        }

        group.Children.Add(new RectangleGeometry(new Rect(0.5, 5.5, 15, 1.5)));
        group.Children.Add(new RectangleGeometry(new Rect(0.5, 9.5, 15, 1.5)));
        return group;
    }

    /// <summary>Returns straight-alpha BGRA pixels, top row first.</summary>
    private static byte[] Render(int size, Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 16.0, size / 16.0));
            dc.DrawGeometry(new SolidColorBrush(color), null, BuildGlyph());
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);

        // ICO stores straight alpha; RenderTargetBitmap gives premultiplied.
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            if (a is > 0 and < 255)
            {
                pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
                pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
                pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
            }
        }

        return pixels;
    }

    private static void WriteIco(Stream stream, IReadOnlyList<(int Size, byte[] Pixels)> images)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);

        var offset = 6 + 16 * images.Count;
        var payloads = new List<byte[]>();
        foreach (var (size, pixels) in images)
        {
            var payload = BuildDib(size, pixels);
            payloads.Add(payload);
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)payload.Length);
            writer.Write((uint)offset);
            offset += payload.Length;
        }

        foreach (var payload in payloads)
        {
            writer.Write(payload);
        }
    }

    /// <summary>32-bit DIB icon image: header, bottom-up BGRA rows, then an empty AND mask.</summary>
    private static byte[] BuildDib(int size, byte[] pixels)
    {
        var maskStride = (size + 31) / 32 * 4;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(40);
        w.Write(size);
        w.Write(size * 2);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(0);
        w.Write(size * size * 4 + maskStride * size);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);

        for (var row = size - 1; row >= 0; row--)
        {
            w.Write(pixels, row * size * 4, size * 4);
        }

        w.Write(new byte[maskStride * size]);
        w.Flush();
        return ms.ToArray();
    }
}
