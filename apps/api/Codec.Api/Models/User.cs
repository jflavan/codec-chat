namespace Codec.Api.Models;

public class User
{
    public Guid Id { get; set; }
    public string GoogleSubject { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// User-chosen display name that overrides the Google-provided
    /// <see cref="DisplayName"/>. Nullable; max 32 characters.
    /// </summary>
    public string? Nickname { get; set; }

    public string? Email { get; set; }
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Relative path to a user-uploaded avatar file. When set, this takes
    /// priority over the Google-sourced <see cref="AvatarUrl"/>.
    /// </summary>
    public string? CustomAvatarPath { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Returns the effective display name: nickname if set, otherwise the Google display name.
    /// </summary>
    public string EffectiveDisplayName =>
        string.IsNullOrWhiteSpace(Nickname) ? DisplayName : Nickname;
    public List<Message> Messages { get; set; } = new();
    public List<ServerMember> ServerMemberships { get; set; } = new();
    public List<Reaction> Reactions { get; set; } = new();
    public List<Friendship> SentFriendRequests { get; set; } = new();
    public List<Friendship> ReceivedFriendRequests { get; set; } = new();
    public List<DmChannelMember> DmChannelMemberships { get; set; } = new();
    public List<DirectMessage> DirectMessages { get; set; } = new();
    public List<ServerInvite> CreatedInvites { get; set; } = new();
}
