using GPhotosUploader.Core.Models;

namespace GPhotosUploader.Core.Data;

/// <summary>Connected Google account (google_account table, single row id=1).</summary>
public class AccountRepository
{
    private readonly Database _db;

    public AccountRepository(Database db) => _db = db;

    public GoogleAccount? Get()
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT email, display_name, connected_at, scopes FROM google_account WHERE id = 1";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new GoogleAccount
        {
            Email = reader.IsDBNull(0) ? null : reader.GetString(0),
            DisplayName = reader.IsDBNull(1) ? null : reader.GetString(1),
            ConnectedAt = Database.FromDbDate(reader[2]),
            Scopes = reader.IsDBNull(3) ? null : reader.GetString(3)
        };
    }

    public void Save(GoogleAccount account)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO google_account (id, email, display_name, connected_at, scopes)
            VALUES (1, @email, @name, @at, @scopes)
            ON CONFLICT(id) DO UPDATE SET email = @email, display_name = @name, connected_at = @at, scopes = @scopes
            """;
        cmd.Parameters.AddWithValue("@email", (object?)account.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@name", (object?)account.DisplayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@at", account.ConnectedAt is null ? DBNull.Value : Database.ToDbDate(account.ConnectedAt.Value));
        cmd.Parameters.AddWithValue("@scopes", (object?)account.Scopes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Delete()
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM google_account WHERE id = 1";
        cmd.ExecuteNonQuery();
    }
}
