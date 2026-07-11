namespace GPhotosUploader.Core.Models;

/// <summary>An upload batch (upload_batches table).</summary>
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

/// <summary>An individual upload attempt (upload_attempts table).</summary>
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

/// <summary>A log entry (app_logs table).</summary>
public class LogEntry
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public AppLogLevel Level { get; set; }
    public string? Source { get; set; }
    public string Message { get; set; } = "";
}
