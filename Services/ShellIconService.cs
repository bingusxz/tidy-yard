using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TidyYard.Interop;

namespace TidyYard.Services;

/// <summary>
/// Explorer icons and thumbnails for files and folders. Loaded in the background of the UI
/// thread (the shell needs an STA thread) so a full fence does not block the window.
/// </summary>
public static class ShellIconService
{
    /// <summary>Requested pixel size: crisp at 48px up to 200% scaling.</summary>
    private const int PixelSize = 96;

    public static void LoadAsync(string path, Action<ImageSource> onLoaded) =>
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (NativeMethods.GetShellImage(path, PixelSize) is { } image)
            {
                var bitmap = BitmapSource.Create(
                    image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null, image.Pixels, image.Width * 4);
                bitmap.Freeze();
                onLoaded(bitmap);
            }
        });
}
