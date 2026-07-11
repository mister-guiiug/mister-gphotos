using System.Globalization;
using MisterGPhotos.Core.Models;

namespace MisterGPhotos.Core.Data;

/// <summary>Application settings persisted in the settings table (key/value).</summary>
public class SettingsRepository
{
    private readonly Database _db;

    public SettingsRepository(Database db) => _db = db;

    public AppSettings Load()
    {
        var map = new Dictionary<string, string>();
        using (var conn = _db.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT key, value FROM settings";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                map[reader.GetString(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1);
        }

        var s = new AppSettings();
        if (map.TryGetValue("root_folder", out var root)) s.RootFolder = root;
        if (map.TryGetValue("batch_size", out var bs) && int.TryParse(bs, out var bsi)) s.BatchSize = bsi;
        if (map.TryGetValue("max_retries", out var mr) && int.TryParse(mr, out var mri)) s.MaxRetries = mri;
        if (map.TryGetValue("concurrency", out var cc) && int.TryParse(cc, out var cci)) s.Concurrency = cci;
        if (map.TryGetValue("max_file_size_mb", out var mfs) && int.TryParse(mfs, out var mfsi)) s.MaxFileSizeMb = mfsi;
        if (map.TryGetValue("included_extensions", out var ext) && !string.IsNullOrWhiteSpace(ext)) s.IncludedExtensions = ext;
        if (map.TryGetValue("oauth_client_id", out var cid)) s.OAuthClientId = cid;
        s.Clamp();
        return s;
    }

    public void Save(AppSettings s)
    {
        s.Clamp();
        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();
        void Set(string key, string value)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO settings (key, value) VALUES (@k, @v) ON CONFLICT(key) DO UPDATE SET value = @v";
            cmd.Parameters.AddWithValue("@k", key);
            cmd.Parameters.AddWithValue("@v", value);
            cmd.ExecuteNonQuery();
        }
        Set("root_folder", s.RootFolder);
        Set("batch_size", s.BatchSize.ToString(CultureInfo.InvariantCulture));
        Set("max_retries", s.MaxRetries.ToString(CultureInfo.InvariantCulture));
        Set("concurrency", s.Concurrency.ToString(CultureInfo.InvariantCulture));
        Set("max_file_size_mb", s.MaxFileSizeMb.ToString(CultureInfo.InvariantCulture));
        Set("included_extensions", s.IncludedExtensions);
        Set("oauth_client_id", s.OAuthClientId);
        tx.Commit();
    }
}
