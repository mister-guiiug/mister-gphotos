namespace GPhotosUploader.Core.Models;

/// <summary>Application settings (settings table, key/value).</summary>
public class AppSettings
{
    public const int MinBatchSize = 1;
    /// <summary>Hard limit imposed by mediaItems:batchCreate (50 items max per call).</summary>
    public const int MaxBatchSize = 50;
    public const int MinConcurrency = 1;
    public const int MaxConcurrency = 3;

    public string RootFolder { get; set; } = "";

    /// <summary>Number of files per batch (byte uploads + one batchCreate call).</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Maximum number of attempts for a file with a temporary error.</summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>Number of concurrent uploads (1 to 3).</summary>
    public int Concurrency { get; set; } = 2;

    /// <summary>Maximum size accepted for a photo, in MB (Google Photos limit: 200 MB).</summary>
    public int MaxFileSizeMb { get; set; } = 200;

    /// <summary>Included extensions, comma-separated, without a dot, in lowercase.</summary>
    public string IncludedExtensions { get; set; } = DefaultExtensions;

    /// <summary>The user's OAuth Client ID (created in the Google Cloud Console, "Desktop app" type).</summary>
    public string OAuthClientId { get; set; } = "";

    public const string DefaultExtensions =
        "jpg,jpeg,png,webp,heic,heif,gif,tif,tiff,bmp,avif,ico," +
        "dng,cr2,cr3,crw,nef,nrw,arw,orf,raf,rw2,srw,pef,srf,sr2";

    public IReadOnlySet<string> ExtensionSet()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in IncludedExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            set.Add(ext.TrimStart('.'));
        return set;
    }

    public long MaxFileSizeBytes => (long)MaxFileSizeMb * 1024 * 1024;

    /// <summary>
    /// Immutable copy for the duration of a scan or an upload run: worker threads
    /// must never see the changes made in the Settings screen.
    /// </summary>
    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Brings the values back within safe bounds.</summary>
    public void Clamp()
    {
        BatchSize = Math.Clamp(BatchSize, MinBatchSize, MaxBatchSize);
        Concurrency = Math.Clamp(Concurrency, MinConcurrency, MaxConcurrency);
        MaxRetries = Math.Clamp(MaxRetries, 0, 20);
        MaxFileSizeMb = Math.Clamp(MaxFileSizeMb, 1, 200);
        if (string.IsNullOrWhiteSpace(IncludedExtensions))
            IncludedExtensions = DefaultExtensions;
    }
}
