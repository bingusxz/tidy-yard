using System.Globalization;
using Microsoft.Data.Sqlite;
using TidyYard.Models;

namespace TidyYard.Services;

/// <summary>
/// SQLite storage at %APPDATA%\TidyYard\tidyyard.db.
/// </summary>
public sealed class StorageService
{
    private const int SchemaVersion = 1;
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm";

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
            """
            SELECT id, date, time, title, note
            FROM events
            WHERE date BETWEEN $from AND $to
            ORDER BY date, time IS NOT NULL, time, id
            """;
        command.Parameters.AddWithValue("$from", FormatDate(from));
        command.Parameters.AddWithValue("$to", FormatDate(to));

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
            INSERT INTO events (date, time, title, note, created_at, updated_at)
            VALUES ($date, $time, $title, $note, $now, $now);
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
            SET date = $date, time = $time, title = $title, note = $note, updated_at = $now
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

        using var transaction = connection.BeginTransaction();
        using var create = connection.CreateCommand();
        create.Transaction = transaction;
        create.CommandText =
            $"""
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
            PRAGMA user_version = {SchemaVersion};
            """;
        create.ExecuteNonQuery();
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
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    }

    private static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
