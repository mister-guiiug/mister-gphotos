using System.Text.Json;
using GPhotosUploader.Core.Resources;

namespace GPhotosUploader.Core.Services;

/// <summary>Credentials of a Google OAuth client of type "Desktop app".</summary>
public record OAuthClientCredentials(string ClientId, string ClientSecret);

/// <summary>
/// Validation and import of the OAuth credentials created by the user in Google
/// Cloud Console. Google exposes no public API (neither gcloud nor Terraform)
/// to create a "Desktop app" OAuth client: the application can only
/// import the client_secret_….json file downloaded from the console,
/// or accept the values pasted manually.
/// </summary>
public static class OAuthClientConfig
{
    public const string ClientIdSuffix = ".apps.googleusercontent.com";

    public static bool IsValidClientId(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return false;
        var id = clientId.Trim();
        return id.EndsWith(ClientIdSuffix, StringComparison.OrdinalIgnoreCase)
            && id.Length > ClientIdSuffix.Length + 5
            && !id.Any(char.IsWhiteSpace);
    }

    public static bool IsPlausibleClientSecret(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return false;
        var s = secret.Trim();
        return s.Length >= 10 && !s.Any(char.IsWhiteSpace);
    }

    /// <summary>
    /// Extracts the Client ID and the Client Secret from a client_secret_….json file
    /// downloaded from Google Cloud Console ("installed" root key for a
    /// "Desktop app" client).
    /// </summary>
    /// <exception cref="FormatException">Unreadable file, wrong client type, or missing fields.</exception>
    public static OAuthClientCredentials ParseClientSecretJson(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new FormatException(Loc.T("Config_InvalidJson"));
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("web", out _))
                throw new FormatException(Loc.T("Config_WebClientType"));

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("installed", out var installed)
                || installed.ValueKind != JsonValueKind.Object)
                throw new FormatException(Loc.T("Config_UnexpectedFormat"));

            var clientId = installed.TryGetProperty("client_id", out var idProp)
                && idProp.ValueKind == JsonValueKind.String ? idProp.GetString() : null;
            var clientSecret = installed.TryGetProperty("client_secret", out var secretProp)
                && secretProp.ValueKind == JsonValueKind.String ? secretProp.GetString() : null;

            if (!IsValidClientId(clientId))
                throw new FormatException(Loc.T("Config_MissingClientId"));
            if (!IsPlausibleClientSecret(clientSecret))
                throw new FormatException(Loc.T("Config_MissingClientSecret"));

            return new OAuthClientCredentials(clientId!.Trim(), clientSecret!.Trim());
        }
    }

    /// <exception cref="FormatException">Invalid content.</exception>
    /// <exception cref="IOException">Unreadable file.</exception>
    public static OAuthClientCredentials ParseClientSecretFile(string path)
        => ParseClientSecretJson(File.ReadAllText(path));
}
