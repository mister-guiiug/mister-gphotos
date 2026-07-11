namespace GPhotosUploader.Core.Models;

/// <summary>A local image indexed in SQLite (media_files table).</summary>
public class MediaFile
{
    public long Id { get; set; }
    public string LocalPath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Extension { get; set; } = "";
    public long FileSize { get; set; }
    public string? Sha256Hash { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public ScanStatus ScanStatus { get; set; } = ScanStatus.Scanned;
    public UploadStatus UploadStatus { get; set; } = UploadStatus.Discovered;
    public string? GoogleMediaItemId { get; set; }
    public string? UploadToken { get; set; }
    public DateTime? UploadTokenAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? UploadedAt { get; set; }
}
