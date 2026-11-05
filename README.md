# Tidy Yard

A Windows desktop app that keeps your desktop tidy. It puts a calendar widget on the wallpaper and groups your desktop files into "fences", which are boxes that show the contents of a folder.

Runs on Windows 10 and 11. Windows 11 gets the acrylic glass look.

## What it does

**Calendar widget**
- Month, week and day views, with a day card for quick add and the agenda
- Quick add understands things like `Dentist 3pm` or `Pay rent tomorrow 9:30`
- Repeating events (every day, week, month or year), with "Skip this day" for a single exception
- Reminders as Windows toast notifications, with snooze
- Checklists on events
- Read-only calendar links: show events from Google Calendar, Outlook or iCloud by pasting the calendar's secret iCal link. Repeating events and time zones are handled. The link is stored encrypted for your Windows account, and the PC reads it directly, with no server in between
- Three themes (Paper, Night Shift, Moss), plus your own accent colour and background tint
- Resizable, and can be locked in place so it sits behind your windows

**Fences**
- Boxes on the desktop backed by real folders
- Drag files in and out
- Tidy Desktop sorts loose files into fences by type. It shows you the plan first, logs every move, and can undo the last one
- Files are only ever moved, never deleted or overwritten

## Your data

Everything lives in `%APPDATA%\TidyYard`:

| File | Contents |
| --- | --- |
| `tidyyard.db` | Events, reminders and checklists (SQLite) |
| `settings.json` | Settings and window positions |
| `fences.json` | Fence layout |
| `moves.log` | Every file move made by fences or cleanup |
| `error.log` | Errors, if any |

Updates replace the program files only. Before the database layout changes, a copy is saved as `tidyyard.db.backup-vN` next to it.

## Building

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```
dotnet build
dotnet run
```

## Making a release

Installer and updates use [Velopack](https://velopack.io). The `vpk` tool is pinned in `.config/dotnet-tools.json`.

```
.\release.ps1 -Version 1.0.1
```

This writes the installer and update files to `releases\`. Upload that folder's contents to wherever updates are hosted, and set the address in `UpdateService.DefaultFeed`. The version has to go up with every release.

## Notes

- The app is a tray app. Closing the calendar leaves it running; use Quit in the tray menu to exit.
- Desktop pinning uses the WorkerW window, so it can behave differently on unusual shell replacements.
- Inter is bundled under the SIL Open Font License (`Assets/Fonts/Inter-OFL.txt`).
