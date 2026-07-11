namespace GPhotosUploader.Core.Services;

/// <summary>Emplacements des données locales de l'application (sous %APPDATA%).</summary>
public static class AppPaths
{
    public const string AppFolderName = "GooglePhotosLocalUploader";

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

    public static string DatabasePath => Path.Combine(DataDirectory, "app.db");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static string LogFilePath =>
        Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
}
