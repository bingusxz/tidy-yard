using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TidyYard.Services;

public sealed record BackupResult(bool Ok, string Message);

/// <summary>
/// Backs up and restores everything Tidy Yard keeps: events and checklists (the database), settings and
/// fence layout, as one .zip file. Backing up reads a consistent copy of the live database. Restoring is
/// done in two steps so the running app never has its own files swapped under it: the backup is checked and
/// staged, and the swap happens the next time the app starts, before anything has opened the data. The
/// current data is copied to <c>backups\before-restore-...</c> first, and nothing is deleted.
/// Subscribed calendar links are encrypted for one Windows account, so after restoring on another PC they
/// have to be added again.
/// </summary>
public sealed class BackupService
{
    private const string DatabaseName = "tidyyard.db";
    private const string InfoName = "backup-info.json";
    private const long MaxDatabaseBytes = 1L * 1024 * 1024 * 1024;
    private const long MaxSmallFileBytes = 50L * 1024 * 1024;

    /// <summary>The only entries ever read from a backup. Names inside the zip are never used as paths.</summary>
    private static readonly string[] DataFiles = [DatabaseName, "settings.json", "fences.json", "notes.json"];

    private readonly string _dataDirectory;
    private readonly Action _flush;

    /// <param name="dataDirectory">%APPDATA%\TidyYard.</param>
    /// <param name="flush">Writes pending settings and fence changes to disk before a backup is taken.</param>
    public BackupService(string dataDirectory, Action flush)
    {
        _dataDirectory = dataDirectory;
        _flush = flush;
    }

    private string PendingFolder => PendingFolderFor(_dataDirectory);

    private static string PendingFolderFor(string dataDirectory) => Path.Combine(dataDirectory, "restore-pending");

    /// <summary>A checked backup is waiting to be applied at the next start.</summary>
    public bool HasPendingRestore => File.Exists(Path.Combine(PendingFolder, DatabaseName));

    public BackupResult CreateBackup(string zipPath)
    {
        var work = Path.Combine(Path.GetTempPath(), "tidyyard-backup-" + Guid.NewGuid().ToString("N"));
        var partial = zipPath + ".tmp";
        try
        {
            _flush();
            Directory.CreateDirectory(work);

            // A consistent copy of the live database, even while the app has it open.
            var databaseCopy = Path.Combine(work, DatabaseName);
            var livePath = Path.Combine(_dataDirectory, DatabaseName);
            var schema = 0;
            if (File.Exists(livePath))
            {
                using var source = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = livePath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false,
                }.ToString());
                source.Open();
                using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = databaseCopy,
                    Pooling = false,
                }.ToString());
                destination.Open();
                source.BackupDatabase(destination);
                schema = ReadSchemaVersion(destination);
            }

            if (File.Exists(partial))
            {
                File.Delete(partial);
            }

            using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                if (File.Exists(databaseCopy))
                {
                    zip.CreateEntryFromFile(databaseCopy, DatabaseName, CompressionLevel.Optimal);
                }

                foreach (var name in DataFiles.Skip(1))
                {
                    AddSharedFile(zip, Path.Combine(_dataDirectory, name), name);
                }

                var info = JsonSerializer.Serialize(new
                {
                    app = "Tidy Yard",
                    version = typeof(BackupService).Assembly.GetName().Version?.ToString(3),
                    schema,
                    created = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
                });
                var entry = zip.CreateEntry(InfoName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(info);
            }

            File.Move(partial, zipPath, overwrite: true);
            return new BackupResult(true, $"Saved {Path.GetFileName(zipPath)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
        {
            TryDelete(partial);
            return new BackupResult(false, $"Could not save the backup: {ex.Message}");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDeleteFolder(work);
        }
    }

    /// <summary>
    /// Checks a backup file and stages it. Nothing in the live data changes: the swap happens at the next
    /// start, and the person can cancel until then.
    /// </summary>
    public BackupResult StageRestore(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var database = zip.GetEntry(DatabaseName);
            if (database is null)
            {
                return new BackupResult(false, "That file is not a Tidy Yard backup.");
            }

            if (database.Length > MaxDatabaseBytes || zip.Entries.Any(e => e.Length > MaxSmallFileBytes && e.Name != DatabaseName))
            {
                return new BackupResult(false, "That backup is larger than expected, so it was not opened.");
            }

            TryDeleteFolder(PendingFolder);
            Directory.CreateDirectory(PendingFolder);

            // Only these known names are ever extracted, always to a fixed place.
            foreach (var name in DataFiles)
            {
                if (zip.GetEntry(name) is { } entry)
                {
                    entry.ExtractToFile(Path.Combine(PendingFolder, name), overwrite: true);
                }
            }

            var problem = CheckStagedDatabase(Path.Combine(PendingFolder, DatabaseName));
            if (problem is not null)
            {
                TryDeleteFolder(PendingFolder);
                return new BackupResult(false, problem);
            }

            return new BackupResult(true, "Backup checked. Restart Tidy Yard to finish restoring. Your current data is saved first.");
        }
        catch (InvalidDataException)
        {
            TryDeleteFolder(PendingFolder);
            return new BackupResult(false, "That file is not a Tidy Yard backup.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteFolder(PendingFolder);
            return new BackupResult(false, $"Could not read the backup: {ex.Message}");
        }
    }

    /// <summary>Forgets a staged backup before it is applied.</summary>
    public void CancelPendingRestore() => TryDeleteFolder(PendingFolder);

    /// <summary>
    /// Run once at start-up, before the data is opened. Applies a staged backup: the current files are
    /// copied to a "before-restore" folder first, then replaced. If anything goes wrong the current data
    /// is left as it was, and the staged backup is set aside so it is not retried at every start.
    /// </summary>
    public static bool ApplyPendingRestore(string dataDirectory)
    {
        var pending = PendingFolderFor(dataDirectory);
        if (!File.Exists(Path.Combine(pending, DatabaseName)))
        {
            return false;
        }

        try
        {
            var safe = UnusedFolder(Path.Combine(dataDirectory, "backups"), "before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(safe);
            foreach (var name in DataFiles)
            {
                var current = Path.Combine(dataDirectory, name);
                if (File.Exists(current))
                {
                    File.Copy(current, Path.Combine(safe, name), overwrite: false);
                }
            }

            foreach (var name in DataFiles)
            {
                var staged = Path.Combine(pending, name);
                if (!File.Exists(staged))
                {
                    continue; // the backup did not include it: keep the current one
                }

                var target = Path.Combine(dataDirectory, name);
                var incoming = target + ".incoming";
                File.Copy(staged, incoming, overwrite: true);
                File.Move(incoming, target, overwrite: true);
            }

            TryDeleteFolder(pending);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                var aside = UnusedFolder(Path.Combine(dataDirectory, "backups"), "restore-failed-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(Path.GetDirectoryName(aside)!);
                Directory.Move(pending, aside);
            }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException)
            {
                // Leave it; the current data is untouched either way.
            }

            return false;
        }
    }

    /// <summary>A folder path under <paramref name="parent"/> that does not exist yet ("name", "name (2)", ...). Never reuses one.</summary>
    private static string UnusedFolder(string parent, string name)
    {
        var path = Path.Combine(parent, name);
        for (var n = 2; Directory.Exists(path); n++)
        {
            path = Path.Combine(parent, $"{name} ({n})");
        }

        return path;
    }

    /// <summary>Null if the staged database looks right, otherwise a message for the person.</summary>
    private static string? CheckStagedDatabase(string path)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            connection.Open();

            if (ScalarText(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'events'") is null)
            {
                return "That file is not a Tidy Yard backup.";
            }

            if (ReadSchemaVersion(connection) > StorageService.CurrentSchemaVersion)
            {
                return "That backup was made by a newer version of Tidy Yard. Update Tidy Yard first.";
            }

            return string.Equals(ScalarText(connection, "PRAGMA integrity_check"), "ok", StringComparison.OrdinalIgnoreCase)
                ? null
                : "That backup is damaged, so it was not used.";
        }
        catch (SqliteException)
        {
            return "That file is not a Tidy Yard backup.";
        }
    }

    private static int ReadSchemaVersion(SqliteConnection connection) =>
        int.TryParse(ScalarText(connection, "PRAGMA user_version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
            ? version
            : 0;

    private static string? ScalarText(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        // "No row" must stay null: Convert.ToString(null) would turn it into an empty string.
        return command.ExecuteScalar() is { } value and not DBNull ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Adds a file that the app may have open (it is only read).</summary>
    private static void AddSharedFile(ZipArchive zip, string path, string name)
    {
        if (!File.Exists(path))
        {
            return;
        }

        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var target = entry.Open();
        source.CopyTo(target);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
