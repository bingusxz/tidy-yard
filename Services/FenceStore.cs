using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using TidyYard.Models;

namespace TidyYard.Services;

/// <summary>
/// Fence layout in %APPDATA%\TidyYard\fences.json. Writes are debounced and atomic.
/// Fence folders and their files are never created, moved or deleted here.
/// </summary>
public sealed class FenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly List<Fence> _fences;
    private readonly DispatcherTimer _saveTimer;

    public FenceStore(string path)
    {
        _path = path;
        _fences = Load();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };
    }

    public IReadOnlyList<Fence> Fences => _fences;

    public void Add(Fence fence)
    {
        _fences.Add(fence);
        Changed();
    }

    public void Remove(Fence fence)
    {
        _fences.Remove(fence);
        Changed();
    }

    /// <summary>Call after changing a fence's properties.</summary>
    public void Changed()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Flush()
    {
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
            SaveNow();
        }
    }

    private List<Fence> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Fence>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep the unreadable file next to the new one instead of overwriting it.
            try
            {
                File.Copy(_path, Path.Combine(
                    Path.GetDirectoryName(_path)!, $"fences.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json"));
            }
            catch (IOException)
            {
            }

            return [];
        }
    }

    private void SaveNow()
    {
        try
        {
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_fences, JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Retried on the next change.
        }
    }
}
