namespace MisterGPhotos.Core.Models;

/// <summary>File discovery status during the scan.</summary>
public enum ScanStatus
{
    Scanned,
    Missing
}

/// <summary>Upload lifecycle status of a file.</summary>
public enum UploadStatus
{
    Discovered,
    Queued,
    Uploading,
    Uploaded,
    SkippedDuplicateLocal,
    SkippedDuplicateRemoteAppCreated,
    SkippedIncompatible,
    Failed,
    Paused
}

/// <summary>Logging level.</summary>
public enum AppLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>Conversion of enums to/from the text values stored in SQLite.</summary>
public static class StatusMapper
{
    public static string ToDb(this UploadStatus status) => status switch
    {
        UploadStatus.Discovered => "discovered",
        UploadStatus.Queued => "queued",
        UploadStatus.Uploading => "uploading",
        UploadStatus.Uploaded => "uploaded",
        UploadStatus.SkippedDuplicateLocal => "skipped_duplicate_local",
        UploadStatus.SkippedDuplicateRemoteAppCreated => "skipped_duplicate_remote_app_created",
        UploadStatus.SkippedIncompatible => "skipped_incompatible",
        UploadStatus.Failed => "failed",
        UploadStatus.Paused => "paused",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public static UploadStatus UploadStatusFromDb(string value) => value switch
    {
        "discovered" => UploadStatus.Discovered,
        "queued" => UploadStatus.Queued,
        "uploading" => UploadStatus.Uploading,
        "uploaded" => UploadStatus.Uploaded,
        "skipped_duplicate_local" => UploadStatus.SkippedDuplicateLocal,
        "skipped_duplicate_remote_app_created" => UploadStatus.SkippedDuplicateRemoteAppCreated,
        "skipped_incompatible" => UploadStatus.SkippedIncompatible,
        "failed" => UploadStatus.Failed,
        "paused" => UploadStatus.Paused,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown upload status")
    };

    public static string ToDb(this ScanStatus status) => status switch
    {
        ScanStatus.Scanned => "scanned",
        ScanStatus.Missing => "missing",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public static ScanStatus ScanStatusFromDb(string value) => value switch
    {
        "scanned" => ScanStatus.Scanned,
        "missing" => ScanStatus.Missing,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown scan status")
    };
}
