using System.IO;
using System.Text.Json;

namespace TidyYard.Services;

public sealed record MoveResult(int Moved, IReadOnlyList<string> Problems);

/// <summary>
/// The only place Tidy Yard moves user files. Rules:
/// - never overwrite: a name clash gets "Name (2).ext";
/// - never delete;
/// - every move is appended to %APPDATA%\TidyYard\moves.log (JSON lines);
/// - the last batch can be undone.
/// </summary>
public sealed class FileMoveService
{
    private readonly string _logPath;
    private List<(string From, string To)> _lastBatch = [];

    public FileMoveService(string logPath) => _logPath = logPath;

    public event EventHandler? UndoChanged;

    public bool CanUndo => _lastBatch.Count > 0;

    public string UndoLabel { get; private set; } = string.Empty;

    /// <summary>Moves files and folders into <paramref name="destinationFolder"/>.</summary>
    public MoveResult MoveInto(IEnumerable<string> paths, string destinationFolder, string label) =>
        MoveInto(paths.Select(p => (p, destinationFolder)), label);

    /// <summary>Moves each item into its own destination folder, as one undoable batch.</summary>
    public MoveResult MoveInto(IEnumerable<(string Path, string Folder)> items, string label)
    {
        var problems = new List<string>();
        var batch = new List<(string From, string To)>();

        foreach (var (raw, folder) in items)
        {
            var destination = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            if (!Directory.Exists(destination))
            {
                problems.Add($"{Path.GetFileName(raw)}: the fence folder does not exist ({destination}).");
                continue;
            }

            var source = Path.GetFullPath(raw).TrimEnd(Path.DirectorySeparatorChar);
            var name = Path.GetFileName(source);
            var isFolder = Directory.Exists(source);

            if (!isFolder && !File.Exists(source))
            {
                continue;
            }

            if (string.Equals(Path.GetDirectoryName(source), destination, StringComparison.OrdinalIgnoreCase))
            {
                continue; // already in this fence
            }

            if (isFolder && (destination + Path.DirectorySeparatorChar).StartsWith(
                    source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{name}: a folder cannot be moved into itself.");
                continue;
            }

            if (isFolder && !SameDrive(source, destination))
            {
                problems.Add($"{name}: folders can only be moved within the same drive.");
                continue;
            }

            var target = UniquePath(Path.Combine(destination, name));
            try
            {
                if (isFolder)
                {
                    Directory.Move(source, target);
                }
                else
                {
                    File.Move(source, target, overwrite: false);
                }

                Log("move", source, target);
                batch.Add((source, target));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{name}: {ex.Message}");
            }
        }

        if (batch.Count > 0)
        {
            _lastBatch = batch;
            UndoLabel = $"Undo {label}";
            UndoChanged?.Invoke(this, EventArgs.Empty);
        }

        return new MoveResult(batch.Count, problems);
    }

    /// <summary>Moves the last batch back. Items whose original spot is taken get a "(2)" name.</summary>
    public MoveResult Undo()
    {
        var problems = new List<string>();
        var moved = 0;
        foreach (var (from, to) in Enumerable.Reverse(_lastBatch))
        {
            var isFolder = Directory.Exists(to);
            if (!isFolder && !File.Exists(to))
            {
                problems.Add($"{Path.GetFileName(to)} is no longer in the fence.");
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(from)!);
                var back = UniquePath(from);
                if (isFolder)
                {
                    Directory.Move(to, back);
                }
                else
                {
                    File.Move(to, back, overwrite: false);
                }

                Log("undo", to, back);
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{Path.GetFileName(to)}: {ex.Message}");
            }
        }

        _lastBatch = [];
        UndoLabel = string.Empty;
        UndoChanged?.Invoke(this, EventArgs.Empty);
        return new MoveResult(moved, problems);
    }

    /// <summary>"Report.pdf" -> "Report (2).pdf" -> "Report (3).pdf" ... until the name is free.</summary>
    public static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var folder = Path.GetDirectoryName(path)!;
        var isFolder = Directory.Exists(path);
        var stem = isFolder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        var extension = isFolder ? string.Empty : Path.GetExtension(path);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({n}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool SameDrive(string a, string b) =>
        string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);

    private void Log(string action, string from, string to)
    {
        try
        {
            var line = JsonSerializer.Serialize(new { time = DateTime.Now.ToString("O"), action, from, to });
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The move already happened; a missing log line must not undo it.
        }
    }
}
