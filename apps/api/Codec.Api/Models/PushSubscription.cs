namespace Codec.Api.Models;

public class PushSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Push service endpoint URL.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Client public key (Base64-URL encoded).</summary>
    public string P256dh { get; set; } = "";

    /// <summary>Shared authentication secret (Base64-URL encoded).</summary>
    public string Auth { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set to false when the push service returns a permanent failure (410 Gone).</summary>
    public bool IsActive { get; set; } = true;

    public User? User { get; set; }
}
