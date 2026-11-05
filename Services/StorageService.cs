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
    private const int SchemaVersion = 5;
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
        // Single events inside the window, plus every repeating event that has started by the end of it.
        command.CommandText =
            $"""
            SELECT {Columns}
            FROM events
            WHERE (repeat = 0 AND date BETWEEN $from AND $to) OR (repeat <> 0 AND date <= $to)
            ORDER BY date, time IS NOT NULL, time, id
            """;
        command.Parameters.AddWithValue("$from", FormatDate(from));
        command.Parameters.AddWithValue("$to", FormatDate(to));
        var own = ExpandRepeats(connection, ReadEvents(command), from, to);

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

    private const string Columns = "id, date, time, title, note, reminder_minutes, next_reminder, repeat";

    /// <summary>Same column order as <see cref="Columns"/> (never repeating), plus the calendar's name; the id is negated.</summary>
    private const string ImportedColumns = "-e.id, e.date, e.time, e.title, e.location, NULL, e.next_reminder, 0, s.name";

    /// <summary>
    /// Turns each repeating event into one copy per day it falls on inside the window. The copies share the
    /// event's id; <see cref="CalendarEvent.SeriesStart"/> keeps the day the series began.
    /// </summary>
    private static List<CalendarEvent> ExpandRepeats(
        SqliteConnection connection, List<CalendarEvent> events, DateOnly from, DateOnly to)
    {
        var repeating = events.Where(e => e.Repeat != RepeatRule.None).ToList();
        if (repeating.Count == 0)
        {
            return events;
        }

        var skipped = LoadSkippedDays(connection, repeating.Select(e => e.Id));
        var result = events.Where(e => e.Repeat == RepeatRule.None).ToList();
        foreach (var series in repeating)
        {
            skipped.TryGetValue(series.Id, out var skipSet);
            foreach (var day in Recurrence.Occurrences(series.Date, series.Repeat, from, to, skipSet))
            {
                result.Add(series.OnDay(day));
            }
        }

        return result;
    }

    private static Dictionary<long, HashSet<DateOnly>> LoadSkippedDays(SqliteConnection connection, IEnumerable<long> eventIds)
    {
        var result = new Dictionary<long, HashSet<DateOnly>>();
        var ids = eventIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return result;
        }

        using var command = connection.CreateCommand();
        var names = ids.Select((id, i) => $"$s{i}").ToList();
        for (var i = 0; i < ids.Count; i++)
        {
            command.Parameters.AddWithValue(names[i], ids[i]);
        }

        command.CommandText = $"SELECT event_id, date FROM event_exceptions WHERE event_id IN ({string.Join(", ", names)})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            if (!result.TryGetValue(id, out var set))
            {
                result[id] = set = [];
            }

            set.Add(DateOnly.ParseExact(reader.GetString(1), DateFormat, CultureInfo.InvariantCulture));
        }

        return result;
    }

    /// <summary>Leaves one day of a repeating event out. The rest of the series is unchanged.</summary>
    public void SkipDay(long eventId, DateOnly day)
    {
        using var connection = Open();
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT OR IGNORE INTO event_exceptions (event_id, date) VALUES ($id, $date)";
            insert.Parameters.AddWithValue("$id", eventId);
            insert.Parameters.AddWithValue("$date", FormatDate(day));
            insert.ExecuteNonQuery();
        }

        // If that day held the next reminder, move the reminder on to the following one.
        AdvanceReminder(connection, eventId, DateTime.Now);
    }

    /// <summary>After a repeating event's reminder fired, schedules the one for its next day.</summary>
    public void AdvanceReminder(long eventId, DateTime now)
    {
        using var connection = Open();
        AdvanceReminder(connection, eventId, now);
    }

    private static void AdvanceReminder(SqliteConnection connection, long eventId, DateTime now)
    {
        using var read = connection.CreateCommand();
        read.CommandText = $"SELECT {Columns} FROM events WHERE id = $id";
        read.Parameters.AddWithValue("$id", eventId);
        var series = ReadEvents(read).FirstOrDefault();
        if (series is null)
        {
            return;
        }

        var skipped = LoadSkippedDays(connection, [eventId]);
        skipped.TryGetValue(eventId, out var skipSet);
        var next = Recurrence.NextReminder(series.Date, series.Time, series.ReminderMinutes, series.Repeat, now, skipSet);

        using var write = connection.CreateCommand();
        write.CommandText = "UPDATE events SET next_reminder = $next WHERE id = $id";
        write.Parameters.AddWithValue("$next", next is { } value ? FormatDateTime(value) : DBNull.Value);
        write.Parameters.AddWithValue("$id", eventId);
        write.ExecuteNonQuery();
    }

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
                Repeat = (RepeatRule)reader.GetInt32(7),
                IsImported = reader.FieldCount > 8,
                SourceName = reader.FieldCount > 8 ? reader.GetString(8) : null,
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
            INSERT INTO events (date, time, title, note, reminder_minutes, next_reminder, repeat, created_at, updated_at)
            VALUES ($date, $time, $title, $note, $reminderMinutes, $nextReminder, $repeat, $now, $now);
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
            -- Changing how often it repeats makes the skipped days meaningless, so they are cleared.
            DELETE FROM event_exceptions WHERE event_id = $id AND (SELECT repeat FROM events WHERE id = $id) IS NOT $repeat;

            UPDATE events
            SET date = $date, time = $time, title = $title, note = $note,
                reminder_minutes = $reminderMinutes,
                repeat = $repeat,
                -- Keep a reminder that already fired or is snoozed unless the date, time, lead time or repeat changed.
                next_reminder = CASE
                    WHEN date IS $date AND time IS $time AND reminder_minutes IS $reminderMinutes AND repeat IS $repeat THEN next_reminder
                    ELSE $nextReminder
                END,
                updated_at = $now
            WHERE id = $id
            """;
        AddFields(command, calendarEvent);
        command.Parameters.AddWithValue("$id", calendarEvent.Id);
        command.ExecuteNonQuery();

        // Pick the next reminder with this series' skipped days taken into account.
        if (calendarEvent.Repeat != RepeatRule.None)
        {
            AdvanceReminder(connection, calendarEvent.Id, DateTime.Now);
        }
    }

    public void Delete(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM checklist_checks WHERE item_id IN (SELECT id FROM checklist_items WHERE event_id = $id);
            DELETE FROM checklist_items WHERE event_id = $id;
            DELETE FROM event_exceptions WHERE event_id = $id;
            DELETE FROM events WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Checklist items for the given events, in the order the user arranged them. For a repeating event
    /// "done" is per day: ticking Monday's item leaves Tuesday's unticked.
    /// </summary>
    public IReadOnlyDictionary<long, List<ChecklistItem>> GetChecklists(IReadOnlyCollection<long> eventIds, DateOnly day)
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

        command.Parameters.AddWithValue("$day", FormatDate(day));
        command.CommandText =
            $"""
            SELECT ci.id, ci.event_id, ci.text,
                   CASE WHEN e.repeat = 0
                        THEN ci.done
                        ELSE EXISTS (SELECT 1 FROM checklist_checks k WHERE k.item_id = ci.id AND k.date = $day)
                   END
            FROM checklist_items ci JOIN events e ON e.id = ci.event_id
            WHERE ci.event_id IN ({string.Join(", ", names)})
            ORDER BY ci.event_id, ci.position, ci.id
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

    /// <summary>
    /// Saves an event's checklist as the user arranged it. Items keep their identity (so a repeating
    /// event's ticks on other days survive), new ones are added, and removed ones are deleted.
    /// <paramref name="day"/> is the day whose ticks <paramref name="items"/> describe.
    /// </summary>
    public void SaveChecklist(long eventId, IEnumerable<ChecklistItem> items, DateOnly day)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        var repeats = Scalar<long>(connection, transaction, "SELECT repeat FROM events WHERE id = $e", ("$e", eventId)) != 0;
        var list = items.ToList();
        var keep = list.Where(i => i.Id != 0).Select(i => i.Id).ToHashSet();

        // Remove items the user took out of the list, with their per-day ticks.
        var existing = new List<long>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT id FROM checklist_items WHERE event_id = $e";
            read.Parameters.AddWithValue("$e", eventId);
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                existing.Add(reader.GetInt64(0));
            }
        }

        foreach (var id in existing.Where(id => !keep.Contains(id)))
        {
            Run(connection, transaction, "DELETE FROM checklist_checks WHERE item_id = $i; DELETE FROM checklist_items WHERE id = $i", ("$i", id));
        }

        var position = 0;
        foreach (var item in list)
        {
            var isExisting = item.Id != 0 && existing.Contains(item.Id);
            if (isExisting)
            {
                Run(connection, transaction,
                    "UPDATE checklist_items SET text = $t, position = $p WHERE id = $i",
                    ("$t", item.Text), ("$p", position), ("$i", item.Id));
            }
            else
            {
                item.Id = Scalar<long>(connection, transaction,
                    "INSERT INTO checklist_items (event_id, text, done, position) VALUES ($e, $t, 0, $p); SELECT last_insert_rowid()",
                    ("$e", eventId), ("$t", item.Text), ("$p", position));
            }

            WriteDone(connection, transaction, item.Id, item.IsDone, day, repeats);
            position++;
        }

        transaction.Commit();
    }

    /// <summary>Ticks or unticks one item straight from the agenda, for the given day.</summary>
    public void SetItemDone(long itemId, bool done, DateOnly day)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var repeats = Scalar<long>(connection, transaction,
            "SELECT e.repeat FROM checklist_items ci JOIN events e ON e.id = ci.event_id WHERE ci.id = $i", ("$i", itemId)) != 0;
        WriteDone(connection, transaction, itemId, done, day, repeats);
        transaction.Commit();
    }

    /// <summary>A single event stores "done" on the item; a repeating one stores a tick per day.</summary>
    private static void WriteDone(
        SqliteConnection connection, SqliteTransaction transaction, long itemId, bool done, DateOnly day, bool repeats)
    {
        if (!repeats)
        {
            Run(connection, transaction, "UPDATE checklist_items SET done = $d WHERE id = $i", ("$d", done ? 1 : 0), ("$i", itemId));
            return;
        }

        Run(connection, transaction, "DELETE FROM checklist_checks WHERE item_id = $i AND date = $day", ("$i", itemId), ("$day", FormatDate(day)));
        if (done)
        {
            Run(connection, transaction, "INSERT INTO checklist_checks (item_id, date) VALUES ($i, $day)", ("$i", itemId), ("$day", FormatDate(day)));
        }
    }

    private static void Run(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] args)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in args)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    private static T Scalar<T>(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] args)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in args)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = command.ExecuteScalar();
        return result is null or DBNull ? default! : (T)Convert.ChangeType(result, typeof(T), CultureInfo.InvariantCulture);
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

        if (version < 5)
        {
            steps.Add(
                """
                ALTER TABLE events ADD COLUMN repeat INTEGER NOT NULL DEFAULT 0;
                CREATE TABLE IF NOT EXISTS event_exceptions (
                    event_id  INTEGER NOT NULL,
                    date      TEXT NOT NULL,
                    PRIMARY KEY (event_id, date)
                );
                CREATE TABLE IF NOT EXISTS checklist_checks (
                    item_id   INTEGER NOT NULL,
                    date      TEXT NOT NULL,
                    PRIMARY KEY (item_id, date)
                );
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
        // A repeating event is stored under the day it began, not the day it was opened from.
        var startDay = calendarEvent.SeriesStart ?? calendarEvent.Date;
        command.Parameters.AddWithValue("$date", FormatDate(startDay));
        command.Parameters.AddWithValue(
            "$time",
            calendarEvent.Time is { } time ? time.ToString(TimeFormat, CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("$title", calendarEvent.Title);
        command.Parameters.AddWithValue("$note", (object?)calendarEvent.Note ?? DBNull.Value);
        command.Parameters.AddWithValue("$reminderMinutes", (object?)calendarEvent.ReminderMinutes ?? DBNull.Value);
        command.Parameters.AddWithValue("$repeat", (int)calendarEvent.Repeat);
        var next = Recurrence.NextReminder(
            startDay, calendarEvent.Time, calendarEvent.ReminderMinutes, calendarEvent.Repeat, DateTime.Now);
        command.Parameters.AddWithValue("$nextReminder", next is { } value ? FormatDateTime(value) : DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    }

    private static string FormatDateTime(DateTime value) => value.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    private static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
