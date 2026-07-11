using GPhotosUploader.Core.Models;

namespace GPhotosUploader.Core.Data;

/// <summary>Table app_logs : journal persistant (en plus du fichier texte).</summary>
public class LogRepository
{
    private readonly Database _db;

    public LogRepository(Database db) => _db = db;

    public void Add(AppLogLevel level, string? source, string message)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO app_logs (timestamp, level, source, message) VALUES (@t, @l, @s, @m)";
        cmd.Parameters.AddWithValue("@t", Database.ToDbDate(DateTime.UtcNow));
        cmd.Parameters.AddWithValue("@l", level.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@s", (object?)source ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@m", message);
        cmd.ExecuteNonQuery();
    }

    public List<LogEntry> ListRecent(int limit)
    {
        var list = new List<LogEntry>();
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, timestamp, level, source, message FROM app_logs ORDER BY id DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new LogEntry
            {
                Id = reader.GetInt64(0),
                Timestamp = Database.FromDbDate(reader[1]) ?? DateTime.MinValue,
                Level = Enum.TryParse<AppLogLevel>(reader.GetString(2), true, out var lvl) ? lvl : AppLogLevel.Info,
                Source = reader.IsDBNull(3) ? null : reader.GetString(3),
                Message = reader.GetString(4)
            });
        }
        return list;
    }

    /// <summary>Purge les entrées plus anciennes que le nombre de jours donné.</summary>
    public int Purge(int keepDays)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM app_logs WHERE timestamp < @limit";
        cmd.Parameters.AddWithValue("@limit", Database.ToDbDate(DateTime.UtcNow.AddDays(-keepDays)));
        return cmd.ExecuteNonQuery();
    }
}
