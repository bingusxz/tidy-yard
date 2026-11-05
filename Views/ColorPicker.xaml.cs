using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TidyYard.Services;

namespace TidyYard.Views;

/// <summary>
/// HSV colour picker control: a hue/saturation wheel, a brightness slider, a hex field and presets.
/// This is control logic (colour maths and pointer handling), not app behaviour.
/// </summary>
public partial class ColorPicker : UserControl
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor),
        typeof(Color),
        typeof(ColorPicker),
        new FrameworkPropertyMetadata(Colors.Gray, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    public static readonly DependencyProperty PresetsProperty = DependencyProperty.Register(
        nameof(Presets),
        typeof(IEnumerable<Color>),
        typeof(ColorPicker),
        new PropertyMetadata(null, (d, e) => ((ColorPicker)d).PresetList.ItemsSource = (IEnumerable<Color>?)e.NewValue));

    private const int WheelPixels = 368; // 2x the on-screen size, for crisp edges at 150-200% scaling

    private static readonly Lazy<BitmapSource> WheelBitmap = new(RenderWheel);

    private double _hue;
    private double _saturation;
    private double _value = 1;
    private bool _dragging;
    private bool _updating;

    public ColorPicker()
    {
        InitializeComponent();
        Wheel.Source = WheelBitmap.Value;
        Loaded += (_, _) => SyncFromColor(SelectedColor);
    }

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public IEnumerable<Color>? Presets
    {
        get => (IEnumerable<Color>?)GetValue(PresetsProperty);
        set => SetValue(PresetsProperty, value);
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPicker)d;
        if (!picker._updating)
        {
            picker.SyncFromColor((Color)e.NewValue);
        }
    }

    /// <summary>Updates wheel thumb, slider, preview and hex text from a colour set from outside.</summary>
    private void SyncFromColor(Color color)
    {
        (_hue, _saturation, _value) = ToHsv(color);
        UpdateVisuals(color);
    }

    private void Commit()
    {
        var color = FromHsv(_hue, _saturation, _value);
        _updating = true;
        SelectedColor = color;
        _updating = false;
        UpdateVisuals(color);
    }

    private void UpdateVisuals(Color color)
    {
        _updating = true;
        Brightness.Value = Math.Round(_value * 100);
        _updating = false;

        Shade.Opacity = 1 - _value;
        Preview.Background = new SolidColorBrush(color);
        if (!HexBox.IsKeyboardFocused)
        {
            HexBox.Text = ThemeService.ToHex(color);
        }

        var radius = WheelHost.Width / 2;
        var angle = _hue * Math.PI / 180;
        Canvas.SetLeft(Thumb, radius + Math.Cos(angle) * _saturation * radius - Thumb.Width / 2);
        Canvas.SetTop(Thumb, radius - Math.Sin(angle) * _saturation * radius - Thumb.Height / 2);
    }

    private void PickAt(Point point)
    {
        var radius = WheelHost.Width / 2;
        var dx = point.X - radius;
        var dy = radius - point.Y;
        _hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        _saturation = Math.Min(1, Math.Sqrt(dx * dx + dy * dy) / radius);
        Commit();
    }

    private void OnWheelMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        WheelHost.CaptureMouse();
        PickAt(e.GetPosition(WheelHost));
    }

    private void OnWheelMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            PickAt(e.GetPosition(WheelHost));
        }
    }

    private void OnWheelMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        WheelHost.ReleaseMouseCapture();
    }

    private void OnBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || !IsLoaded)
        {
            return;
        }

        _value = e.NewValue / 100;
        Commit();
    }

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnHexCommit(sender, e);
            e.Handled = true;
        }
    }

    private void OnHexCommit(object sender, RoutedEventArgs e)
    {
        if (ThemeService.TryParseHex(HexBox.Text, out var color))
        {
            (_hue, _saturation, _value) = ToHsv(color);
            Commit();
        }

        HexBox.Text = ThemeService.ToHex(SelectedColor);
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Color color })
        {
            (_hue, _saturation, _value) = ToHsv(color);
            Commit();
        }
    }

    private static BitmapSource RenderWheel()
    {
        const int size = WheelPixels;
        var pixels = new byte[size * size * 4];
        var r = size / 2.0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x + 0.5 - r;
                var dy = r - (y + 0.5);
                var dist = Math.Sqrt(dx * dx + dy * dy);
                var alpha = Math.Clamp(r - dist, 0, 1); // anti-aliased rim
                if (alpha <= 0)
                {
                    continue;
                }

                var hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                var c = FromHsv(hue, Math.Min(1, dist / r), 1);
                var i = (y * size + x) * 4;
                pixels[i] = c.B;
                pixels[i + 1] = c.G;
                pixels[i + 2] = c.R;
                pixels[i + 3] = (byte)(alpha * 255);
            }
        }

        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static Color FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = (h % 360) switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static (double H, double S, double V) ToHsv(Color color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double h = 0;
        if (delta > 0)
        {
            if (max == r)
            {
                h = 60 * ((g - b) / delta % 6);
            }
            else if (max == g)
            {
                h = 60 * ((b - r) / delta + 2);
            }
            else
            {
                h = 60 * ((r - g) / delta + 4);
            }
        }

        return ((h + 360) % 360, max == 0 ? 0 : delta / max, max);
    }
}
