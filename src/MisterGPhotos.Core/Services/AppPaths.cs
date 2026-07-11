namespace MisterGPhotos.Core.Services;

/// <summary>Locations of the application's local data (under %APPDATA%).</summary>
public static class AppPaths
{
    public const string AppFolderName = "MisterGPhotos";

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

    public static string DatabasePath => Path.Combine(DataDirectory, "app.db");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static string LogFilePath =>
        Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
}
