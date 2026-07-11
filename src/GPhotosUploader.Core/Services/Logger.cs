using GPhotosUploader.Core.Data;
using GPhotosUploader.Core.Models;

namespace GPhotosUploader.Core.Services;

/// <summary>
/// Journalisation triple : fichier texte quotidien, table SQLite app_logs,
/// et événement pour l'affichage temps réel dans l'interface.
/// Aucune donnée sensible (token, secret) ne doit être passée à ce logger.
/// </summary>
public class Logger
{
    private readonly LogRepository? _repo;
    private readonly string _logDirectory;
    private readonly object _fileLock = new();

    public event Action<AppLogLevel, string>? MessageLogged;

    public Logger(LogRepository? repo, string? logDirectory = null)
    {
        _repo = repo;
        _logDirectory = logDirectory ?? AppPaths.LogDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    private string CurrentLogFile => Path.Combine(_logDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

    public void Debug(string source, string message) => Log(AppLogLevel.Debug, source, message);
    public void Info(string source, string message) => Log(AppLogLevel.Info, source, message);
    public void Warning(string source, string message) => Log(AppLogLevel.Warning, source, message);
    public void Error(string source, string message) => Log(AppLogLevel.Error, source, message);

    public void Log(AppLogLevel level, string source, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level.ToString().ToUpperInvariant()}] [{source}] {message}";
        lock (_fileLock)
        {
            try
            {
                File.AppendAllText(CurrentLogFile, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Le journal fichier ne doit jamais faire tomber l'application.
            }
        }

        if (level != AppLogLevel.Debug)
        {
            try
            {
                _repo?.Add(level, source, message);
            }
            catch
            {
                // Base occupée ou verrouillée : le fichier texte a déjà la trace.
            }
        }

        MessageLogged?.Invoke(level, $"{DateTime.Now:HH:mm:ss}  {message}");
    }

    /// <summary>Exporte les journaux récents (SQLite) vers un fichier texte choisi par l'utilisateur.</summary>
    public void ExportTo(string destinationPath, int maxEntries = 10000)
    {
        var entries = _repo?.ListRecent(maxEntries) ?? new List<LogEntry>();
        entries.Reverse();
        var lines = entries.Select(e =>
            $"{e.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss} [{e.Level.ToString().ToUpperInvariant()}] [{e.Source}] {e.Message}");
        File.WriteAllLines(destinationPath, lines);
    }
}
