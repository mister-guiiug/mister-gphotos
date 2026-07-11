using MisterGPhotos.Core.Data;
using MisterGPhotos.Core.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MisterGPhotos.Tests;

/// <summary>Temporary SQLite database, deleted at the end of the test.</summary>
public sealed class TempDatabase : IDisposable
{
    public Database Db { get; }
    private readonly string _path;

    public TempDatabase()
    {
        _path = Path.Combine(Path.GetTempPath(), $"gphotos-test-{Guid.NewGuid():N}.db");
        Db = new Database(_path);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }
}

public class DatabaseTests
{
    private static MediaFile SampleFile(string path = @"C:\photos\a.jpg", string hash = "abc123") => new()
    {
        LocalPath = path,
        FileName = Path.GetFileName(path),
        Extension = "jpg",
        FileSize = 1234,
        Sha256Hash = hash,
        CreatedAt = DateTime.UtcNow.AddDays(-2),
        ModifiedAt = DateTime.UtcNow.AddDays(-1),
        UploadStatus = UploadStatus.Queued,
        FirstSeenAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow
    };

    [Fact]
    public void Migrations_CreateAllTables()
    {
        using var tmp = new TempDatabase();
        using var conn = tmp.Db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        var tables = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) tables.Add(reader.GetString(0));

        Assert.Contains("settings", tables);
        Assert.Contains("google_account", tables);
        Assert.Contains("media_files", tables);
        Assert.Contains("upload_batches", tables);
        Assert.Contains("upload_attempts", tables);
        Assert.Contains("app_logs", tables);
    }

    [Fact]
    public void MediaFile_InsertAndReadBack_PreservesAllFields()
    {
        using var tmp = new TempDatabase();
        var repo = new MediaFileRepository(tmp.Db);
        var file = SampleFile();
        file.Id = repo.Insert(file);

        var loaded = repo.GetByPath(file.LocalPath);
        Assert.NotNull(loaded);
        Assert.Equal(file.Id, loaded.Id);
        Assert.Equal(file.FileName, loaded.FileName);
        Assert.Equal(file.FileSize, loaded.FileSize);
        Assert.Equal(file.Sha256Hash, loaded.Sha256Hash);
        Assert.Equal(UploadStatus.Queued, loaded.UploadStatus);
        Assert.Equal(file.ModifiedAt!.Value, loaded.ModifiedAt!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void DuplicateLocalPath_IsRejected_NoDoubleIndexing()
    {
        using var tmp = new TempDatabase();
        var repo = new MediaFileRepository(tmp.Db);
        repo.Insert(SampleFile());
        Assert.Throws<SqliteException>(() => repo.Insert(SampleFile()));
    }

    [Fact]
    public void RequeueInterrupted_ResetsUploadingToQueued()
    {
        using var tmp = new TempDatabase();
        var repo = new MediaFileRepository(tmp.Db);
        var file = SampleFile();
        file.UploadStatus = UploadStatus.Uploading;
        file.Id = repo.Insert(file);

        var count = repo.RequeueInterrupted();

        Assert.Equal(1, count);
        Assert.Equal(UploadStatus.Queued, repo.GetById(file.Id)!.UploadStatus);
    }

    [Fact]
    public void FindUploadedByHash_FindsTwin_ExcludesSelf()
    {
        using var tmp = new TempDatabase();
        var repo = new MediaFileRepository(tmp.Db);
        var uploaded = SampleFile(@"C:\photos\a.jpg", "samehash");
        uploaded.UploadStatus = UploadStatus.Uploaded;
        uploaded.GoogleMediaItemId = "gmedia-1";
        uploaded.Id = repo.Insert(uploaded);

        var candidate = SampleFile(@"C:\photos\copy.jpg", "samehash");
        candidate.Id = repo.Insert(candidate);

        var twin = repo.FindUploadedByHash("samehash", candidate.Id);
        Assert.NotNull(twin);
        Assert.Equal(uploaded.Id, twin.Id);

        Assert.Null(repo.FindUploadedByHash("samehash", uploaded.Id));
    }

    [Fact]
    public void GetNextForUpload_RespectsRetryLimit_AndOrdersQueuedFirst()
    {
        using var tmp = new TempDatabase();
        var repo = new MediaFileRepository(tmp.Db);

        var failedRetryable = SampleFile(@"C:\p\1.jpg", "h1");
        failedRetryable.UploadStatus = UploadStatus.Failed;
        failedRetryable.RetryCount = 2;
        repo.Insert(failedRetryable);

        var failedExhausted = SampleFile(@"C:\p\2.jpg", "h2");
        failedExhausted.UploadStatus = UploadStatus.Failed;
        failedExhausted.RetryCount = 5;
        repo.Insert(failedExhausted);

        var queued = SampleFile(@"C:\p\3.jpg", "h3");
        repo.Insert(queued);

        var next = repo.GetNextForUpload(10, maxRetries: 5);

        Assert.Equal(2, next.Count);
        Assert.Equal(@"C:\p\3.jpg", next[0].LocalPath); // queued before failed
        Assert.Equal(@"C:\p\1.jpg", next[1].LocalPath);
    }

    [Fact]
    public void GetNextForUpload_PaginatesWithOffset()
    {
        using var tmp = new TempDatabase();
        var repo = new MediaFileRepository(tmp.Db);
        for (int i = 1; i <= 3; i++)
            repo.Insert(SampleFile($@"C:\p\{i}.jpg", $"hash{i}"));

        var page2 = repo.GetNextForUpload(2, maxRetries: 5, offset: 2);

        Assert.Single(page2);
        Assert.Equal(@"C:\p\3.jpg", page2[0].LocalPath);
    }

    [Fact]
    public void TouchLastSeen_UpdatesTimestampAndScanStatus_InBulk()
    {
        using var tmp = new TempDatabase();
        var repo = new MediaFileRepository(tmp.Db);
        var f = SampleFile();
        f.ScanStatus = ScanStatus.Missing;
        f.LastSeenAt = DateTime.UtcNow.AddDays(-3);
        f.Id = repo.Insert(f);

        var now = DateTime.UtcNow;
        repo.TouchLastSeen(new[] { f.Id }, now);

        var loaded = repo.GetById(f.Id)!;
        Assert.Equal(ScanStatus.Scanned, loaded.ScanStatus);
        Assert.Equal(now, loaded.LastSeenAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Settings_SaveAndLoad_RoundTrips()
    {
        using var tmp = new TempDatabase();
        var repo = new SettingsRepository(tmp.Db);
        var s = new AppSettings
        {
            RootFolder = @"D:\Photos",
            BatchSize = 25,
            MaxRetries = 7,
            Concurrency = 3,
            OAuthClientId = "client-id-test"
        };
        repo.Save(s);

        var loaded = repo.Load();
        Assert.Equal(@"D:\Photos", loaded.RootFolder);
        Assert.Equal(25, loaded.BatchSize);
        Assert.Equal(7, loaded.MaxRetries);
        Assert.Equal(3, loaded.Concurrency);
        Assert.Equal("client-id-test", loaded.OAuthClientId);
    }

    [Fact]
    public void Batches_And_Attempts_RecordLifecycle()
    {
        using var tmp = new TempDatabase();
        var files = new MediaFileRepository(tmp.Db);
        var batches = new BatchRepository(tmp.Db);
        var fileId = files.Insert(SampleFile());

        var batchId = batches.CreateBatch(1);
        var attemptId = batches.StartAttempt(fileId, batchId);
        batches.FinishAttempt(attemptId, "bytes_uploaded", null);
        batches.CompleteBatch(batchId, 1, 0, "completed");

        var recent = batches.ListRecent(10);
        Assert.Single(recent);
        Assert.Equal(1, recent[0].SuccessCount);
        Assert.Equal("completed", recent[0].Status);
        Assert.NotNull(recent[0].CompletedAt);
    }
}
