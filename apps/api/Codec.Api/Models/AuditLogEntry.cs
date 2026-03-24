namespace Codec.Api.Models;

public enum AuditAction
{
    ServerRenamed,
    ServerDescriptionChanged,
    ServerIconChanged,
    ServerDeleted,
    ChannelCreated,
    ChannelRenamed,
    ChannelDescriptionChanged,
    ChannelDeleted,
    ChannelPurged,
    ChannelMoved,
    CategoryCreated,
    CategoryRenamed,
    CategoryDeleted,
    MemberKicked,
    MemberRoleChanged,
    InviteCreated,
    InviteRevoked,
    EmojiUploaded,
    EmojiRenamed,
    EmojiDeleted,
    MessageDeletedByAdmin,
    MessagePinned,
    MessageUnpinned,
    WebhookCreated,
    WebhookUpdated,
    WebhookDeleted,
    MemberBanned,
    MemberUnbanned,
    RoleCreated,
    RoleUpdated,
    RoleDeleted
}

public class AuditLogEntry
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public Guid? ActorUserId { get; set; }
    public AuditAction Action { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Server? Server { get; set; }
    public User? ActorUser { get; set; }
}
