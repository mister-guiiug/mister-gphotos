namespace GPhotosUploader.Core.Models;

/// <summary>Compte Google connecté (table google_account, une seule ligne).</summary>
public class GoogleAccount
{
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public DateTime? ConnectedAt { get; set; }
    public string? Scopes { get; set; }
}
