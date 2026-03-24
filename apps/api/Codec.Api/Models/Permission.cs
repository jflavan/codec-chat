namespace Codec.Api.Models;

/// <summary>
/// Granular permission flags for server roles.
/// Stored as a bigint bitmask on each <see cref="ServerRoleEntity"/>.
/// </summary>
[Flags]
public enum Permission : long
{
    None = 0,

    // General
    ViewChannels = 1L << 0,
    ManageChannels = 1L << 1,
    ManageServer = 1L << 2,
    ManageRoles = 1L << 3,
    ManageEmojis = 1L << 4,
    ViewAuditLog = 1L << 5,
    CreateInvites = 1L << 6,
    ManageInvites = 1L << 7,

    // Membership
    KickMembers = 1L << 10,
    BanMembers = 1L << 11,

    // Messages
    SendMessages = 1L << 20,
    EmbedLinks = 1L << 21,
    AttachFiles = 1L << 22,
    AddReactions = 1L << 23,
    MentionEveryone = 1L << 24,
    ManageMessages = 1L << 25,
    PinMessages = 1L << 26,

    // Voice
    Connect = 1L << 30,
    Speak = 1L << 31,
    MuteMembers = 1L << 32,
    DeafenMembers = 1L << 33,

    /// <summary>
    /// Grants every permission. Holders bypass all permission checks (like the server owner).
    /// </summary>
    Administrator = 1L << 40,
}

public static class PermissionExtensions
{
    /// <summary>All permissions that the default "Admin" system role receives.</summary>
    public static readonly Permission AdminDefaults =
        Permission.ViewChannels | Permission.ManageChannels | Permission.ManageServer |
        Permission.ManageRoles | Permission.ManageEmojis | Permission.ViewAuditLog |
        Permission.CreateInvites | Permission.ManageInvites | Permission.KickMembers |
        Permission.SendMessages | Permission.EmbedLinks | Permission.AttachFiles |
        Permission.AddReactions | Permission.MentionEveryone | Permission.ManageMessages |
        Permission.PinMessages | Permission.Connect | Permission.Speak |
        Permission.MuteMembers | Permission.DeafenMembers;

    /// <summary>All permissions that the default "Member" system role receives.</summary>
    public static readonly Permission MemberDefaults =
        Permission.ViewChannels | Permission.SendMessages | Permission.EmbedLinks |
        Permission.AttachFiles | Permission.AddReactions | Permission.CreateInvites |
        Permission.Connect | Permission.Speak;

    /// <summary>Checks whether the permission set includes the given flag.</summary>
    public static bool Has(this Permission permissions, Permission flag) =>
        (permissions & Permission.Administrator) != 0 || (permissions & flag) == flag;
}
