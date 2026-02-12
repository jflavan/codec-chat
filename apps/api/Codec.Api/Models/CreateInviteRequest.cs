namespace Codec.Api.Models;

/// <summary>
/// Request payload for creating a server invite code.
/// </summary>
public record CreateInviteRequest
{
    /// <summary>
    /// Maximum number of uses for the invite. Null or 0 means unlimited.
    /// </summary>
    public int? MaxUses { get; init; }

    /// <summary>
    /// Number of hours until the invite expires. Null means 7 days (default). 0 means never.
    /// </summary>
    public int? ExpiresInHours { get; init; }
}
