using System.Text.Json;

namespace GPhotosUploader.Core.Services;

/// <summary>Identifiants d'un client OAuth Google de type « Application de bureau ».</summary>
public record OAuthClientCredentials(string ClientId, string ClientSecret);

/// <summary>
/// Validation et import des identifiants OAuth créés par l'utilisateur dans Google
/// Cloud Console. Google n'expose aucune API publique (ni gcloud, ni Terraform)
/// pour créer un client OAuth « Application de bureau » : l'application ne peut
/// qu'importer le fichier client_secret_….json téléchargé depuis la console,
/// ou accepter les valeurs collées manuellement.
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
    /// Extrait le Client ID et le Client Secret d'un fichier client_secret_….json
    /// téléchargé depuis Google Cloud Console (clé racine « installed » pour un
    /// client de type « Application de bureau »).
    /// </summary>
    /// <exception cref="FormatException">Fichier illisible, mauvais type de client ou champs manquants.</exception>
    public static OAuthClientCredentials ParseClientSecretJson(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new FormatException("Ce fichier n'est pas un JSON valide.");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("web", out _))
                throw new FormatException(
                    "Ce fichier correspond à un client OAuth de type « Application Web ». " +
                    "Créez un client de type « Application de bureau » puis téléchargez son JSON.");

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("installed", out var installed)
                || installed.ValueKind != JsonValueKind.Object)
                throw new FormatException(
                    "Format inattendu : ce fichier ne ressemble pas à un client_secret_….json " +
                    "téléchargé depuis Google Cloud Console.");

            var clientId = installed.TryGetProperty("client_id", out var idProp)
                && idProp.ValueKind == JsonValueKind.String ? idProp.GetString() : null;
            var clientSecret = installed.TryGetProperty("client_secret", out var secretProp)
                && secretProp.ValueKind == JsonValueKind.String ? secretProp.GetString() : null;

            if (!IsValidClientId(clientId))
                throw new FormatException("Le champ client_id du fichier est absent ou invalide.");
            if (!IsPlausibleClientSecret(clientSecret))
                throw new FormatException("Le champ client_secret du fichier est absent ou invalide.");

            return new OAuthClientCredentials(clientId!.Trim(), clientSecret!.Trim());
        }
    }

    /// <exception cref="FormatException">Contenu invalide.</exception>
    /// <exception cref="IOException">Fichier illisible.</exception>
    public static OAuthClientCredentials ParseClientSecretFile(string path)
        => ParseClientSecretJson(File.ReadAllText(path));
}
