namespace GPhotosUploader.Core.Models;

/// <summary>Un batch d'upload (table upload_batches).</summary>
public class UploadBatch
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int FileCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public string Status { get; set; } = "running";
}

/// <summary>Une tentative d'upload individuelle (table upload_attempts).</summary>
public class UploadAttempt
{
    public long Id { get; set; }
    public long MediaFileId { get; set; }
    public long? BatchId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Outcome { get; set; }
    public string? Error { get; set; }
}

/// <summary>Une entrée de journal (table app_logs).</summary>
public class LogEntry
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public AppLogLevel Level { get; set; }
    public string? Source { get; set; }
    public string Message { get; set; } = "";
}
