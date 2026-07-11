using GPhotosUploader.Core.Models;
using Microsoft.Data.Sqlite;

namespace GPhotosUploader.Core.Data;

/// <summary>Access to the media_files table: local inventory and upload state machine.</summary>
public class MediaFileRepository
{
    private readonly Database _db;

    public MediaFileRepository(Database db) => _db = db;

    public MediaFile? GetByPath(string localPath)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM media_files WHERE local_path = @p";
        cmd.Parameters.AddWithValue("@p", localPath);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public MediaFile? GetById(long id)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM media_files WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public long Insert(MediaFile f)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO media_files
                (local_path, file_name, extension, file_size, sha256_hash, created_at, modified_at,
                 scan_status, upload_status, google_media_item_id, upload_token, upload_token_at,
                 retry_count, last_error, first_seen_at, last_seen_at, uploaded_at)
            VALUES
                (@local_path, @file_name, @extension, @file_size, @sha256_hash, @created_at, @modified_at,
                 @scan_status, @upload_status, @google_media_item_id, @upload_token, @upload_token_at,
                 @retry_count, @last_error, @first_seen_at, @last_seen_at, @uploaded_at);
            SELECT last_insert_rowid();
            """;
        BindAll(cmd, f);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void Update(MediaFile f)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE media_files SET
                local_path = @local_path, file_name = @file_name, extension = @extension,
                file_size = @file_size, sha256_hash = @sha256_hash, created_at = @created_at,
                modified_at = @modified_at, scan_status = @scan_status, upload_status = @upload_status,
                google_media_item_id = @google_media_item_id, upload_token = @upload_token,
                upload_token_at = @upload_token_at, retry_count = @retry_count, last_error = @last_error,
                first_seen_at = @first_seen_at, last_seen_at = @last_seen_at, uploaded_at = @uploaded_at
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", f.Id);
        BindAll(cmd, f);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Files to upload: queued first, then paused, then retryable failed ones.</summary>
    public List<MediaFile> GetNextForUpload(int limit, int maxRetries, int offset = 0)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM media_files
            WHERE upload_status = 'queued'
               OR upload_status = 'paused'
               OR (upload_status = 'failed' AND retry_count < @maxRetries)
            ORDER BY CASE upload_status WHEN 'queued' THEN 0 WHEN 'paused' THEN 1 ELSE 2 END, id
            LIMIT @limit OFFSET @offset
            """;
        cmd.Parameters.AddWithValue("@maxRetries", maxRetries);
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);
        return MapList(cmd);
    }

    /// <summary>Looks for a file already uploaded by this application with the same hash.</summary>
    public MediaFile? FindUploadedByHash(string sha256, long excludeId)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM media_files
            WHERE sha256_hash = @h AND upload_status = 'uploaded' AND id != @id
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@h", sha256);
        cmd.Parameters.AddWithValue("@id", excludeId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>
    /// Looks for another local file sharing the same hash (local duplicate).
    /// Only "live" rows (seen again in the last scan) can serve as the
    /// canonical one: a vanished file does not block the upload of its moved copy.
    /// </summary>
    public MediaFile? FindLocalDuplicate(string sha256, long excludeId)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM media_files
            WHERE sha256_hash = @h AND id != @id
              AND scan_status = 'scanned'
              AND upload_status IN ('discovered', 'queued', 'uploading', 'uploaded', 'paused', 'failed')
            ORDER BY id
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@h", sha256);
        cmd.Parameters.AddWithValue("@id", excludeId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>
    /// Recovery after a crash: any file left in 'uploading' is reset to 'queued'.
    /// The upload token, if any, is kept: if it is still fresh (less than 20 h),
    /// it will be reused without resending the bytes.
    /// </summary>
    public int RequeueInterrupted()
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE media_files SET upload_status = 'queued' WHERE upload_status = 'uploading'";
        return cmd.ExecuteNonQuery();
    }

    /// <summary>On shutdown: files still 'uploading' switch to 'paused' for later resumption.</summary>
    public int MarkUploadingAsPaused()
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE media_files SET upload_status = 'paused' WHERE upload_status = 'uploading'";
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Re-queues files marked 'paused' (Resume button / restart).</summary>
    public int RequeuePaused()
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE media_files SET upload_status = 'queued' WHERE upload_status = 'paused'";
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Resets the retry counter of failed files to relaunch them.</summary>
    public int ResetFailed()
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE media_files SET retry_count = 0, upload_status = 'queued', last_error = NULL WHERE upload_status = 'failed'";
        return cmd.ExecuteNonQuery();
    }

    public Dictionary<UploadStatus, int> CountByStatus()
    {
        var result = new Dictionary<UploadStatus, int>();
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT upload_status, COUNT(*) FROM media_files GROUP BY upload_status";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result[StatusMapper.UploadStatusFromDb(reader.GetString(0))] = reader.GetInt32(1);
        return result;
    }

    /// <summary>Sum of bytes remaining to upload (for the estimated time remaining).</summary>
    public long PendingBytes(int maxRetries)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(SUM(file_size), 0) FROM media_files
            WHERE upload_status IN ('queued', 'paused', 'uploading')
               OR (upload_status = 'failed' AND retry_count < @maxRetries)
            """;
        cmd.Parameters.AddWithValue("@maxRetries", maxRetries);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>Paginated list for the Details view, filterable by status.</summary>
    public List<MediaFile> List(UploadStatus? status, int limit, int offset)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        if (status is null)
        {
            cmd.CommandText = "SELECT * FROM media_files ORDER BY id LIMIT @limit OFFSET @offset";
        }
        else
        {
            cmd.CommandText = "SELECT * FROM media_files WHERE upload_status = @s ORDER BY id LIMIT @limit OFFSET @offset";
            cmd.Parameters.AddWithValue("@s", status.Value.ToDb());
        }
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);
        return MapList(cmd);
    }

    public int CountAll()
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM media_files";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Refreshes last_seen_at/scan_status for a batch of unchanged files, in a
    /// single transaction (avoids a synchronous commit per file during rescans).
    /// </summary>
    public void TouchLastSeen(IReadOnlyList<long> ids, DateTime seenAtUtc)
    {
        if (ids.Count == 0) return;
        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();
        const int chunkSize = 500;
        for (int start = 0; start < ids.Count; start += chunkSize)
        {
            var chunk = ids.Skip(start).Take(chunkSize).ToList();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            var placeholders = string.Join(',', chunk.Select((_, i) => $"@id{i}"));
            cmd.CommandText =
                $"UPDATE media_files SET last_seen_at = @seen, scan_status = 'scanned' WHERE id IN ({placeholders})";
            cmd.Parameters.AddWithValue("@seen", Database.ToDbDate(seenAtUtc));
            for (int i = 0; i < chunk.Count; i++)
                cmd.Parameters.AddWithValue($"@id{i}", chunk[i]);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Marks as 'missing' the files under the root not seen again by the current scan.</summary>
    public int MarkMissingUnderRoot(string rootPrefix, DateTime scanStartedUtc)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE media_files SET scan_status = 'missing'
            WHERE local_path LIKE @prefix ESCAPE '\'
              AND last_seen_at < @scanStart
              AND upload_status != 'uploaded'
            """;
        cmd.Parameters.AddWithValue("@prefix", EscapeLike(rootPrefix) + "%");
        cmd.Parameters.AddWithValue("@scanStart", Database.ToDbDate(scanStartedUtc));
        return cmd.ExecuteNonQuery();
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static void BindAll(SqliteCommand cmd, MediaFile f)
    {
        cmd.Parameters.AddWithValue("@local_path", f.LocalPath);
        cmd.Parameters.AddWithValue("@file_name", f.FileName);
        cmd.Parameters.AddWithValue("@extension", f.Extension);
        cmd.Parameters.AddWithValue("@file_size", f.FileSize);
        cmd.Parameters.AddWithValue("@sha256_hash", (object?)f.Sha256Hash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at", f.CreatedAt is null ? DBNull.Value : Database.ToDbDate(f.CreatedAt.Value));
        cmd.Parameters.AddWithValue("@modified_at", f.ModifiedAt is null ? DBNull.Value : Database.ToDbDate(f.ModifiedAt.Value));
        cmd.Parameters.AddWithValue("@scan_status", f.ScanStatus.ToDb());
        cmd.Parameters.AddWithValue("@upload_status", f.UploadStatus.ToDb());
        cmd.Parameters.AddWithValue("@google_media_item_id", (object?)f.GoogleMediaItemId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@upload_token", (object?)f.UploadToken ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@upload_token_at", f.UploadTokenAt is null ? DBNull.Value : Database.ToDbDate(f.UploadTokenAt.Value));
        cmd.Parameters.AddWithValue("@retry_count", f.RetryCount);
        cmd.Parameters.AddWithValue("@last_error", (object?)f.LastError ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@first_seen_at", Database.ToDbDate(f.FirstSeenAt));
        cmd.Parameters.AddWithValue("@last_seen_at", Database.ToDbDate(f.LastSeenAt));
        cmd.Parameters.AddWithValue("@uploaded_at", f.UploadedAt is null ? DBNull.Value : Database.ToDbDate(f.UploadedAt.Value));
    }

    private static List<MediaFile> MapList(SqliteCommand cmd)
    {
        var list = new List<MediaFile>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(Map(reader));
        return list;
    }

    private static MediaFile Map(SqliteDataReader r)
    {
        return new MediaFile
        {
            Id = r.GetInt64(r.GetOrdinal("id")),
            LocalPath = r.GetString(r.GetOrdinal("local_path")),
            FileName = r.GetString(r.GetOrdinal("file_name")),
            Extension = r.GetString(r.GetOrdinal("extension")),
            FileSize = r.GetInt64(r.GetOrdinal("file_size")),
            Sha256Hash = r.IsDBNull(r.GetOrdinal("sha256_hash")) ? null : r.GetString(r.GetOrdinal("sha256_hash")),
            CreatedAt = Database.FromDbDate(r["created_at"]),
            ModifiedAt = Database.FromDbDate(r["modified_at"]),
            ScanStatus = StatusMapper.ScanStatusFromDb(r.GetString(r.GetOrdinal("scan_status"))),
            UploadStatus = StatusMapper.UploadStatusFromDb(r.GetString(r.GetOrdinal("upload_status"))),
            GoogleMediaItemId = r.IsDBNull(r.GetOrdinal("google_media_item_id")) ? null : r.GetString(r.GetOrdinal("google_media_item_id")),
            UploadToken = r.IsDBNull(r.GetOrdinal("upload_token")) ? null : r.GetString(r.GetOrdinal("upload_token")),
            UploadTokenAt = Database.FromDbDate(r["upload_token_at"]),
            RetryCount = r.GetInt32(r.GetOrdinal("retry_count")),
            LastError = r.IsDBNull(r.GetOrdinal("last_error")) ? null : r.GetString(r.GetOrdinal("last_error")),
            FirstSeenAt = Database.FromDbDate(r["first_seen_at"]) ?? DateTime.MinValue,
            LastSeenAt = Database.FromDbDate(r["last_seen_at"]) ?? DateTime.MinValue,
            UploadedAt = Database.FromDbDate(r["uploaded_at"])
        };
    }
}
