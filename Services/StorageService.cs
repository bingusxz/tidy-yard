using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TidyYard.Models;

namespace TidyYard.Services;

/// <summary>
/// SQLite storage at %APPDATA%\TidyYard\tidyyard.db.
/// </summary>
public sealed class StorageService
{
    private const int SchemaVersion = 4;
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm";
    private const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm";

    private readonly string _connectionString;

    public StorageService(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        BackUpBeforeUpgrade(databasePath);
        EnsureSchema();
    }

    /// <summary>
    /// Before an app update changes the database layout, keeps a copy of the old file next to it
    /// (tidyyard.db.backup-v2). Uses its own unpooled connection so the file is not held open while copying.
    /// </summary>
    private static void BackUpBeforeUpgrade(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return;
        }

        try
        {
            int version;
            var readOnly = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }
                .ToString();
            using (var connection = new SqliteConnection(readOnly))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version";
                version = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }

            var backup = $"{databasePath}.backup-v{version}";
            if (version > 0 && version < SchemaVersion && !File.Exists(backup))
            {
                File.Copy(databasePath, backup);
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // A missing backup must not stop the app from starting; the upgrade itself is transactional.
        }
    }

    public IReadOnlyList<CalendarEvent> GetEvents(DateOnly from, DateOnly to)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {Columns}
            FROM events
            WHERE date BETWEEN $from AND $to
            ORDER BY date, time IS NOT NULL, time, id
            """;
        command.Parameters.AddWithValue("$from", FormatDate(from));
        command.Parameters.AddWithValue("$to", FormatDate(to));
        var own = ReadEvents(command);

        using var imported = connection.CreateCommand();
        imported.CommandText =
            $"""
            SELECT {ImportedColumns}
            FROM imported_events e JOIN calendar_sources s ON s.id = e.source_id
            WHERE e.date BETWEEN $from AND $to
            """;
        imported.Parameters.AddWithValue("$from", FormatDate(from));
        imported.Parameters.AddWithValue("$to", FormatDate(to));
        var fromLinks = ReadEvents(imported);
        if (fromLinks.Count == 0)
        {
            return own;
        }

        // All-day first, then by time; the user's own events before subscribed ones at the same time.
        return [.. own.Concat(fromLinks)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Time is not null)
            .ThenBy(e => e.Time)
            .ThenBy(e => e.IsImported)
            .ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Events whose next reminder is at or before <paramref name="now"/>, earliest first.</summary>
    public IReadOnlyList<CalendarEvent> GetDueReminders(DateTime now)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {Columns}
            FROM events
            WHERE next_reminder IS NOT NULL AND next_reminder <= $now
            ORDER BY next_reminder, id
            """;
        command.Parameters.AddWithValue("$now", FormatDateTime(now));
        var own = ReadEvents(command);

        using var imported = connection.CreateCommand();
        imported.CommandText =
            $"""
            SELECT {ImportedColumns}
            FROM imported_events e JOIN calendar_sources s ON s.id = e.source_id
            WHERE e.next_reminder IS NOT NULL AND e.next_reminder <= $now
            ORDER BY e.next_reminder, e.id
            """;
        imported.Parameters.AddWithValue("$now", FormatDateTime(now));
        return [.. own.Concat(ReadEvents(imported))];
    }

    /// <summary>
    /// Sets when the next reminder for an event is shown; null clears it. Events from subscribed
    /// calendars have negative ids, so one id works for both kinds.
    /// </summary>
    public void SetNextReminder(long id, DateTime? next)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = id < 0
            ? "UPDATE imported_events SET next_reminder = $next WHERE id = $id"
            : "UPDATE events SET next_reminder = $next WHERE id = $id";
        command.Parameters.AddWithValue("$next", next is { } value ? FormatDateTime(value) : DBNull.Value);
        command.Parameters.AddWithValue("$id", Math.Abs(id));
        command.ExecuteNonQuery();
    }

    /// <summary>Gets one event by id, or null if it has been deleted.</summary>
    public CalendarEvent? GetEvent(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = id < 0
            ? $"SELECT {ImportedColumns} FROM imported_events e JOIN calendar_sources s ON s.id = e.source_id WHERE e.id = $id"
            : $"SELECT {Columns} FROM events WHERE id = $id";
        command.Parameters.AddWithValue("$id", Math.Abs(id));
        return ReadEvents(command).FirstOrDefault();
    }

    private const string Columns = "id, date, time, title, note, reminder_minutes, next_reminder";

    /// <summary>Same column order as <see cref="Columns"/>, plus the calendar's name; the id is negated.</summary>
    private const string ImportedColumns = "-e.id, e.date, e.time, e.title, e.location, NULL, e.next_reminder, s.name";

    private static List<CalendarEvent> ReadEvents(SqliteCommand command)
    {
        var result = new List<CalendarEvent>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new CalendarEvent
            {
                Id = reader.GetInt64(0),
                Date = DateOnly.ParseExact(reader.GetString(1), DateFormat, CultureInfo.InvariantCulture),
                Time = reader.IsDBNull(2)
                    ? null
                    : TimeOnly.ParseExact(reader.GetString(2), TimeFormat, CultureInfo.InvariantCulture),
                Title = reader.GetString(3),
                Note = reader.IsDBNull(4) ? null : reader.GetString(4),
                ReminderMinutes = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                NextReminder = reader.IsDBNull(6)
                    ? null
                    : DateTime.ParseExact(reader.GetString(6), DateTimeFormat, CultureInfo.InvariantCulture),
                IsImported = reader.FieldCount > 7,
                SourceName = reader.FieldCount > 7 ? reader.GetString(7) : null,
            });
        }

        return result;
    }

    public void Add(CalendarEvent calendarEvent)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO events (date, time, title, note, reminder_minutes, next_reminder, created_at, updated_at)
            VALUES ($date, $time, $title, $note, $reminderMinutes, $nextReminder, $now, $now);
            SELECT last_insert_rowid();
            """;
        AddFields(command, calendarEvent);
        calendarEvent.Id = (long)command.ExecuteScalar()!;
    }

    public void Update(CalendarEvent calendarEvent)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE events
            SET date = $date, time = $time, title = $title, note = $note,
                reminder_minutes = $reminderMinutes,
                -- Keep a reminder that already fired or is snoozed unless the date, time or lead time changed.
                next_reminder = CASE
                    WHEN date IS $date AND time IS $time AND reminder_minutes IS $reminderMinutes THEN next_reminder
                    ELSE $nextReminder
                END,
                updated_at = $now
            WHERE id = $id
            """;
        AddFields(command, calendarEvent);
        command.Parameters.AddWithValue("$id", calendarEvent.Id);
        command.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM checklist_items WHERE event_id = $id;
            DELETE FROM events WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Checklist items for the given events, in the order the user arranged them.</summary>
    public IReadOnlyDictionary<long, List<ChecklistItem>> GetChecklists(IReadOnlyCollection<long> eventIds)
    {
        var result = new Dictionary<long, List<ChecklistItem>>();
        if (eventIds.Count == 0)
        {
            return result;
        }

        using var connection = Open();
        using var command = connection.CreateCommand();
        var names = new List<string>();
        var index = 0;
        foreach (var id in eventIds)
        {
            var name = $"$e{index++}";
            names.Add(name);
            command.Parameters.AddWithValue(name, id);
        }

        command.CommandText =
            $"""
            SELECT id, event_id, text, done
            FROM checklist_items
            WHERE event_id IN ({string.Join(", ", names)})
            ORDER BY event_id, position, id
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var item = new ChecklistItem
            {
                Id = reader.GetInt64(0),
                EventId = reader.GetInt64(1),
                Text = reader.GetString(2),
                IsDone = reader.GetInt32(3) != 0,
            };
            if (!result.TryGetValue(item.EventId, out var list))
            {
                result[item.EventId] = list = [];
            }

            list.Add(item);
        }

        return result;
    }

    /// <summary>Replaces an event's whole checklist with <paramref name="items"/>, in that order.</summary>
    public void SaveChecklist(long eventId, IEnumerable<ChecklistItem> items)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM checklist_items WHERE event_id = $event";
            delete.Parameters.AddWithValue("$event", eventId);
            delete.ExecuteNonQuery();
        }

        var position = 0;
        foreach (var item in items)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO checklist_items (event_id, text, done, position) VALUES ($event, $text, $done, $position)";
            insert.Parameters.AddWithValue("$event", eventId);
            insert.Parameters.AddWithValue("$text", item.Text);
            insert.Parameters.AddWithValue("$done", item.IsDone ? 1 : 0);
            insert.Parameters.AddWithValue("$position", position++);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>Ticks or unticks one item straight from the agenda.</summary>
    public void SetItemDone(long itemId, bool done)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE checklist_items SET done = $done WHERE id = $id";
        command.Parameters.AddWithValue("$done", done ? 1 : 0);
        command.Parameters.AddWithValue("$id", itemId);
        command.ExecuteNonQuery();
    }

    // ---- Subscribed calendars ----------------------------------------------------------------

    private static readonly byte[] LinkEntropy = "TidyYard.CalendarLink"u8.ToArray();

    /// <summary>The link is a secret, so it is stored encrypted for this Windows user and never as plain text.</summary>
    private static byte[] Protect(string link) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(link), LinkEntropy, DataProtectionScope.CurrentUser);

    private static string? Unprotect(byte[] data)
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(data, LinkEntropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            // Another Windows account or a restored backup: the link cannot be read back.
            return null;
        }
    }

    public IReadOnlyList<CalendarSource> GetSources()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, url_protected, last_synced, last_error FROM calendar_sources ORDER BY name, id";
        var result = new List<CalendarSource>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var link = Unprotect((byte[])reader[2]);
            result.Add(new CalendarSource(
                reader.GetInt64(0),
                reader.GetString(1),
                link ?? string.Empty,
                reader.IsDBNull(3) ? null : DateTime.ParseExact(reader.GetString(3), DateTimeFormat, CultureInfo.InvariantCulture),
                link is null ? "This link cannot be read on this account. Remove it and add it again." : reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return result;
    }

    public long AddSource(string name, string link)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO calendar_sources (name, url_protected) VALUES ($name, $link);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$link", Protect(link));
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>Forgets a calendar link and every event that came from it. Your own events are not touched.</summary>
    public void RemoveSource(long sourceId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM imported_events WHERE source_id = $id;
            DELETE FROM calendar_sources WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", sourceId);
        command.ExecuteNonQuery();
    }

    public void SetSourceError(long sourceId, string error)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE calendar_sources SET last_error = $error WHERE id = $id";
        command.Parameters.AddWithValue("$error", error);
        command.Parameters.AddWithValue("$id", sourceId);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Swaps in the freshly downloaded events for one calendar. A reminder that already fired for an
    /// event stays fired, so a refresh never shows the same toast twice; new events get the default reminder.
    /// </summary>
    public void ReplaceImportedEvents(
        long sourceId, IEnumerable<ImportedOccurrence> events, int? defaultReminderMinutes, DateTime now)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        // Remember where each existing event stood: key = uid + date + time.
        var known = new Dictionary<string, string?>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT uid, date, time, next_reminder FROM imported_events WHERE source_id = $id";
            read.Parameters.AddWithValue("$id", sourceId);
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                known.TryAdd(
                    $"{reader.GetString(0)}|{reader.GetString(1)}|{(reader.IsDBNull(2) ? string.Empty : reader.GetString(2))}",
                    reader.IsDBNull(3) ? null : reader.GetString(3));
            }
        }

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM imported_events WHERE source_id = $id";
            delete.Parameters.AddWithValue("$id", sourceId);
            delete.ExecuteNonQuery();
        }

        foreach (var item in events)
        {
            var date = FormatDate(item.Date);
            var time = item.Time is { } t ? t.ToString(TimeFormat, CultureInfo.InvariantCulture) : string.Empty;
            string? next;
            if (known.TryGetValue($"{item.Uid}|{date}|{time}", out var previous))
            {
                next = previous;
            }
            else
            {
                var due = CalendarEvent.ComputeNextReminder(item.Date, item.Time, defaultReminderMinutes, now);
                next = due is { } d ? FormatDateTime(d) : null;
            }

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO imported_events (source_id, uid, date, time, title, location, next_reminder)
                VALUES ($source, $uid, $date, $time, $title, $location, $next)
                """;
            insert.Parameters.AddWithValue("$source", sourceId);
            insert.Parameters.AddWithValue("$uid", item.Uid);
            insert.Parameters.AddWithValue("$date", date);
            insert.Parameters.AddWithValue("$time", time.Length == 0 ? DBNull.Value : time);
            insert.Parameters.AddWithValue("$title", item.Title);
            insert.Parameters.AddWithValue("$location", (object?)item.Location ?? DBNull.Value);
            insert.Parameters.AddWithValue("$next", (object?)next ?? DBNull.Value);
            insert.ExecuteNonQuery();
        }

        using (var done = connection.CreateCommand())
        {
            done.Transaction = transaction;
            done.CommandText = "UPDATE calendar_sources SET last_synced = $now, last_error = NULL WHERE id = $id";
            done.Parameters.AddWithValue("$now", FormatDateTime(now));
            done.Parameters.AddWithValue("$id", sourceId);
            done.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(check.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version >= SchemaVersion)
        {
            return;
        }

        // Each step upgrades from one version to the next; existing rows are kept.
        using var transaction = connection.BeginTransaction();
        using var upgrade = connection.CreateCommand();
        upgrade.Transaction = transaction;
        var steps = new List<string>();
        if (version < 1)
        {
            steps.Add(
                """
                CREATE TABLE IF NOT EXISTS events (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    date        TEXT NOT NULL,
                    time        TEXT NULL,
                    title       TEXT NOT NULL,
                    note        TEXT NULL,
                    created_at  TEXT NOT NULL,
                    updated_at  TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_events_date ON events (date);
                """);
        }

        if (version < 2)
        {
            steps.Add(
                """
                ALTER TABLE events ADD COLUMN reminder_minutes INTEGER NULL;
                ALTER TABLE events ADD COLUMN next_reminder TEXT NULL;
                CREATE INDEX IF NOT EXISTS ix_events_next_reminder ON events (next_reminder) WHERE next_reminder IS NOT NULL;
                """);
        }

        if (version < 3)
        {
            steps.Add(
                """
                CREATE TABLE IF NOT EXISTS checklist_items (
                    id        INTEGER PRIMARY KEY AUTOINCREMENT,
                    event_id  INTEGER NOT NULL,
                    text      TEXT NOT NULL,
                    done      INTEGER NOT NULL DEFAULT 0,
                    position  INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_checklist_items_event ON checklist_items (event_id, position);
                """);
        }

        if (version < 4)
        {
            steps.Add(
                """
                CREATE TABLE IF NOT EXISTS calendar_sources (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    name           TEXT NOT NULL,
                    url_protected  BLOB NOT NULL,
                    last_synced    TEXT NULL,
                    last_error     TEXT NULL
                );
                CREATE TABLE IF NOT EXISTS imported_events (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    source_id      INTEGER NOT NULL,
                    uid            TEXT NOT NULL,
                    date           TEXT NOT NULL,
                    time           TEXT NULL,
                    title          TEXT NOT NULL,
                    location       TEXT NULL,
                    next_reminder  TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_imported_events_date ON imported_events (date);
                CREATE INDEX IF NOT EXISTS ix_imported_events_source ON imported_events (source_id);
                CREATE INDEX IF NOT EXISTS ix_imported_events_reminder ON imported_events (next_reminder) WHERE next_reminder IS NOT NULL;
                """);
        }

        steps.Add($"PRAGMA user_version = {SchemaVersion};");
        upgrade.CommandText = string.Join(Environment.NewLine, steps);
        upgrade.ExecuteNonQuery();
        transaction.Commit();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void AddFields(SqliteCommand command, CalendarEvent calendarEvent)
    {
        command.Parameters.AddWithValue("$date", FormatDate(calendarEvent.Date));
        command.Parameters.AddWithValue(
            "$time",
            calendarEvent.Time is { } time ? time.ToString(TimeFormat, CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("$title", calendarEvent.Title);
        command.Parameters.AddWithValue("$note", (object?)calendarEvent.Note ?? DBNull.Value);
        command.Parameters.AddWithValue("$reminderMinutes", (object?)calendarEvent.ReminderMinutes ?? DBNull.Value);
        var next = CalendarEvent.ComputeNextReminder(
            calendarEvent.Date, calendarEvent.Time, calendarEvent.ReminderMinutes, DateTime.Now);
        command.Parameters.AddWithValue("$nextReminder", next is { } value ? FormatDateTime(value) : DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    }

    private static string FormatDateTime(DateTime value) => value.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    private static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
