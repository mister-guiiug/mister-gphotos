using Microsoft.Data.Sqlite;

namespace MisterGPhotos.Core.Data;

/// <summary>
/// SQLite access point: database path, opening connections and migrations.
/// The database is opened in WAL mode to survive abrupt shutdowns, with a
/// busy_timeout to tolerate concurrent access (scan + upload + UI).
/// </summary>
public class Database
{
    public string DbPath { get; }

    public Database(string dbPath)
    {
        DbPath = dbPath;
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        Migrate();
    }

    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    private void Migrate()
    {
        using var conn = OpenConnection();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL)";
            cmd.ExecuteNonQuery();
        }

        int current = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version";
            current = Convert.ToInt32(cmd.ExecuteScalar());
        }

        foreach (var (version, script) in Migrations.All)
        {
            if (version <= current) continue;
            // BeginTransaction (Serializable -> BEGIN IMMEDIATE) takes the write lock;
            // we re-check the version inside in case another process has just
            // applied the same migration.
            using var tx = conn.BeginTransaction();
            using (var check = conn.CreateCommand())
            {
                check.Transaction = tx;
                check.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version";
                if (Convert.ToInt32(check.ExecuteScalar()) >= version)
                {
                    tx.Rollback();
                    continue;
                }
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = script;
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO schema_version (version) VALUES (@v)";
                cmd.Parameters.AddWithValue("@v", version);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    /// <summary>ISO 8601 UTC format used for all dates in the database.</summary>
    public static string ToDbDate(DateTime dt) => dt.ToUniversalTime().ToString("o");

    public static DateTime? FromDbDate(object? value)
    {
        if (value is null || value is DBNull) return null;
        var s = Convert.ToString(value);
        if (string.IsNullOrEmpty(s)) return null;
        return DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind);
    }
}
