using System.IO;

namespace TidyYard.Services;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TidyYard");

    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");

    public static string DatabaseFile { get; } = Path.Combine(DataDirectory, "tidyyard.db");

    public static string ErrorLogFile { get; } = Path.Combine(DataDirectory, "error.log");

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);
}
