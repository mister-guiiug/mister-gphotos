using GPhotosUploader.Core.Models;
using GPhotosUploader.Core.Services;
using Xunit;

namespace GPhotosUploader.Tests;

public class StatusMapperTests
{
    [Fact]
    public void UploadStatus_RoundTripsThroughDbValues()
    {
        foreach (UploadStatus status in Enum.GetValues<UploadStatus>())
        {
            var db = status.ToDb();
            Assert.Equal(status, StatusMapper.UploadStatusFromDb(db));
        }
    }

    [Fact]
    public void DbValues_MatchSpecification()
    {
        Assert.Equal("skipped_duplicate_local", UploadStatus.SkippedDuplicateLocal.ToDb());
        Assert.Equal("skipped_duplicate_remote_app_created", UploadStatus.SkippedDuplicateRemoteAppCreated.ToDb());
        Assert.Equal("skipped_incompatible", UploadStatus.SkippedIncompatible.ToDb());
    }

    [Fact]
    public void UnknownStatus_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StatusMapper.UploadStatusFromDb("bogus"));
    }
}

public class AppSettingsTests
{
    [Fact]
    public void Clamp_EnforcesApiLimits()
    {
        var s = new AppSettings { BatchSize = 500, Concurrency = 99, MaxFileSizeMb = 9999, MaxRetries = -3 };
        s.Clamp();
        Assert.Equal(AppSettings.MaxBatchSize, s.BatchSize);
        Assert.Equal(AppSettings.MaxConcurrency, s.Concurrency);
        Assert.Equal(200, s.MaxFileSizeMb);
        Assert.Equal(0, s.MaxRetries);
    }

    [Fact]
    public void ExtensionSet_IsCaseInsensitive_AndTrimsDots()
    {
        var s = new AppSettings { IncludedExtensions = " JPG , .png ,heic" };
        var set = s.ExtensionSet();
        Assert.Contains("jpg", set);
        Assert.Contains("PNG", set);
        Assert.Contains("heic", set);
        Assert.DoesNotContain("gif", set);
    }
}

public class CompatibilityCheckerTests
{
    private static CompatibilityChecker MakeChecker(int maxMb = 200) =>
        new(new AppSettings { MaxFileSizeMb = maxMb });

    [Fact]
    public void AcceptsSupportedExtensionWithinSize()
    {
        var result = MakeChecker().Check("jpg", 5 * 1024 * 1024);
        Assert.True(result.IsCompatible);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void RejectsUnsupportedExtension()
    {
        var result = MakeChecker().Check("exe", 1024);
        Assert.False(result.IsCompatible);
        Assert.Contains("non prise en charge", result.Reason);
    }

    [Fact]
    public void RejectsOversizedFile()
    {
        var checker = MakeChecker(maxMb: 1);
        var result = checker.Check("jpg", 2L * 1024 * 1024);
        Assert.False(result.IsCompatible);
        Assert.Contains("volumineux", result.Reason);
    }

    [Fact]
    public void RejectsEmptyFile()
    {
        var result = MakeChecker().Check("png", 0);
        Assert.False(result.IsCompatible);
    }

    [Fact]
    public void MimeTypes_AreCorrectForCommonFormats()
    {
        Assert.Equal("image/jpeg", CompatibilityChecker.MimeTypeFor("jpg"));
        Assert.Equal("image/jpeg", CompatibilityChecker.MimeTypeFor("JPEG"));
        Assert.Equal("image/heic", CompatibilityChecker.MimeTypeFor("heic"));
        Assert.Equal("application/octet-stream", CompatibilityChecker.MimeTypeFor("nef"));
    }
}

public class BackoffTests
{
    [Fact]
    public void GrowsExponentially_UpToCap()
    {
        var first = Backoff.For(0);
        var later = Backoff.For(20);
        Assert.True(first >= TimeSpan.FromSeconds(1));
        Assert.True(first < TimeSpan.FromSeconds(2));
        Assert.True(later <= Backoff.MaxDelay + TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void HonorsRetryAfterHint()
    {
        var delay = Backoff.For(0, TimeSpan.FromSeconds(17));
        Assert.Equal(TimeSpan.FromSeconds(17), delay);
    }

    [Fact]
    public void CapsExcessiveRetryAfterHint()
    {
        var delay = Backoff.For(0, TimeSpan.FromMinutes(30));
        Assert.Equal(Backoff.MaxDelay, delay);
    }
}
