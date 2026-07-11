using Microsoft.Data.Sqlite;

namespace GPhotosUploader.Core.Data;

/// <summary>
/// Point d'accès SQLite : chemin de la base, ouverture de connexions et migrations.
/// La base est ouverte en mode WAL pour survivre aux arrêts brutaux, avec un
/// busy_timeout pour tolérer les accès concurrents (scan + upload + UI).
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
            // BeginTransaction (Serializable -> BEGIN IMMEDIATE) prend le verrou d'écriture ;
            // on revérifie la version à l'intérieur au cas où un autre processus vient
            // d'appliquer la même migration.
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

    /// <summary>Format ISO 8601 UTC utilisé pour toutes les dates en base.</summary>
    public static string ToDbDate(DateTime dt) => dt.ToUniversalTime().ToString("o");

    public static DateTime? FromDbDate(object? value)
    {
        if (value is null || value is DBNull) return null;
        var s = Convert.ToString(value);
        if (string.IsNullOrEmpty(s)) return null;
        return DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind);
    }
}
