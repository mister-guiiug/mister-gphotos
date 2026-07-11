using MisterGPhotos.Core.Models;

namespace MisterGPhotos.Core.Data;

/// <summary>upload_batches and upload_attempts tables: upload history.</summary>
public class BatchRepository
{
    private readonly Database _db;

    public BatchRepository(Database db) => _db = db;

    public long CreateBatch(int fileCount)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO upload_batches (created_at, file_count, status) VALUES (@at, @count, 'running');
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@at", Database.ToDbDate(DateTime.UtcNow));
        cmd.Parameters.AddWithValue("@count", fileCount);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void CompleteBatch(long batchId, int successCount, int failureCount, string status)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE upload_batches
            SET completed_at = @at, success_count = @s, failure_count = @f, status = @status
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@at", Database.ToDbDate(DateTime.UtcNow));
        cmd.Parameters.AddWithValue("@s", successCount);
        cmd.Parameters.AddWithValue("@f", failureCount);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@id", batchId);
        cmd.ExecuteNonQuery();
    }

    public long StartAttempt(long mediaFileId, long? batchId)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO upload_attempts (media_file_id, batch_id, started_at) VALUES (@f, @b, @at);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@f", mediaFileId);
        cmd.Parameters.AddWithValue("@b", (object?)batchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@at", Database.ToDbDate(DateTime.UtcNow));
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void FinishAttempt(long attemptId, string outcome, string? error)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE upload_attempts SET finished_at = @at, outcome = @o, error = @e WHERE id = @id";
        cmd.Parameters.AddWithValue("@at", Database.ToDbDate(DateTime.UtcNow));
        cmd.Parameters.AddWithValue("@o", outcome);
        cmd.Parameters.AddWithValue("@e", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", attemptId);
        cmd.ExecuteNonQuery();
    }

    public List<UploadBatch> ListRecent(int limit)
    {
        var list = new List<UploadBatch>();
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, created_at, completed_at, file_count, success_count, failure_count, status FROM upload_batches ORDER BY id DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new UploadBatch
            {
                Id = reader.GetInt64(0),
                CreatedAt = Database.FromDbDate(reader[1]) ?? DateTime.MinValue,
                CompletedAt = Database.FromDbDate(reader[2]),
                FileCount = reader.GetInt32(3),
                SuccessCount = reader.GetInt32(4),
                FailureCount = reader.GetInt32(5),
                Status = reader.GetString(6)
            });
        }
        return list;
    }
}
