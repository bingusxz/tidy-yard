using System.Globalization;
using Microsoft.Data.Sqlite;
using TidyYard.Models;

namespace TidyYard.Services;

/// <summary>
/// SQLite storage at %APPDATA%\TidyYard\tidyyard.db.
/// </summary>
public sealed class StorageService
{
    private const int SchemaVersion = 2;
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

        EnsureSchema();
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
        return ReadEvents(command);
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
        return ReadEvents(command);
    }

    /// <summary>Sets when the next reminder for an event is shown; null clears it.</summary>
    public void SetNextReminder(long id, DateTime? next)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE events SET next_reminder = $next WHERE id = $id";
        command.Parameters.AddWithValue("$next", next is { } value ? FormatDateTime(value) : DBNull.Value);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Gets one event by id, or null if it has been deleted.</summary>
    public CalendarEvent? GetEvent(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM events WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return ReadEvents(command).FirstOrDefault();
    }

    private const string Columns = "id, date, time, title, note, reminder_minutes, next_reminder";

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
        command.CommandText = "DELETE FROM events WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
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
