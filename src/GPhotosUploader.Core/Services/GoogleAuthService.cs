using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GPhotosUploader.Core.Data;
using GPhotosUploader.Core.Models;

namespace GPhotosUploader.Core.Services;

/// <summary>Levée quand aucun compte n'est connecté ou que le refresh token est invalide.</summary>
public class AuthRequiredException : Exception
{
    public AuthRequiredException(string message) : base(message) { }
}

/// <summary>
/// Authentification Google OAuth 2.0 pour application installée :
/// flux Authorization Code + PKCE avec redirection loopback (127.0.0.1).
/// Scopes minimaux : appendonly (upload) + readonly.appcreateddata (relecture des
/// médias créés par l'application) + openid/email (affichage du compte connecté).
/// Le refresh token et le client secret sont stockés dans le Gestionnaire
/// d'identifiants Windows, jamais en clair.
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

    /// <summary>Scopes sans lesquels l'application ne peut pas fonctionner (consentement granulaire).</summary>
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
    /// Lance le flux OAuth complet : ouvre le navigateur par défaut, attend le code
    /// d'autorisation sur une boucle locale, l'échange contre les tokens et
    /// persiste le refresh token de façon sécurisée.
    /// </summary>
    public async Task<GoogleAccount> SignInAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new AuthRequiredException(
                "Client ID et Client Secret OAuth requis. Consultez le guide de configuration Google Cloud.");

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

        _log.Info("Auth", "Ouverture du navigateur pour l'autorisation Google...");
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
            throw new AuthRequiredException("Délai d'autorisation dépassé (5 minutes). Réessayez.");
        }
        finally
        {
            listener.Stop();
        }

        // Échange du code contre les tokens.
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
            throw new AuthRequiredException($"Échange du code OAuth refusé par Google ({(int)response.StatusCode}).");

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        if (string.IsNullOrEmpty(refreshToken))
            throw new AuthRequiredException("Google n'a pas fourni de refresh token. Réessayez la connexion.");

        // Consentement granulaire : vérifier que les scopes indispensables ont bien été accordés.
        var grantedScopes = root.TryGetProperty("scope", out var scopeProp) ? scopeProp.GetString() ?? "" : "";
        var missing = RequiredScopes.Where(s => !grantedScopes.Contains(s, StringComparison.Ordinal)).ToList();
        if (grantedScopes.Length > 0 && missing.Count > 0)
        {
            await TryRevokeAsync(refreshToken);
            throw new AuthRequiredException(
                "L'accès à Google Photos n'a pas été accordé. Sur l'écran de consentement Google, " +
                "cochez toutes les autorisations demandées puis réessayez la connexion.");
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
        _log.Info("Auth", $"Compte Google connecté : {email ?? "(email non communiqué)"}");
        return account;
    }

    /// <summary>
    /// Démarre le listener sur un port loopback libre. Le port est réservé directement
    /// par HttpListener.Start (pas de fenêtre entre la découverte du port et sa prise).
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
                // Port occupé : essayer le suivant.
            }
        }
        throw new AuthRequiredException(
            "Impossible d'ouvrir un port local pour recevoir la réponse Google. Réessayez.");
    }

    /// <summary>
    /// Attend la redirection OAuth. Les requêtes parasites (sondes, favicon, onglet
    /// rafraîchi) reçoivent un 404 et n'interrompent pas l'attente de la vraie réponse.
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

            // Requête sans rapport avec le flux OAuth : on répond 404 et on continue d'attendre.
            if (error is null && (code is null || returnedState != expectedState))
            {
                await TryWriteResponseAsync(context, 404, "<html><body>Not found</body></html>", ct);
                continue;
            }

            var (statusCode, html) = error is null
                ? (200, "<html><body style='font-family:sans-serif'><h2>Connexion réussie</h2><p>Vous pouvez fermer cette fenêtre et revenir dans Google Photos Local Uploader.</p></body></html>")
                : (400, "<html><body style='font-family:sans-serif'><h2>Échec de la connexion</h2><p>Retournez dans l'application et réessayez.</p></body></html>");

            // L'écriture de la page de confirmation est best-effort : un navigateur qui
            // coupe la connexion ne doit pas faire échouer une autorisation déjà reçue.
            await TryWriteResponseAsync(context, statusCode, html, ct);

            if (error is not null)
                throw new AuthRequiredException($"Autorisation refusée : {error}");
            return code!;
        }
    }

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
            _log.Warning("Auth", "La page de confirmation n'a pas pu être affichée dans le navigateur (sans conséquence).");
        }
    }

    /// <summary>Retourne un access token valide, en le rafraîchissant si nécessaire.</summary>
    public async Task<string> GetAccessTokenAsync(string clientId, CancellationToken ct)
    {
        var cached = Volatile.Read(ref _cached);
        if (cached is not null && DateTime.UtcNow < cached.ExpiresUtc)
            return cached.Value;
        return await RefreshAccessTokenAsync(clientId, ct);
    }

    /// <summary>Force un rafraîchissement (utilisé après un 401).</summary>
    public async Task<string> RefreshAccessTokenAsync(string clientId, CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            // Un autre appel a peut-être déjà rafraîchi pendant l'attente du verrou.
            var cached = Volatile.Read(ref _cached);
            if (cached is not null && DateTime.UtcNow < cached.ExpiresUtc)
                return cached.Value;

            var refreshToken = CredentialStore.Read(CredentialStore.RefreshTokenTarget)
                ?? throw new AuthRequiredException("Aucun compte Google connecté.");
            var clientSecret = CredentialStore.ReadClientSecret(clientId)
                ?? throw new AuthRequiredException(
                    "Client secret OAuth introuvable pour ce Client ID (il a peut-être été modifié). " +
                    "Reconnectez votre compte.");

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
                _log.Warning("Auth", $"Rafraîchissement du token refusé ({(int)response.StatusCode}).");
                if ((int)response.StatusCode is 400 or 401)
                {
                    Volatile.Write(ref _cached, null);
                    throw new AuthRequiredException(
                        "La session Google a expiré ou a été révoquée. Reconnectez votre compte.");
                }
                throw new HttpRequestException($"Rafraîchissement du token impossible ({(int)response.StatusCode}).");
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

    /// <summary>Invalide le token d'accès en cache (après un 401 de l'API).</summary>
    public void InvalidateAccessToken() => Volatile.Write(ref _cached, null);

    /// <summary>
    /// Déconnecte le compte : révoque le refresh token (au mieux, avec timeout court)
    /// puis l'efface TOUJOURS localement, même si la révocation distante échoue.
    /// Le Client Secret OAuth est volontairement conservé : il identifie l'application
    /// (pas le compte) et permet de se reconnecter sans le retaper. Il n'est effacé
    /// que par « Supprimer les données locales de l'application ».
    /// </summary>
    public async Task SignOutAsync()
    {
        var refreshToken = CredentialStore.Read(CredentialStore.RefreshTokenTarget);
        if (refreshToken is not null)
            await TryRevokeAsync(refreshToken);

        CredentialStore.Delete(CredentialStore.RefreshTokenTarget);
        Volatile.Write(ref _cached, null);
        _accounts.Delete();
        _log.Info("Auth", "Compte Google déconnecté (refresh token révoqué et supprimé).");
    }

    /// <summary>Révocation best-effort avec timeout court : ne doit jamais bloquer ni faire échouer l'appelant.</summary>
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
            _log.Warning("Auth", "Révocation distante du token impossible (hors-ligne ?) — le token local sera effacé quand même.");
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
