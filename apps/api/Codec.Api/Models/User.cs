namespace Codec.Api.Models;

public class User
{
    public Guid Id { get; set; }
    public string? GoogleSubject { get; set; }
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

    /// <summary>
    /// BCrypt hash of the user's password. Null for Google-only accounts.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// SAML NameID from the identity provider. Used to match SAML assertions
    /// to existing users. Null for non-SAML accounts.
    /// </summary>
    public string? SamlNameId { get; set; }

    /// <summary>
    /// The SAML identity provider that provisioned this user via JIT.
    /// Null for non-SAML accounts.
    /// </summary>
    public Guid? SamlIdentityProviderId { get; set; }
    public SamlIdentityProvider? SamlIdentityProvider { get; set; }

    /// <summary>
    /// Indicates whether this user has global admin privileges, allowing them to
    /// delete any server, channel, or message regardless of ownership.
    /// </summary>
    public bool IsGlobalAdmin { get; set; }

    /// <summary>
    /// Number of consecutive failed login attempts. Reset to 0 on successful login.
    /// </summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>
    /// When set, login is blocked until this time passes (account lockout).
    /// </summary>
    public DateTimeOffset? LockoutEnd { get; set; }

    /// <summary>
    /// Whether the user has verified their email address.
    /// </summary>
    public bool EmailVerified { get; set; }

    /// <summary>
    /// SHA-256 hash of the email verification token. Null when no pending verification.
    /// </summary>
    public string? EmailVerificationToken { get; set; }

    /// <summary>
    /// When the email verification token expires.
    /// </summary>
    public DateTimeOffset? EmailVerificationTokenExpiresAt { get; set; }

    /// <summary>
    /// When the last verification email was sent (for rate limiting resends).
    /// </summary>
    public DateTimeOffset? EmailVerificationTokenSentAt { get; set; }

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
