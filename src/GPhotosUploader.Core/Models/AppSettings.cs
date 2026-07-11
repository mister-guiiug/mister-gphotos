namespace GPhotosUploader.Core.Models;

/// <summary>Paramètres de l'application (table settings, clé/valeur).</summary>
public class AppSettings
{
    public const int MinBatchSize = 1;
    /// <summary>Limite dure imposée par mediaItems:batchCreate (50 éléments max par appel).</summary>
    public const int MaxBatchSize = 50;
    public const int MinConcurrency = 1;
    public const int MaxConcurrency = 3;

    public string RootFolder { get; set; } = "";

    /// <summary>Nombre de fichiers par batch (uploads d'octets + un appel batchCreate).</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Nombre maximum de tentatives pour un fichier en erreur temporaire.</summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>Nombre d'uploads simultanés (1 à 3).</summary>
    public int Concurrency { get; set; } = 2;

    /// <summary>Taille maximum acceptée pour une photo, en Mo (limite Google Photos : 200 Mo).</summary>
    public int MaxFileSizeMb { get; set; } = 200;

    /// <summary>Extensions incluses, séparées par des virgules, sans point, en minuscules.</summary>
    public string IncludedExtensions { get; set; } = DefaultExtensions;

    /// <summary>Client ID OAuth de l'utilisateur (créé dans Google Cloud Console, type "Application de bureau").</summary>
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
    /// Copie immuable pour la durée d'un scan ou d'un run d'upload : les threads de
    /// travail ne doivent jamais voir les modifications faites dans l'écran Paramètres.
    /// </summary>
    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Ramène les valeurs dans des bornes sûres.</summary>
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
