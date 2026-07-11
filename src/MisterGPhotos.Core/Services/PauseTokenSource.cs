namespace MisterGPhotos.Core.Services;

/// <summary>
/// Cooperative pause token: the upload orchestrator calls
/// WaitWhilePausedAsync between each step and freezes while the pause is active.
/// </summary>
public class PauseTokenSource
{
    private readonly object _lock = new();
    private TaskCompletionSource<object?> _resumeTcs = CreateCompleted();

    public bool IsPaused { get; private set; }

    public void Pause()
    {
        lock (_lock)
        {
            if (IsPaused) return;
            IsPaused = true;
            _resumeTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (!IsPaused) return;
            IsPaused = false;
            _resumeTcs.TrySetResult(null);
        }
    }

    public Task WaitWhilePausedAsync(CancellationToken ct)
    {
        Task task;
        lock (_lock)
        {
            task = _resumeTcs.Task;
        }
        return task.WaitAsync(ct);
    }

    private static TaskCompletionSource<object?> CreateCompleted()
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult(null);
        return tcs;
    }
}
