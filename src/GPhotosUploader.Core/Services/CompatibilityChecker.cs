using GPhotosUploader.Core.Models;

namespace GPhotosUploader.Core.Services;

/// <summary>Résultat de la vérification de compatibilité d'un fichier.</summary>
public record CompatibilityResult(bool IsCompatible, string? Reason);

/// <summary>
/// Vérifie qu'un fichier est acceptable pour Google Photos :
/// extension incluse dans les paramètres et taille sous la limite (200 Mo pour les photos).
/// La liste d'extensions est configurable ; en ajouter une nouvelle ne demande
/// aucune modification de code.
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

    /// <summary>Filtre rapide sur l'extension, utilisé pendant l'énumération du scan.</summary>
    public bool HasSupportedExtension(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.');
        return ext.Length > 0 && _extensions.Contains(ext);
    }

    public CompatibilityResult Check(string extension, long fileSize)
    {
        if (!_extensions.Contains(extension.TrimStart('.')))
            return new CompatibilityResult(false, $"Extension .{extension} non prise en charge");
        if (fileSize <= 0)
            return new CompatibilityResult(false, "Fichier vide");
        if (fileSize > _maxSizeBytes)
            return new CompatibilityResult(false,
                $"Fichier trop volumineux ({fileSize / (1024 * 1024)} Mo, limite {_maxSizeMb} Mo)");
        return new CompatibilityResult(true, null);
    }

    /// <summary>Type MIME transmis à l'endpoint d'upload Google Photos.</summary>
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
