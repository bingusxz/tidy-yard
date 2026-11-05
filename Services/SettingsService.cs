using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using TidyYard.Models;

namespace TidyYard.Services;

/// <summary>
/// Loads and saves %APPDATA%\TidyYard\settings.json. Writes are debounced.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly string _path;
    private readonly DispatcherTimer _saveTimer;

    public SettingsService(string path)
    {
        _path = path;
        Current = Load();
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };
    }

    public AppSettings Current { get; }

    /// <summary>Raised after a change that affects how the app looks.</summary>
    public event EventHandler? Changed;

    public void Update(Action<AppSettings> change, bool notify = true)
    {
        change(Current);
        Normalize(Current);
        if (notify)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes any pending change immediately.</summary>
    public void Flush()
    {
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
            SaveNow();
        }
    }

    private AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
            Normalize(settings);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep the unreadable file next to the new one instead of overwriting it.
            try
            {
                var keep = Path.Combine(
                    Path.GetDirectoryName(_path)!,
                    $"settings.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Copy(_path, keep, overwrite: false);
            }
            catch (IOException)
            {
            }

            return new AppSettings();
        }
    }

    private void SaveNow()
    {
        try
        {
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are retried on the next change.
        }
    }

    private static void Normalize(AppSettings settings)
    {
        settings.OpacityPercent = Math.Clamp(
            settings.OpacityPercent, AppSettings.MinOpacityPercent, AppSettings.MaxOpacityPercent);
        settings.Theme = ThemeService.Resolve(settings.Theme).Key;
        settings.AccentColor = ThemeService.TryParseHex(settings.AccentColor, out var accent) ? ThemeService.ToHex(accent) : null;
        settings.TintColor = ThemeService.TryParseHex(settings.TintColor, out var tint) ? ThemeService.ToHex(tint) : null;
    }
}
