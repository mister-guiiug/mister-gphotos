using MisterGPhotos.Core.Models;
using MisterGPhotos.Core.Resources;

namespace MisterGPhotos.Core.Services;

/// <summary>Result of a file's compatibility check.</summary>
public record CompatibilityResult(bool IsCompatible, string? Reason);

/// <summary>
/// Checks that a file is acceptable for Google Photos:
/// extension included in the settings and size under the limit (200 MB for photos).
/// The extension list is configurable; adding a new one requires
/// no code modification.
/// </summary>
public class CompatibilityChecker
{
    private readonly IReadOnlySet<string> _extensions;
    private readonly long _maxSizeBytes;
    private readonly int _maxSizeMb;

    public CompatibilityChecker(AppSettings settings)
    {
        _extensions = settings.ExtensionSet();
        _maxSizeBytes = settings.MaxFileSizeBytes;
        _maxSizeMb = settings.MaxFileSizeMb;
    }

    /// <summary>Fast filter on the extension, used during the scan enumeration.</summary>
    public bool HasSupportedExtension(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.');
        return ext.Length > 0 && _extensions.Contains(ext);
    }

    public CompatibilityResult Check(string extension, long fileSize)
    {
        if (!_extensions.Contains(extension.TrimStart('.')))
            return new CompatibilityResult(false, Loc.TF("Compat_ExtensionNotSupported", extension));
        if (fileSize <= 0)
            return new CompatibilityResult(false, Loc.T("Compat_EmptyFile"));
        if (fileSize > _maxSizeBytes)
            return new CompatibilityResult(false,
                Loc.TF("Compat_TooLarge", fileSize / (1024 * 1024), _maxSizeMb));
        return new CompatibilityResult(true, null);
    }

    /// <summary>MIME type passed to the Google Photos upload endpoint.</summary>
    public static string MimeTypeFor(string extension) => extension.TrimStart('.').ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => "image/jpeg",
        "png" => "image/png",
        "webp" => "image/webp",
        "heic" or "heif" => "image/heic",
        "gif" => "image/gif",
        "tif" or "tiff" => "image/tiff",
        "bmp" => "image/bmp",
        "avif" => "image/avif",
        "ico" => "image/x-icon",
        _ => "application/octet-stream"
    };
}
