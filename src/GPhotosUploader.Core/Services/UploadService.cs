using GPhotosUploader.Core.Data;
using GPhotosUploader.Core.Models;
using GPhotosUploader.Core.Resources;

namespace GPhotosUploader.Core.Services;

public enum UploadServiceState { Idle, Running, Paused, Stopping }

/// <summary>Progress of the file currently being uploaded.</summary>
public record UploadFileProgress(string FileName, long BytesSent, long TotalBytes);

/// <summary>
/// Upload orchestrator: consumes the queue of 'queued' files in batches,
/// obtains the upload tokens (with limited concurrency), creates the media items via batchCreate,
/// and persists every state transition to SQLite to allow resuming
/// after a shutdown, crash, or network loss.
/// </summary>
public class UploadService
{
    /// <summary>Conservative validity duration of an upload token (Google states ~24 h).</summary>
    private static readonly TimeSpan UploadTokenLifetime = TimeSpan.FromHours(20);

    /// <summary>Internal retries (backoff) within a single attempt in case of a transient error.</summary>
    private const int InAttemptTransientRetries = 3;

    /// <summary>Beyond this number of consecutive transient failures, we assume the network is down.</summary>
    private const int ConsecutiveTransientLimit = 5;

    private readonly MediaFileRepository _files;
    private readonly BatchRepository _batches;
    private readonly GoogleAuthService _auth;
    private readonly GooglePhotosApi _api;
    private readonly Logger _log;

    private readonly PauseTokenSource _pause = new();
    private readonly object _stateLock = new();
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private int _consecutiveTransient;

    private readonly object _rateLock = new();
    private readonly Queue<(DateTime At, long Bytes)> _rateSamples = new();

    public UploadServiceState State { get; private set; } = UploadServiceState.Idle;

    public event Action<UploadServiceState>? StateChanged;
    public event Action<UploadFileProgress>? FileProgressChanged;
    public event Action? CountersChanged;
    public event Action<string>? RunCompleted;
    public event Action? AuthenticationLost;

    public UploadService(MediaFileRepository files, BatchRepository batches,
        GoogleAuthService auth, GooglePhotosApi api, Logger log)
    {
        _files = files;
        _batches = batches;
        _auth = auth;
        _api = api;
        _log = log;
    }

    /// <summary>Recovery at application startup: files left in 'uploading' go back to 'queued'.</summary>
    public void RecoverAfterRestart()
    {
        var requeued = _files.RequeueInterrupted();
        if (requeued > 0)
            _log.Info("Upload", Loc.TF("Log_Upload_Recovered", requeued));
    }

    public bool Start(AppSettings settings)
    {
        lock (_stateLock)
        {
            if (State != UploadServiceState.Idle) return false;
            _cts = new CancellationTokenSource();
            _pause.Resume();
            _consecutiveTransient = 0;
            SetState(UploadServiceState.Running);
            var ct = _cts.Token;
            _runTask = Task.Run(() => RunAsync(settings, ct), CancellationToken.None);
            return true;
        }
    }

    public void Pause()
    {
        lock (_stateLock)
        {
            if (State != UploadServiceState.Running) return;
            _pause.Pause();
            SetState(UploadServiceState.Paused);
        }
        _log.Info("Upload", Loc.T("Log_Upload_Paused"));
    }

    public void Resume()
    {
        lock (_stateLock)
        {
            if (State != UploadServiceState.Paused) return;
            _pause.Resume();
            SetState(UploadServiceState.Running);
        }
        _log.Info("Upload", Loc.T("Log_Upload_Resumed"));
    }

    public async Task StopAsync()
    {
        Task? runTask;
        lock (_stateLock)
        {
            if (State is UploadServiceState.Idle or UploadServiceState.Stopping) return;
            SetState(UploadServiceState.Stopping);
            _cts?.Cancel();
            _pause.Resume(); // release the pause waits to let the cancellation propagate
            runTask = _runTask;
        }
        if (runTask is not null)
        {
            try { await runTask; }
            catch (OperationCanceledException) { }
        }
    }

    /// <summary>Current throughput in bytes/second (30 s sliding window).</summary>
    public double BytesPerSecond
    {
        get
        {
            lock (_rateLock)
            {
                TrimRateWindow();
                if (_rateSamples.Count == 0) return 0;
                var span = (DateTime.UtcNow - _rateSamples.Peek().At).TotalSeconds;
                if (span < 1) span = 1;
                return _rateSamples.Sum(s => s.Bytes) / span;
            }
        }
    }

    /// <summary>Estimated remaining time, or null if the throughput is unknown.</summary>
    public TimeSpan? EstimateRemaining(int maxRetries)
    {
        var rate = BytesPerSecond;
        if (rate < 1) return null;
        var pending = _files.PendingBytes(maxRetries);
        return TimeSpan.FromSeconds(pending / rate);
    }

    private async Task RunAsync(AppSettings settings, CancellationToken ct)
    {
        string summary;
        try
        {
            RecoverAfterRestart();
            _files.RequeuePaused();

            int totalUploaded = 0, totalFailed = 0, totalSkipped = 0;
            var attemptedThisRun = new HashSet<long>();

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                await _pause.WaitWhilePausedAsync(ct);

                var batch = NextBatch(settings, attemptedThisRun);
                if (batch.Count == 0) break;
                foreach (var f in batch) attemptedThisRun.Add(f.Id);

                var (uploaded, failed, skipped) = await ProcessBatchAsync(batch, settings, ct);
                totalUploaded += uploaded;
                totalFailed += failed;
                totalSkipped += skipped;
                CountersChanged?.Invoke();
            }

            summary = Loc.TF("Log_Upload_Summary", totalUploaded, totalFailed, totalSkipped);
            _log.Info("Upload", summary);
        }
        catch (OperationCanceledException)
        {
            _files.MarkUploadingAsPaused();
            summary = Loc.T("Log_Upload_Stopped");
            _log.Info("Upload", summary);
        }
        catch (AuthRequiredException ex)
        {
            _files.MarkUploadingAsPaused();
            summary = Loc.TF("Log_Upload_Interrupted", ex.Message);
            _log.Error("Upload", summary);
            AuthenticationLost?.Invoke();
        }
        catch (Exception ex)
        {
            _files.MarkUploadingAsPaused();
            summary = Loc.TF("Log_Upload_InterruptedError", ex.Message);
            _log.Error("Upload", summary);
        }
        finally
        {
            lock (_stateLock)
            {
                SetState(UploadServiceState.Idle);
                _runTask = null;
            }
            CountersChanged?.Invoke();
        }
        RunCompleted?.Invoke(summary);
    }

    /// <summary>
    /// Builds the next batch by paging over the eligible files, skipping
    /// those already attempted during this run. The paging ensures that no eligible file
    /// is starved by repeated failures at the head of the queue.
    /// </summary>
    private List<MediaFile> NextBatch(AppSettings settings, HashSet<long> attemptedThisRun)
    {
        var batch = new List<MediaFile>();
        int offset = 0;
        int pageSize = Math.Max(settings.BatchSize * 10, 100);
        while (batch.Count < settings.BatchSize)
        {
            var page = _files.GetNextForUpload(pageSize, settings.MaxRetries, offset);
            foreach (var f in page)
            {
                if (attemptedThisRun.Contains(f.Id)) continue;
                batch.Add(f);
                if (batch.Count == settings.BatchSize) break;
            }
            if (page.Count < pageSize) break; // last page: nothing more beyond this
            offset += page.Count;
        }
        return batch;
    }

    private async Task<(int Uploaded, int Failed, int Skipped)> ProcessBatchAsync(
        List<MediaFile> batch, AppSettings settings, CancellationToken ct)
    {
        var batchId = _batches.CreateBatch(batch.Count);
        int failed = 0, skipped = 0;

        // Phase 1: obtain an upload token for each file (with limited concurrency).
        using var semaphore = new SemaphoreSlim(settings.Concurrency);
        var ready = new System.Collections.Concurrent.ConcurrentBag<MediaFile>();

        var tasks = batch.Select(async file =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await _pause.WaitWhilePausedAsync(ct);
                var outcome = await PrepareUploadTokenAsync(file, settings, batchId, ct);
                switch (outcome)
                {
                    case FileOutcome.Ready: ready.Add(file); break;
                    case FileOutcome.Skipped: Interlocked.Increment(ref skipped); break;
                    case FileOutcome.Failed: Interlocked.Increment(ref failed); break;
                }
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        int uploaded = 0;
        try
        {
            await Task.WhenAll(tasks);

            // Phase 2: create the media items in a single batchCreate call.
            var withTokens = ready.Where(f => f.UploadToken is not null).ToList();
            if (withTokens.Count > 0)
            {
                uploaded = await BatchCreateAsync(withTokens, settings, batchId, ct);
                failed += withTokens.Count - uploaded;
            }
        }
        catch
        {
            // User stop, authentication loss, or network circuit breaker:
            // the batch must never stay 'running' in the database.
            _batches.CompleteBatch(batchId, uploaded, failed, "stopped");
            throw;
        }

        _batches.CompleteBatch(batchId, uploaded, failed, "completed");
        return (uploaded, failed, skipped);
    }

    private enum FileOutcome { Ready, Skipped, Failed }

    private async Task<FileOutcome> PrepareUploadTokenAsync(MediaFile file, AppSettings settings,
        long batchId, CancellationToken ct)
    {
        var attemptId = _batches.StartAttempt(file.Id, batchId);

        // Does the file still exist?
        if (!File.Exists(file.LocalPath))
        {
            file.ScanStatus = ScanStatus.Missing;
            MarkFailed(file, Loc.T("Upload_FileMissing"), permanent: true, settings);
            _batches.FinishAttempt(attemptId, "failed", file.LastError);
            return FileOutcome.Failed;
        }

        // Still compatible with the current settings?
        var checker = new CompatibilityChecker(settings);
        var compat = checker.Check(file.Extension, file.FileSize);
        if (!compat.IsCompatible)
        {
            file.UploadStatus = UploadStatus.SkippedIncompatible;
            file.LastError = compat.Reason;
            _files.Update(file);
            _batches.FinishAttempt(attemptId, "skipped", compat.Reason);
            return FileOutcome.Skipped;
        }

        // Duplicate of a file already uploaded by this application?
        if (file.Sha256Hash is not null)
        {
            var twin = _files.FindUploadedByHash(file.Sha256Hash, file.Id);
            if (twin is not null)
            {
                file.UploadStatus = UploadStatus.SkippedDuplicateRemoteAppCreated;
                file.GoogleMediaItemId = twin.GoogleMediaItemId;
                file.LastError = Loc.TF("Upload_RemoteDuplicate", twin.LocalPath);
                _files.Update(file);
                _batches.FinishAttempt(attemptId, "skipped", file.LastError);
                return FileOutcome.Skipped;
            }
        }

        // An upload token that is still fresh (crash between upload and batchCreate) is reused as-is.
        if (file.UploadToken is not null && file.UploadTokenAt is not null &&
            DateTime.UtcNow - file.UploadTokenAt.Value < UploadTokenLifetime)
        {
            file.UploadStatus = UploadStatus.Uploading;
            _files.Update(file);
            _batches.FinishAttempt(attemptId, "token_reused", null);
            _log.Info("Upload", Loc.TF("Log_Upload_TokenReused", file.FileName));
            return FileOutcome.Ready;
        }

        file.UploadStatus = UploadStatus.Uploading;
        file.UploadToken = null;
        file.UploadTokenAt = null;
        _files.Update(file);

        var mimeType = CompatibilityChecker.MimeTypeFor(file.Extension);
        long lastReported = 0;
        var progress = new Progress<long>(bytesSent =>
        {
            AddRateSample(bytesSent - Interlocked.Exchange(ref lastReported, bytesSent));
            FileProgressChanged?.Invoke(new UploadFileProgress(file.FileName, bytesSent, file.FileSize));
        });

        for (int attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await _pause.WaitWhilePausedAsync(ct);
            try
            {
                var accessToken = await _auth.GetAccessTokenAsync(settings.OAuthClientId, ct);
                var token = await _api.UploadBytesAsync(accessToken, file.LocalPath, mimeType, progress, ct);
                file.UploadToken = token;
                file.UploadTokenAt = DateTime.UtcNow;
                _files.Update(file);
                Interlocked.Exchange(ref _consecutiveTransient, 0);
                _batches.FinishAttempt(attemptId, "bytes_uploaded", null);
                return FileOutcome.Ready;
            }
            catch (OperationCanceledException)
            {
                file.UploadStatus = UploadStatus.Queued;
                _files.Update(file);
                _batches.FinishAttempt(attemptId, "cancelled", null);
                throw;
            }
            catch (GooglePhotosApiException ex) when (ex.StatusCode == 401 && attempt < InAttemptTransientRetries)
            {
                _auth.InvalidateAccessToken();
                _log.Warning("Upload", Loc.TF("Log_Upload_TokenExpiredRefresh", file.FileName));
            }
            catch (GooglePhotosApiException ex) when (ex.IsTransient && attempt < InAttemptTransientRetries)
            {
                _log.Warning("Upload", Loc.TF("Log_Upload_RetryIn",
                    file.FileName, ex.Message, Backoff.For(attempt, ex.RetryAfter).TotalSeconds.ToString("F0", Loc.Culture)));
                await Task.Delay(Backoff.For(attempt, ex.RetryAfter), ct);
            }
            catch (GooglePhotosApiException ex)
            {
                bool permanent = !ex.IsTransient && ex.StatusCode != 401;
                MarkFailed(file, ex.Message, permanent, settings);
                _batches.FinishAttempt(attemptId, "failed", ex.Message);
                if (ex.IsTransient) RegisterTransientFailure();
                return FileOutcome.Failed;
            }
            catch (HttpRequestException ex) when (attempt < InAttemptTransientRetries)
            {
                _log.Warning("Upload", Loc.TF("Log_Upload_NetworkRetry", file.FileName, ex.Message));
                await Task.Delay(Backoff.For(attempt, null), ct);
            }
            catch (HttpRequestException ex)
            {
                MarkFailed(file, Loc.TF("Upload_NetworkError", ex.Message), permanent: false, settings);
                _batches.FinishAttempt(attemptId, "failed", ex.Message);
                RegisterTransientFailure();
                return FileOutcome.Failed;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Local error (locked file, non-hydrated cloud placeholder, etc.):
                // it must not trip the network circuit breaker.
                MarkFailed(file, Loc.TF("Upload_FileAccessError", ex.Message), permanent: false, settings);
                _batches.FinishAttempt(attemptId, "failed", ex.Message);
                return FileOutcome.Failed;
            }
        }
    }

    private async Task<int> BatchCreateAsync(List<MediaFile> files, AppSettings settings,
        long batchId, CancellationToken ct)
    {
        var items = files.Select(f => (f.FileName, f.UploadToken!)).ToList();
        List<BatchCreateItemResult> results;

        for (int attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await _pause.WaitWhilePausedAsync(ct);
            try
            {
                var accessToken = await _auth.GetAccessTokenAsync(settings.OAuthClientId, ct);
                results = await _api.BatchCreateAsync(accessToken, items, ct);
                Interlocked.Exchange(ref _consecutiveTransient, 0);
                break;
            }
            catch (GooglePhotosApiException ex) when (ex.StatusCode == 401 && attempt < InAttemptTransientRetries)
            {
                _auth.InvalidateAccessToken();
            }
            catch (GooglePhotosApiException ex) when (ex.IsTransient && attempt < InAttemptTransientRetries)
            {
                _log.Warning("Upload", Loc.TF("Log_Upload_BatchCreateRetry", ex.Message));
                await Task.Delay(Backoff.For(attempt, ex.RetryAfter), ct);
            }
            catch (HttpRequestException) when (attempt < InAttemptTransientRetries)
            {
                await Task.Delay(Backoff.For(attempt, null), ct);
            }
            catch (GooglePhotosApiException ex)
            {
                if (ex.IsTransient)
                {
                    // Transient failure: the tokens stay in the database and will be
                    // reused on the next pass if they are still fresh.
                    foreach (var f in files)
                    {
                        f.UploadStatus = UploadStatus.Queued;
                        f.LastError = ex.Message;
                        _files.Update(f);
                    }
                    RegisterTransientFailure();
                }
                else
                {
                    // Permanent rejection (e.g. invalid tokens): discard the tokens to
                    // re-send the bytes, and count the failure toward MaxRetries.
                    foreach (var f in files)
                    {
                        f.UploadToken = null;
                        f.UploadTokenAt = null;
                        MarkFailed(f, ex.Message, permanent: false, settings);
                    }
                }
                _log.Error("Upload", Loc.TF("Log_Upload_BatchCreateFailed", ex.Message));
                return 0;
            }
        }

        var byToken = results.ToDictionary(r => r.UploadToken, r => r);
        int uploaded = 0;
        foreach (var file in files)
        {
            if (byToken.TryGetValue(file.UploadToken!, out var result) && result.Success)
            {
                file.UploadStatus = UploadStatus.Uploaded;
                file.GoogleMediaItemId = result.MediaItemId;
                file.UploadedAt = DateTime.UtcNow;
                file.UploadToken = null;
                file.UploadTokenAt = null;
                file.LastError = null;
                _files.Update(file);
                uploaded++;
                _log.Info("Upload", Loc.TF("Log_Upload_Uploaded", file.FileName));
            }
            else
            {
                var message = result?.ErrorMessage ?? Loc.T("Upload_NoBatchResult");
                // Token consumed or rejected: we discard it to re-send the bytes on the next attempt.
                file.UploadToken = null;
                file.UploadTokenAt = null;
                MarkFailed(file, message, permanent: false, settings);
                _log.Warning("Upload", Loc.TF("Log_Upload_Failed", file.FileName, message));
            }
        }
        return uploaded;
    }

    private void MarkFailed(MediaFile file, string error, bool permanent, AppSettings settings)
    {
        file.UploadStatus = UploadStatus.Failed;
        file.LastError = error;
        file.RetryCount = permanent ? Math.Max(settings.MaxRetries, file.RetryCount + 1) : file.RetryCount + 1;
        _files.Update(file);
    }

    private void RegisterTransientFailure()
    {
        if (Interlocked.Increment(ref _consecutiveTransient) >= ConsecutiveTransientLimit)
            throw new HttpRequestException(Loc.T("Upload_TooManyNetworkErrors"));
    }

    private void AddRateSample(long bytes)
    {
        if (bytes <= 0) return;
        lock (_rateLock)
        {
            _rateSamples.Enqueue((DateTime.UtcNow, bytes));
            TrimRateWindow();
        }
    }

    private void TrimRateWindow()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-30);
        while (_rateSamples.Count > 0 && _rateSamples.Peek().At < cutoff)
            _rateSamples.Dequeue();
    }

    private void SetState(UploadServiceState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }
}
