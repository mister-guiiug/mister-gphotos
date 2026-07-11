using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace GPhotosUploader.Core.Services;

/// <summary>
/// Stockage sécurisé des secrets (refresh token, client secret OAuth) dans le
/// Gestionnaire d'identifiants Windows (Windows Credential Manager).
/// Les secrets sont chiffrés par Windows et liés à la session utilisateur ;
/// rien n'est écrit en clair sur le disque.
/// </summary>
public static class CredentialStore
{
    public const string RefreshTokenTarget = "GooglePhotosLocalUploader/RefreshToken";
    public const string ClientSecretTarget = "GooglePhotosLocalUploader/OAuthClientSecret";

    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    public static void Save(string target, string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        if (blob.Length > 5 * 512)
            throw new InvalidOperationException("Secret trop long pour le Gestionnaire d'identifiants Windows.");

        var credential = new CREDENTIAL
        {
            Type = CRED_TYPE_GENERIC,
            TargetName = Marshal.StringToCoTaskMemUni(target),
            CredentialBlobSize = (uint)blob.Length,
            CredentialBlob = Marshal.AllocCoTaskMem(blob.Length),
            Persist = CRED_PERSIST_LOCAL_MACHINE,
            UserName = Marshal.StringToCoTaskMemUni(Environment.UserName)
        };
        try
        {
            Marshal.Copy(blob, 0, credential.CredentialBlob, blob.Length);
            if (!CredWriteW(ref credential, 0))
                throw new InvalidOperationException(
                    $"Écriture dans le Gestionnaire d'identifiants Windows impossible (code {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            Marshal.FreeCoTaskMem(credential.TargetName);
            Marshal.FreeCoTaskMem(credential.CredentialBlob);
            Marshal.FreeCoTaskMem(credential.UserName);
        }
    }

    public static string? Read(string target)
    {
        if (!CredReadW(target, CRED_TYPE_GENERIC, 0, out var credPtr))
            return null;
        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
                return null;
            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return Encoding.Unicode.GetString(blob);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public static void Delete(string target)
    {
        // Ignorer l'échec si l'identifiant n'existe pas déjà.
        CredDeleteW(target, CRED_TYPE_GENERIC, 0);
    }

    /// <summary>
    /// Enregistre le Client Secret OAuth en le liant au Client ID auquel il appartient,
    /// pour qu'un secret d'un ancien client ne soit jamais réutilisé silencieusement
    /// avec un Client ID différent.
    /// </summary>
    public static void SaveClientSecret(string clientId, string clientSecret)
    {
        var payload = JsonSerializer.Serialize(new ClientSecretRecord(clientId.Trim(), clientSecret.Trim()));
        Save(ClientSecretTarget, payload);
    }

    /// <summary>Retourne le Client Secret stocké, uniquement s'il appartient au Client ID demandé.</summary>
    public static string? ReadClientSecret(string clientId)
    {
        var raw = Read(ClientSecretTarget);
        if (raw is null) return null;
        try
        {
            var record = JsonSerializer.Deserialize<ClientSecretRecord>(raw);
            if (record is null) return null;
            return string.Equals(record.ClientId, clientId.Trim(), StringComparison.Ordinal)
                ? record.ClientSecret
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ClientSecretRecord(string ClientId, string ClientSecret);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr cred);
}
