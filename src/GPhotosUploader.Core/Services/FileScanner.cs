using System.Security.Cryptography;
using GPhotosUploader.Core.Data;
using GPhotosUploader.Core.Models;
using Microsoft.Data.Sqlite;

namespace GPhotosUploader.Core.Services;

/// <summary>Résumé d'un scan terminé.</summary>
public record ScanResult(int TotalSeen, int NewFiles, int Unchanged, int Modified,
                         int Incompatible, int Duplicates, int Errors, int Missing);

/// <summary>Progression du scan en cours.</summary>
public record ScanProgress(int FilesSeen, string CurrentFile);

/// <summary>
/// Scan récursif du dossier racine : détecte les images compatibles, calcule le
/// hash SHA-256 et indexe le tout dans SQLite. Relançable : un fichier déjà connu
/// dont la taille et la date de modification n'ont pas bougé n'est pas re-hashé
/// et ne crée jamais de doublon en base (local_path est UNIQUE).
/// </summary>
public class FileScanner
{
    private readonly MediaFileRepository _repo;
    private readonly Logger _log;

    public FileScanner(MediaFileRepository repo, Logger log)
    {
        _repo = repo;
        _log = log;
    }

    public async Task<ScanResult> ScanAsync(string rootFolder, AppSettings settings,
        IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        if (!Directory.Exists(rootFolder))
            throw new DirectoryNotFoundException($"Dossier introuvable : {rootFolder}");

        var checker = new CompatibilityChecker(settings);
        var scanStart = DateTime.UtcNow;
        int seen = 0, added = 0, unchanged = 0, modified = 0, incompatible = 0, duplicates = 0, errors = 0;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        };

        await Task.Run(() =>
        {
            // Les fichiers inchangés sont rafraîchis (last_seen_at) par lots de 500,
            // en une transaction, pour rester rapide sur des rescans de 100 000+ fichiers.
            var unchangedIds = new List<long>();
            void FlushUnchanged()
            {
                if (unchangedIds.Count == 0) return;
                _repo.TouchLastSeen(unchangedIds, DateTime.UtcNow);
                unchangedIds.Clear();
            }

            try
            {
                foreach (var path in Directory.EnumerateFiles(rootFolder, "*", options))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!checker.HasSupportedExtension(path))
                        continue;

                    seen++;
                    if (seen % 50 == 0)
                        progress?.Report(new ScanProgress(seen, path));

                    try
                    {
                        var outcome = ProcessFile(path, checker, unchangedIds);
                        switch (outcome)
                        {
                            case FileOutcome.Added: added++; break;
                            case FileOutcome.Unchanged: unchanged++; break;
                            case FileOutcome.Modified: modified++; break;
                            case FileOutcome.Incompatible: incompatible++; break;
                            case FileOutcome.Duplicate: duplicates++; break;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
                    {
                        errors++;
                        _log.Warning("Scan", $"Fichier ignoré pour cette passe : {path} ({ex.Message})");
                    }

                    if (unchangedIds.Count >= 500)
                        FlushUnchanged();
                }
            }
            finally
            {
                FlushUnchanged();
            }
        }, ct);

        int missing = _repo.MarkMissingUnderRoot(EnsureTrailingSeparator(rootFolder), scanStart);

        var result = new ScanResult(seen, added, unchanged, modified, incompatible, duplicates, errors, missing);
        _log.Info("Scan",
            $"Scan terminé : {seen} fichiers vus, {added} nouveaux, {modified} modifiés, {unchanged} inchangés, " +
            $"{duplicates} doublons locaux, {incompatible} incompatibles, {errors} erreurs, {missing} disparus.");
        return result;
    }

    private enum FileOutcome { Added, Unchanged, Modified, Incompatible, Duplicate }

    private FileOutcome ProcessFile(string path, CompatibilityChecker checker, List<long> unchangedIds)
    {
        var info = new FileInfo(path);
        var now = DateTime.UtcNow;
        var existing = _repo.GetByPath(path);

        // Fichier déjà connu, inchangé : on met simplement à jour last_seen_at (par lots).
        // Exceptions : les lignes 'discovered' (crash entre insert et mise en file) et
        // 'skipped_duplicate_local' (leur canonique a pu disparaître) sont réévaluées.
        if (existing is not null
            && existing.FileSize == info.Length
            && existing.ModifiedAt.HasValue
            && Math.Abs((existing.ModifiedAt.Value - info.LastWriteTimeUtc).TotalSeconds) < 2
            && existing.Sha256Hash is not null
            && existing.UploadStatus is not UploadStatus.Discovered
            && existing.UploadStatus is not UploadStatus.SkippedDuplicateLocal)
        {
            unchangedIds.Add(existing.Id);
            return FileOutcome.Unchanged;
        }

        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var compat = checker.Check(extension, info.Length);

        var file = existing ?? new MediaFile
        {
            LocalPath = path,
            FirstSeenAt = now
        };
        file.FileName = Path.GetFileName(path);
        file.Extension = extension;
        file.FileSize = info.Length;
        file.CreatedAt = info.CreationTimeUtc;
        file.ModifiedAt = info.LastWriteTimeUtc;
        file.ScanStatus = ScanStatus.Scanned;
        file.LastSeenAt = now;

        if (!compat.IsCompatible)
        {
            file.UploadStatus = UploadStatus.SkippedIncompatible;
            file.LastError = compat.Reason;
            Persist(file, existing is not null);
            return FileOutcome.Incompatible;
        }

        var newHash = ComputeSha256(path);
        bool contentChanged = existing is not null && existing.Sha256Hash != newHash;
        file.Sha256Hash = newHash;

        if (contentChanged)
        {
            // Contenu différent : on repart de zéro pour ce fichier.
            file.UploadStatus = UploadStatus.Queued;
            file.GoogleMediaItemId = null;
            file.UploadToken = null;
            file.UploadTokenAt = null;
            file.UploadedAt = null;
            file.RetryCount = 0;
            file.LastError = null;
        }

        // Ne jamais rétrograder un fichier déjà uploadé.
        if (file.UploadStatus == UploadStatus.Uploaded)
        {
            Persist(file, existing is not null);
            return FileOutcome.Unchanged;
        }

        // Détection de doublons par hash.
        if (file.Id == 0)
            file.Id = _repo.Insert(file); // insérer d'abord pour disposer d'un id de référence

        var uploadedTwin = _repo.FindUploadedByHash(newHash, file.Id);
        if (uploadedTwin is not null)
        {
            file.UploadStatus = UploadStatus.SkippedDuplicateRemoteAppCreated;
            file.GoogleMediaItemId = uploadedTwin.GoogleMediaItemId;
            file.LastError = $"Contenu identique déjà uploadé par cette application : {uploadedTwin.LocalPath}";
            _repo.Update(file);
            return FileOutcome.Duplicate;
        }

        // Peu importe l'ordre des ids : dès qu'un jumeau « vivant » existe, ce fichier
        // est marqué doublon. Aucun cycle possible : une fois marqué skipped, il sort
        // du champ de FindLocalDuplicate.
        var localTwin = _repo.FindLocalDuplicate(newHash, file.Id);
        if (localTwin is not null)
        {
            file.UploadStatus = UploadStatus.SkippedDuplicateLocal;
            file.LastError = $"Doublon local de : {localTwin.LocalPath}";
            _repo.Update(file);
            return FileOutcome.Duplicate;
        }

        if (file.UploadStatus is UploadStatus.Discovered or UploadStatus.SkippedIncompatible
            or UploadStatus.SkippedDuplicateLocal)
        {
            file.UploadStatus = UploadStatus.Queued;
            file.LastError = null;
        }
        _repo.Update(file);
        return existing is null ? FileOutcome.Added : (contentChanged ? FileOutcome.Modified : FileOutcome.Unchanged);
    }

    private void Persist(MediaFile file, bool exists)
    {
        if (exists || file.Id != 0) _repo.Update(file);
        else file.Id = _repo.Insert(file);
    }

    public static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
