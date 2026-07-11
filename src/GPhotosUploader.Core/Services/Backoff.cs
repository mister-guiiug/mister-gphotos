namespace GPhotosUploader.Core.Services;

/// <summary>Backoff exponentiel avec plafond et jitter, pour les erreurs temporaires.</summary>
public static class Backoff
{
    public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    /// <summary>Délai avant la tentative n° <paramref name="attempt"/> (0 = première relance).</summary>
    public static TimeSpan For(int attempt, TimeSpan? retryAfterHint = null)
    {
        if (retryAfterHint is { } hint && hint > TimeSpan.Zero)
            return hint <= MaxDelay ? hint : MaxDelay;

        var exponential = BaseDelay * Math.Pow(2, Math.Min(attempt, 10));
        var capped = exponential > MaxDelay ? MaxDelay : exponential;
        var jitterMs = Random.Shared.Next(0, 500);
        return capped + TimeSpan.FromMilliseconds(jitterMs);
    }
}
