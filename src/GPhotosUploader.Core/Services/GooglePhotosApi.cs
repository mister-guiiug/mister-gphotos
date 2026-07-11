using System.Net;
using System.Text;
using System.Text.Json;
using GPhotosUploader.Core.Models;
using GPhotosUploader.Core.Resources;

namespace GPhotosUploader.Core.Services;

/// <summary>Erreur retournée par l'API Google Photos, classée transitoire ou permanente.</summary>
public class GooglePhotosApiException : Exception
{
    public int StatusCode { get; }
    public bool IsTransient { get; }
    public TimeSpan? RetryAfter { get; }

    public GooglePhotosApiException(int statusCode, string message, bool isTransient, TimeSpan? retryAfter = null)
        : base(message)
    {
        StatusCode = statusCode;
        IsTransient = isTransient;
        RetryAfter = retryAfter;
    }
}

/// <summary>Résultat d'un élément dans une réponse batchCreate.</summary>
public record BatchCreateItemResult(string UploadToken, bool Success, string? MediaItemId, string? ErrorMessage);

/// <summary>
/// Client HTTP minimal de la Google Photos Library API :
///  1. POST /v1/uploads            → envoi des octets, retourne un upload token (valable ~24 h) ;
///  2. POST /v1/mediaItems:batchCreate → crée les médias (50 max par appel).
/// Aucune autre capacité n'est supposée : depuis mars 2025 l'API ne permet de
/// relire que les médias créés par l'application elle-même.
/// </summary>
public class GooglePhotosApi
{
    private const string UploadEndpoint = "https://photoslibrary.googleapis.com/v1/uploads";
    private const string BatchCreateEndpoint = "https://photoslibrary.googleapis.com/v1/mediaItems:batchCreate";

    private readonly HttpClient _http;

    public GooglePhotosApi(HttpClient http) => _http = http;

    /// <summary>Envoie les octets d'un fichier et retourne l'upload token.</summary>
    public async Task<string> UploadBytesAsync(string accessToken, string filePath, string mimeType,
        IProgress<long>? bytesProgress, CancellationToken ct)
    {
        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 128, useAsync: true);
        await using var progressStream = new ProgressReadStream(fileStream, bytesProgress);

        using var request = new HttpRequestMessage(HttpMethod.Post, UploadEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("X-Goog-Upload-Content-Type", mimeType);
        request.Headers.Add("X-Goog-Upload-Protocol", "raw");
        var content = new StreamContent(progressStream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Headers.ContentLength = fileStream.Length;
        request.Content = content;

        HttpResponseMessage response;
        string body;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout du HttpClient (et non annulation utilisateur) : erreur temporaire.
            throw new GooglePhotosApiException(0, Loc.T("Api_HttpTimeoutUpload"), isTransient: true);
        }
        using (response)
        {
            body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw ClassifyError(response, body, Loc.T("Api_Op_UploadBytes"));
        }

        var token = body.Trim();
        if (token.Length == 0)
            throw new GooglePhotosApiException(0, Loc.T("Api_EmptyUploadToken"), isTransient: true);
        return token;
    }

    /// <summary>Crée les médias à partir des upload tokens (50 éléments max par appel).</summary>
    public async Task<List<BatchCreateItemResult>> BatchCreateAsync(string accessToken,
        IReadOnlyList<(string FileName, string UploadToken)> items, CancellationToken ct)
    {
        if (items.Count == 0) return new List<BatchCreateItemResult>();
        if (items.Count > AppSettings.MaxBatchSize)
            throw new ArgumentException(Loc.TF("Api_BatchTooMany", AppSettings.MaxBatchSize));

        var payload = new
        {
            newMediaItems = items.Select(i => new
            {
                description = "",
                simpleMediaItem = new { fileName = i.FileName, uploadToken = i.UploadToken }
            }).ToArray()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BatchCreateEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GooglePhotosApiException(0, Loc.T("Api_HttpTimeoutBatch"), isTransient: true);
        }
        string body;
        using (response)
        {
            body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw ClassifyError(response, body, Loc.T("Api_Op_BatchCreate"));
        }

        var results = new List<BatchCreateItemResult>();
        using var json = JsonDocument.Parse(body);
        if (!json.RootElement.TryGetProperty("newMediaItemResults", out var resultArray))
            throw new GooglePhotosApiException((int)response.StatusCode,
                Loc.T("Api_UnexpectedBatchResponse"), isTransient: false);

        foreach (var item in resultArray.EnumerateArray())
        {
            var uploadToken = item.TryGetProperty("uploadToken", out var ut) ? ut.GetString() ?? "" : "";
            string? mediaItemId = null;
            bool success = false;
            string? error = null;

            if (item.TryGetProperty("mediaItem", out var mediaItem) &&
                mediaItem.TryGetProperty("id", out var id))
            {
                mediaItemId = id.GetString();
                success = mediaItemId is not null;
            }

            if (!success && item.TryGetProperty("status", out var status))
            {
                var code = status.TryGetProperty("code", out var c) ? c.GetInt32() : -1;
                var message = status.TryGetProperty("message", out var m) ? m.GetString() : null;
                if (code == 0 && mediaItemId is null)
                    error = Loc.T("Api_StatusOkNoMedia");
                else
                    error = Loc.TF("Api_RejectedFile", message ?? Loc.T("Api_UnknownError"), code);
            }
            else if (!success)
            {
                error = Loc.T("Api_NoStatusNoMedia");
            }

            results.Add(new BatchCreateItemResult(uploadToken, success, mediaItemId, error));
        }
        return results;
    }

    private static GooglePhotosApiException ClassifyError(HttpResponseMessage response, string body, string operation)
    {
        var status = (int)response.StatusCode;
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

        string detail = ExtractErrorMessage(body);
        bool transient = status switch
        {
            408 or 425 => true, // timeouts et "too early" : rejouables par définition
            429 => true,
            >= 500 => true,
            403 when detail.Contains("quota", StringComparison.OrdinalIgnoreCase)
                   || detail.Contains("rate", StringComparison.OrdinalIgnoreCase) => true,
            _ => false
        };

        var message = status switch
        {
            401 => Loc.T("Api_401"),
            403 when transient => Loc.TF("Api_403_Quota", detail),
            403 => Loc.TF("Api_403_Denied", detail),
            429 => Loc.T("Api_429"),
            >= 500 => Loc.TF("Api_5xx", status),
            _ => Loc.TF("Api_Generic", operation, status, detail)
        };
        return new GooglePhotosApiException(status, message, transient, retryAfter);
    }

    private static string ExtractErrorMessage(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
                return message.GetString() ?? body;
        }
        catch (JsonException) { }
        return body.Length > 300 ? body[..300] : body;
    }
}

/// <summary>Flux en lecture qui rapporte le nombre cumulé d'octets lus (progression d'upload).</summary>
public class ProgressReadStream : Stream
{
    private readonly Stream _inner;
    private readonly IProgress<long>? _progress;
    private long _totalRead;

    public ProgressReadStream(Stream inner, IProgress<long>? progress)
    {
        _inner = inner;
        _progress = progress;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        Report(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var read = await _inner.ReadAsync(buffer, ct);
        Report(read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var read = await _inner.ReadAsync(buffer.AsMemory(offset, count), ct);
        Report(read);
        return read;
    }

    private void Report(int read)
    {
        if (read > 0)
        {
            _totalRead += read;
            _progress?.Report(_totalRead);
        }
    }

    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
