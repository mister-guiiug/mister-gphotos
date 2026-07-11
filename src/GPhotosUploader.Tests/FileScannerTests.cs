using GPhotosUploader.Core.Data;
using GPhotosUploader.Core.Models;
using GPhotosUploader.Core.Services;
using Xunit;

namespace GPhotosUploader.Tests;

public class FileScannerTests : IDisposable
{
    private readonly TempDatabase _tmp = new();
    private readonly string _root;
    private readonly MediaFileRepository _repo;
    private readonly FileScanner _scanner;
    private readonly AppSettings _settings = new();

    public FileScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"gphotos-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _repo = new MediaFileRepository(_tmp.Db);
        var logger = new Logger(null, Path.Combine(_root, "logs"));
        _scanner = new FileScanner(_repo, logger);
    }

    public void Dispose()
    {
        _tmp.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string CreateFile(string relativePath, byte[] content)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return full;
    }

    [Fact]
    public async Task Scan_IndexesImagesRecursively_AndQueuesThem()
    {
        CreateFile("a.jpg", new byte[] { 1, 2, 3 });
        CreateFile(@"sub\deep\b.png", new byte[] { 4, 5, 6 });
        CreateFile("notes.txt", new byte[] { 7 }); // ignoré : extension non supportée

        var result = await _scanner.ScanAsync(_root, _settings, null, CancellationToken.None);

        Assert.Equal(2, result.TotalSeen);
        Assert.Equal(2, result.NewFiles);
        var counts = _repo.CountByStatus();
        Assert.Equal(2, counts[UploadStatus.Queued]);
    }

    [Fact]
    public async Task Rescan_UnchangedFile_DoesNotDuplicate()
    {
        CreateFile("a.jpg", new byte[] { 1, 2, 3 });

        await _scanner.ScanAsync(_root, _settings, null, CancellationToken.None);
        var second = await _scanner.ScanAsync(_root, _settings, null, CancellationToken.None);

        Assert.Equal(1, second.Unchanged);
        Assert.Equal(0, second.NewFiles);
        Assert.Equal(1, _repo.CountAll());
    }

    [Fact]
    public async Task IdenticalContent_SecondFile_MarkedLocalDuplicate()
    {
        var content = new byte[] { 9, 9, 9, 9 };
        CreateFile("original.jpg", content);
        CreateFile("copie.jpg", content);

        await _scanner.ScanAsync(_root, _settings, null, CancellationToken.None);

        var counts = _repo.CountByStatus();
        Assert.Equal(1, counts[UploadStatus.Queued]);
        Assert.Equal(1, counts[UploadStatus.SkippedDuplicateLocal]);
    }

    [Fact]
    public async Task ContentMatchingUploadedFile_MarkedRemoteDuplicate()
    {
        var content = new byte[] { 5, 5, 5 };
        var hash = FileScanner.ComputeSha256(CreateFile("deja-uploade.jpg", content));

        var uploaded = new MediaFile
        {
            LocalPath = @"C:\ailleurs\old.jpg",
            FileName = "old.jpg",
            Extension = "jpg",
            FileSize = content.Length,
            Sha256Hash = hash,
            UploadStatus = UploadStatus.Uploaded,
            GoogleMediaItemId = "gmedia-42",
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        _repo.Insert(uploaded);

        await _scanner.ScanAsync(_root, _settings, null, CancellationToken.None);

        var scanned = _repo.GetByPath(Path.Combine(_root, "deja-uploade.jpg"));
        Assert.NotNull(scanned);
        Assert.Equal(UploadStatus.SkippedDuplicateRemoteAppCreated, scanned.UploadStatus);
        Assert.Equal("gmedia-42", scanned.GoogleMediaItemId);
    }

    [Fact]
    public async Task OversizedFile_MarkedIncompatible()
    {
        _settings.MaxFileSizeMb = 1;
        CreateFile("gros.jpg", new byte[2 * 1024 * 1024]);

        var result = await _scanner.ScanAsync(_root, _settings, null, CancellationToken.None);

        Assert.Equal(1, result.Incompatible);
        var file = _repo.GetByPath(Path.Combine(_root, "gros.jpg"));
        Assert.Equal(UploadStatus.SkippedIncompatible, file!.UploadStatus);
        Assert.Contains("volumineux", file.LastError);
    }

    [Fact]
    public async Task DeletedFile_MarkedMissing_OnNextScan()
    {
        var path = CreateFile("ephemere.jpg", new byte[] { 1 });
        await _scanner.ScanAsync(_root, _settings, null, CancellationToken.None);

        File.Delete(path);
        var result = await _scanner.ScanAsync(_root, _settings, null, CancellationToken.None);

        Assert.Equal(1, result.Missing);
        Assert.Equal(ScanStatus.Missing, _repo.GetByPath(path)!.ScanStatus);
    }

    [Fact]
    public async Task MovedFile_IsRequeued_OnceOldRowIsMissing()
    {
        var content = new byte[] { 42, 42, 42 };
        var oldPath = CreateFile("a.jpg", content);
        await _scanner.ScanAsync(_root, _settings, null, CancellationToken.None);

        // Déplacement du fichier avant upload.
        var newPath = Path.Combine(_root, "sub", "a.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.Move(oldPath, newPath);

        // Scan 2 : la nouvelle ligne est vue comme doublon de l'ancienne (encore 'scanned'
        // au moment du traitement) ; l'ancienne passe 'missing' en fin de scan.
        await _scanner.ScanAsync(_root, _settings, null, CancellationToken.None);
        // Scan 3 : le doublon local est réévalué, son canonique est 'missing' -> remise en file.
        await _scanner.ScanAsync(_root, _settings, null, CancellationToken.None);

        var moved = _repo.GetByPath(newPath);
        Assert.NotNull(moved);
        Assert.Equal(UploadStatus.Queued, moved.UploadStatus);
    }

    [Fact]
    public async Task FileModifiedToMatchNewerRow_IsMarkedDuplicate_RegardlessOfIdOrder()
    {
        var pathA = CreateFile("a.jpg", new byte[] { 1, 1, 1 });
        CreateFile("b.jpg", new byte[] { 2, 2, 2 });
        await _scanner.ScanAsync(_root, _settings, null, CancellationToken.None);

        // A (id plus petit) prend le contenu de B (id plus grand).
        File.WriteAllBytes(pathA, new byte[] { 2, 2, 2 });
        File.SetLastWriteTimeUtc(pathA, DateTime.UtcNow.AddMinutes(1));
        await _scanner.ScanAsync(_root, _settings, null, CancellationToken.None);

        var counts = _repo.CountByStatus();
        Assert.Equal(1, counts.GetValueOrDefault(UploadStatus.Queued));
        Assert.Equal(1, counts.GetValueOrDefault(UploadStatus.SkippedDuplicateLocal));
    }

    [Fact]
    public async Task StrandedDiscoveredRow_IsRequeued_OnNextScan()
    {
        var path = CreateFile("a.jpg", new byte[] { 7, 7 });
        await _scanner.ScanAsync(_root, _settings, null, CancellationToken.None);

        // Simule un crash entre Insert('discovered') et Update('queued').
        var row = _repo.GetByPath(path)!;
        row.UploadStatus = UploadStatus.Discovered;
        _repo.Update(row);

        await _scanner.ScanAsync(_root, _settings, null, CancellationToken.None);

        Assert.Equal(UploadStatus.Queued, _repo.GetByPath(path)!.UploadStatus);
    }

    [Fact]
    public void Sha256_MatchesKnownVector()
    {
        var path = Path.Combine(_root, "abc.bin");
        File.WriteAllText(path, "abc");
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            FileScanner.ComputeSha256(path));
    }
}
