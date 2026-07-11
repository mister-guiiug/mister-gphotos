namespace MisterGPhotos.Core.Models;

/// <summary>Connected Google account (google_account table, a single row).</summary>
public class GoogleAccount
{
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public DateTime? ConnectedAt { get; set; }
    public string? Scopes { get; set; }
}
