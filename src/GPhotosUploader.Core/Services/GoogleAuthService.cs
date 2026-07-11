using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GPhotosUploader.Core.Data;
using GPhotosUploader.Core.Models;
using GPhotosUploader.Core.Resources;

namespace GPhotosUploader.Core.Services;

/// <summary>Thrown when no account is connected or the refresh token is invalid.</summary>
public class AuthRequiredException : Exception
{
    public AuthRequiredException(string message) : base(message) { }
}

/// <summary>
/// Google OAuth 2.0 authentication for an installed application:
/// Authorization Code + PKCE flow with loopback redirect (127.0.0.1).
/// Minimal scopes: appendonly (upload) + readonly.appcreateddata (re-reading the
/// media created by the application) + openid/email (displaying the connected account).
/// The refresh token and the client secret are stored in the Windows Credential
/// Manager, never in clear text.
/// </summary>
public class GoogleAuthService
{
    public static readonly string[] Scopes =
    {
        "https://www.googleapis.com/auth/photoslibrary.appendonly",
        "https://www.googleapis.com/auth/photoslibrary.readonly.appcreateddata",
        "openid",
        "email"
    };

    /// <summary>Scopes without which the application cannot function (granular consent).</summary>
    private static readonly string[] RequiredScopes =
    {
        "https://www.googleapis.com/auth/photoslibrary.appendonly",
        "https://www.googleapis.com/auth/photoslibrary.readonly.appcreateddata"
    };

    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

    private sealed record CachedToken(string Value, DateTime ExpiresUtc);

    private readonly HttpClient _http;
    private readonly AccountRepository _accounts;
    private readonly Logger _log;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private CachedToken? _cached;

    public GoogleAuthService(HttpClient http, AccountRepository accounts, Logger log)
    {
        _http = http;
        _accounts = accounts;
        _log = log;
    }

    public bool IsConnected =>
        CredentialStore.Read(CredentialStore.RefreshTokenTarget) is not null;

    public GoogleAccount? CurrentAccount => _accounts.Get();

    /// <summary>
    /// Runs the full OAuth flow: opens the default browser, waits for the authorization
    /// code on a local loopback, exchanges it for the tokens and
    /// securely persists the refresh token.
    /// </summary>
    public async Task<GoogleAccount> SignInAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new AuthRequiredException(Loc.T("Auth_ClientCredsRequired"));

        var codeVerifier = RandomUrlSafeString(64);
        var codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var state = RandomUrlSafeString(32);

        using var listener = new HttpListener();
        var redirectUri = StartLoopbackListener(listener);

        var authUrl = AuthEndpoint +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            "&response_type=code" +
            $"&scope={Uri.EscapeDataString(string.Join(' ', Scopes))}" +
            $"&code_challenge={codeChallenge}" +
            "&code_challenge_method=S256" +
            $"&state={state}" +
            "&access_type=offline" +
            "&prompt=consent";

        _log.Info("Auth", Loc.T("Log_Auth_OpeningBrowser"));
        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        string code;
        try
        {
            code = await WaitForAuthorizationCodeAsync(listener, state, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AuthRequiredException(Loc.T("Auth_Timeout"));
        }
        finally
        {
            listener.Stop();
        }

        // Exchange the code for the tokens.
        var form = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = codeVerifier
        };
        using var response = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new AuthRequiredException(Loc.TF("Auth_ExchangeRefused", (int)response.StatusCode));

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        if (string.IsNullOrEmpty(refreshToken))
            throw new AuthRequiredException(Loc.T("Auth_NoRefreshToken"));

        // Granular consent: verify that the essential scopes were indeed granted.
        var grantedScopes = root.TryGetProperty("scope", out var scopeProp) ? scopeProp.GetString() ?? "" : "";
        var missing = RequiredScopes.Where(s => !grantedScopes.Contains(s, StringComparison.Ordinal)).ToList();
        if (grantedScopes.Length > 0 && missing.Count > 0)
        {
            await TryRevokeAsync(refreshToken);
            throw new AuthRequiredException(Loc.T("Auth_ScopesNotGranted"));
        }

        var accessToken = root.GetProperty("access_token").GetString()!;
        var expiresUtc = DateTime.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32() - 60);
        Volatile.Write(ref _cached, new CachedToken(accessToken, expiresUtc));

        CredentialStore.Save(CredentialStore.RefreshTokenTarget, refreshToken);
        CredentialStore.SaveClientSecret(clientId, clientSecret);

        var email = root.TryGetProperty("id_token", out var idToken)
            ? ExtractEmailFromIdToken(idToken.GetString())
            : null;

        var account = new GoogleAccount
        {
            Email = email,
            DisplayName = email,
            ConnectedAt = DateTime.UtcNow,
            Scopes = grantedScopes.Length > 0 ? grantedScopes : string.Join(' ', Scopes)
        };
        _accounts.Save(account);
        _log.Info("Auth", Loc.TF("Log_Auth_Connected", email ?? Loc.T("Auth_EmailNotProvided")));
        return account;
    }

    /// <summary>
    /// Starts the listener on a free loopback port. The port is reserved directly
    /// by HttpListener.Start (no window between discovering the port and claiming it).
    /// </summary>
    private static string StartLoopbackListener(HttpListener listener)
    {
        var rng = new Random();
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var port = rng.Next(49215, 65500);
            var prefix = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Clear();
            listener.Prefixes.Add(prefix);
            try
            {
                listener.Start();
                return prefix;
            }
            catch (HttpListenerException)
            {
                // Port in use: try the next one.
            }
        }
        throw new AuthRequiredException(Loc.T("Auth_NoFreePort"));
    }

    /// <summary>
    /// Waits for the OAuth redirect. Spurious requests (probes, favicon, refreshed
    /// tab) receive a 404 and do not interrupt the wait for the real response.
    /// </summary>
    private async Task<string> WaitForAuthorizationCodeAsync(HttpListener listener, string expectedState,
        CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var context = await listener.GetContextAsync().WaitAsync(ct);
            var query = context.Request.QueryString;
            var returnedState = query["state"];
            var error = query["error"];
            var code = query["code"];

            // Request unrelated to the OAuth flow: we respond 404 and keep waiting.
            if (error is null && (code is null || returnedState != expectedState))
            {
                await TryWriteResponseAsync(context, 404, "<html><body>Not found</body></html>", ct);
                continue;
            }

            var (statusCode, html) = error is null
                ? (200, ResultPage(Loc.T("Auth_Html_SuccessTitle"), Loc.T("Auth_Html_SuccessBody")))
                : (400, ResultPage(Loc.T("Auth_Html_FailureTitle"), Loc.T("Auth_Html_FailureBody")));

            // Writing the confirmation page is best-effort: a browser that
            // drops the connection must not cause an already-received authorization to fail.
            await TryWriteResponseAsync(context, statusCode, html, ct);

            if (error is not null)
                throw new AuthRequiredException(Loc.TF("Auth_AuthorizationDenied", error));
            return code!;
        }
    }

    /// <summary>Small HTML confirmation page displayed in the browser after authorization.</summary>
    private static string ResultPage(string title, string body) =>
        $"<html><head><meta charset='utf-8'></head><body style='font-family:sans-serif'>" +
        $"<h2>{WebUtility.HtmlEncode(title)}</h2><p>{WebUtility.HtmlEncode(body)}</p></body></html>";

    private async Task TryWriteResponseAsync(HttpListenerContext context, int statusCode, string html,
        CancellationToken ct)
    {
        try
        {
            var buffer = Encoding.UTF8.GetBytes(html);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, ct);
            context.Response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            _log.Warning("Auth", Loc.T("Log_Auth_ConfirmPageFailed"));
        }
    }

    /// <summary>Returns a valid access token, refreshing it if necessary.</summary>
    public async Task<string> GetAccessTokenAsync(string clientId, CancellationToken ct)
    {
        var cached = Volatile.Read(ref _cached);
        if (cached is not null && DateTime.UtcNow < cached.ExpiresUtc)
            return cached.Value;
        return await RefreshAccessTokenAsync(clientId, ct);
    }

    /// <summary>Forces a refresh (used after a 401).</summary>
    public async Task<string> RefreshAccessTokenAsync(string clientId, CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            // Another call may have already refreshed while waiting for the lock.
            var cached = Volatile.Read(ref _cached);
            if (cached is not null && DateTime.UtcNow < cached.ExpiresUtc)
                return cached.Value;

            var refreshToken = CredentialStore.Read(CredentialStore.RefreshTokenTarget)
                ?? throw new AuthRequiredException(Loc.T("Auth_NoAccount"));
            var clientSecret = CredentialStore.ReadClientSecret(clientId)
                ?? throw new AuthRequiredException(Loc.T("Auth_ClientSecretNotFound"));

            var form = new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "refresh_token"
            };
            using var response = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.Warning("Auth", Loc.TF("Log_Auth_RefreshRefused", (int)response.StatusCode));
                if ((int)response.StatusCode is 400 or 401)
                {
                    Volatile.Write(ref _cached, null);
                    throw new AuthRequiredException(Loc.T("Auth_SessionExpired"));
                }
                throw new HttpRequestException(Loc.TF("Auth_RefreshFailed", (int)response.StatusCode));
            }

            using var json = JsonDocument.Parse(body);
            var accessToken = json.RootElement.GetProperty("access_token").GetString()!;
            var expiresUtc = DateTime.UtcNow.AddSeconds(json.RootElement.GetProperty("expires_in").GetInt32() - 60);
            Volatile.Write(ref _cached, new CachedToken(accessToken, expiresUtc));
            return accessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Invalidates the cached access token (after a 401 from the API).</summary>
    public void InvalidateAccessToken() => Volatile.Write(ref _cached, null);

    /// <summary>
    /// Disconnects the account: revokes the refresh token (best-effort, with a short timeout)
    /// then ALWAYS erases it locally, even if the remote revocation fails.
    /// The OAuth Client Secret is intentionally kept: it identifies the application
    /// (not the account) and allows reconnecting without re-entering it. It is only erased
    /// by "Delete the application's local data".
    /// </summary>
    public async Task SignOutAsync()
    {
        var refreshToken = CredentialStore.Read(CredentialStore.RefreshTokenTarget);
        if (refreshToken is not null)
            await TryRevokeAsync(refreshToken);

        CredentialStore.Delete(CredentialStore.RefreshTokenTarget);
        Volatile.Write(ref _cached, null);
        _accounts.Delete();
        _log.Info("Auth", Loc.T("Log_Auth_Disconnected"));
    }

    /// <summary>Best-effort revocation with a short timeout: must never block or cause the caller to fail.</summary>
    private async Task TryRevokeAsync(string token)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token });
            using var _ = await _http.PostAsync(RevokeEndpoint, content, cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _log.Warning("Auth", Loc.T("Log_Auth_RevokeFailed"));
        }
    }

    private static string? ExtractEmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken)) return null;
        var parts = idToken.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return json.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string RandomUrlSafeString(int bytes)
    {
        var data = RandomNumberGenerator.GetBytes(bytes);
        return Base64UrlEncode(data);
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
