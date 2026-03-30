using Codec.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Data;

public class CodecDbContext : DbContext
{
    public CodecDbContext(DbContextOptions<CodecDbContext> options) : base(options)
    {
    }

    public DbSet<Server> Servers => Set<Server>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ServerMember> ServerMembers => Set<ServerMember>();
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<DmChannel> DmChannels => Set<DmChannel>();
    public DbSet<DmChannelMember> DmChannelMembers => Set<DmChannelMember>();
    public DbSet<DirectMessage> DirectMessages => Set<DirectMessage>();
    public DbSet<ServerInvite> ServerInvites => Set<ServerInvite>();
    public DbSet<LinkPreview> LinkPreviews => Set<LinkPreview>();
    public DbSet<VoiceState> VoiceStates => Set<VoiceState>();
    public DbSet<VoiceCall> VoiceCalls => Set<VoiceCall>();
    public DbSet<CustomEmoji> CustomEmojis => Set<CustomEmoji>();
    public DbSet<PresenceState> PresenceStates => Set<PresenceState>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ChannelCategory> ChannelCategories => Set<ChannelCategory>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<ChannelNotificationOverride> ChannelNotificationOverrides => Set<ChannelNotificationOverride>();
    public DbSet<PinnedMessage> PinnedMessages => Set<PinnedMessage>();
    public DbSet<SamlIdentityProvider> SamlIdentityProviders => Set<SamlIdentityProvider>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<WebhookDeliveryLog> WebhookDeliveryLogs => Set<WebhookDeliveryLog>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<BannedMember> BannedMembers => Set<BannedMember>();
    public DbSet<ServerRoleEntity> ServerRoles => Set<ServerRoleEntity>();
    public DbSet<ServerMemberRole> ServerMemberRoles => Set<ServerMemberRole>();
    public DbSet<ChannelPermissionOverride> ChannelPermissionOverrides => Set<ChannelPermissionOverride>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Server>(e =>
        {
            e.HasMany(server => server.Channels)
                .WithOne(channel => channel.Server)
                .HasForeignKey(channel => channel.ServerId);
            e.Property(s => s.Description).HasMaxLength(256);
        });

        modelBuilder.Entity<Channel>(e =>
        {
            e.HasMany(channel => channel.Messages)
                .WithOne(message => message.Channel)
                .HasForeignKey(message => message.ChannelId);
            e.HasOne(c => c.Category)
                .WithMany(cat => cat.Channels)
                .HasForeignKey(c => c.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
            e.Property(c => c.Description).HasMaxLength(256);
        });

        modelBuilder.Entity<User>()
            .HasIndex(user => user.GoogleSubject)
            .IsUnique()
            .HasFilter("\"GoogleSubject\" IS NOT NULL");

        modelBuilder.Entity<User>()
            .HasIndex(user => user.GitHubSubject)
            .IsUnique()
            .HasFilter("\"GitHubSubject\" IS NOT NULL");

        modelBuilder.Entity<User>()
            .HasIndex(user => user.DiscordSubject)
            .IsUnique()
            .HasFilter("\"DiscordSubject\" IS NOT NULL");

        modelBuilder.Entity<User>()
            .HasIndex(user => user.Email)
            .IsUnique()
            .HasFilter("\"Email\" IS NOT NULL");

        modelBuilder.Entity<User>()
            .HasIndex(user => user.EmailVerificationToken)
            .IsUnique()
            .HasFilter("\"EmailVerificationToken\" IS NOT NULL");

        modelBuilder.Entity<User>()
            .HasIndex(user => new { user.SamlNameId, user.SamlIdentityProviderId })
            .IsUnique()
            .HasFilter("\"SamlNameId\" IS NOT NULL");

        modelBuilder.Entity<User>()
            .HasOne(user => user.SamlIdentityProvider)
            .WithMany()
            .HasForeignKey(user => user.SamlIdentityProviderId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<User>()
            .Property(user => user.Nickname)
            .HasMaxLength(32);

        modelBuilder.Entity<User>()
            .Property(user => user.StatusText)
            .HasMaxLength(128);

        modelBuilder.Entity<User>()
            .Property(user => user.StatusEmoji)
            .HasMaxLength(8);

        // EffectiveDisplayName is a computed property; tell EF Core to ignore it.
        modelBuilder.Entity<User>()
            .Ignore(user => user.EffectiveDisplayName);

        modelBuilder.Entity<User>()
            .HasMany(user => user.Messages)
            .WithOne(message => message.AuthorUser)
            .HasForeignKey(message => message.AuthorUserId);

        modelBuilder.Entity<ServerMember>()
            .HasKey(member => new { member.ServerId, member.UserId });

        modelBuilder.Entity<ServerMember>()
            .HasOne(member => member.Server)
            .WithMany(server => server.Members)
            .HasForeignKey(member => member.ServerId);

        modelBuilder.Entity<ServerMember>()
            .HasOne(member => member.User)
            .WithMany(user => user.ServerMemberships)
            .HasForeignKey(member => member.UserId);

        modelBuilder.Entity<ServerMember>()
            .HasOne(member => member.Role)
            .WithMany(role => role.Members)
            .HasForeignKey(member => member.RoleId);

        modelBuilder.Entity<ServerRoleEntity>(e =>
        {
            e.HasOne(r => r.Server)
                .WithMany(s => s.Roles)
                .HasForeignKey(r => r.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(r => r.Name).HasMaxLength(100);
            e.Property(r => r.Color).HasMaxLength(7);
            e.HasIndex(r => new { r.ServerId, r.Position });
            e.HasIndex(r => new { r.ServerId, r.Name }).IsUnique();
        });

        modelBuilder.Entity<ServerMemberRole>(e =>
        {
            e.HasKey(mr => new { mr.UserId, mr.RoleId });

            e.HasOne(mr => mr.Role)
                .WithMany(r => r.MemberRoles)
                .HasForeignKey(mr => mr.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChannelPermissionOverride>(e =>
        {
            e.HasOne(o => o.Channel)
                .WithMany()
                .HasForeignKey(o => o.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(o => o.Role)
                .WithMany()
                .HasForeignKey(o => o.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(o => new { o.ChannelId, o.RoleId }).IsUnique();
        });

        modelBuilder.Entity<Reaction>(entity =>
        {
            entity.HasOne(r => r.Message)
                .WithMany(m => m.Reactions)
                .HasForeignKey(r => r.MessageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.DirectMessage)
                .WithMany(dm => dm.Reactions)
                .HasForeignKey(r => r.DirectMessageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.User)
                .WithMany(u => u.Reactions)
                .HasForeignKey(r => r.UserId);

            entity.HasIndex(r => new { r.MessageId, r.UserId, r.Emoji })
                .IsUnique()
                .HasFilter("\"MessageId\" IS NOT NULL");

            entity.HasIndex(r => new { r.DirectMessageId, r.UserId, r.Emoji })
                .IsUnique()
                .HasFilter("\"DirectMessageId\" IS NOT NULL");

            entity.ToTable(t => t.HasCheckConstraint("CK_Reaction_SingleParent",
                "(\"MessageId\" IS NOT NULL AND \"DirectMessageId\" IS NULL) OR (\"MessageId\" IS NULL AND \"DirectMessageId\" IS NOT NULL)"));
        });

        // Friendship relationships and constraints.
        modelBuilder.Entity<Friendship>()
            .HasOne(friendship => friendship.Requester)
            .WithMany(user => user.SentFriendRequests)
            .HasForeignKey(friendship => friendship.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Friendship>()
            .HasOne(friendship => friendship.Recipient)
            .WithMany(user => user.ReceivedFriendRequests)
            .HasForeignKey(friendship => friendship.RecipientId)
            .OnDelete(DeleteBehavior.Restrict);

        // One friendship record per user pair.
        modelBuilder.Entity<Friendship>()
            .HasIndex(friendship => new { friendship.RequesterId, friendship.RecipientId })
            .IsUnique();

        modelBuilder.Entity<Friendship>()
            .HasIndex(friendship => friendship.RequesterId);

        modelBuilder.Entity<Friendship>()
            .HasIndex(friendship => friendship.RecipientId);

        // DM channel member relationships and composite primary key.
        modelBuilder.Entity<DmChannelMember>()
            .HasKey(member => new { member.DmChannelId, member.UserId });

        modelBuilder.Entity<DmChannelMember>()
            .HasOne(member => member.DmChannel)
            .WithMany(channel => channel.Members)
            .HasForeignKey(member => member.DmChannelId);

        modelBuilder.Entity<DmChannelMember>()
            .HasOne(member => member.User)
            .WithMany(user => user.DmChannelMemberships)
            .HasForeignKey(member => member.UserId);

        modelBuilder.Entity<DmChannelMember>()
            .HasIndex(member => member.UserId);

        modelBuilder.Entity<DmChannelMember>()
            .HasIndex(member => member.DmChannelId);

        // Direct message relationships and indexes.
        modelBuilder.Entity<DirectMessage>()
            .HasOne(message => message.DmChannel)
            .WithMany(channel => channel.Messages)
            .HasForeignKey(message => message.DmChannelId);

        modelBuilder.Entity<DirectMessage>()
            .HasOne(message => message.AuthorUser)
            .WithMany(user => user.DirectMessages)
            .HasForeignKey(message => message.AuthorUserId);

        modelBuilder.Entity<DirectMessage>()
            .HasIndex(message => message.DmChannelId);

        modelBuilder.Entity<DirectMessage>()
            .HasIndex(message => message.AuthorUserId);

        // Server invite relationships and constraints.
        modelBuilder.Entity<ServerInvite>()
            .HasOne(invite => invite.Server)
            .WithMany(server => server.Invites)
            .HasForeignKey(invite => invite.ServerId);

        modelBuilder.Entity<ServerInvite>()
            .HasOne(invite => invite.CreatedByUser)
            .WithMany(user => user.CreatedInvites)
            .HasForeignKey(invite => invite.CreatedByUserId);

        modelBuilder.Entity<ServerInvite>()
            .HasIndex(invite => invite.Code)
            .IsUnique();

        modelBuilder.Entity<ServerInvite>()
            .HasIndex(invite => invite.ServerId);

        // Message self-reference for replies.
        modelBuilder.Entity<Message>()
            .HasOne<Message>()
            .WithMany()
            .HasForeignKey(m => m.ReplyToMessageId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Message>()
            .HasIndex(m => m.ReplyToMessageId);

        // DirectMessage self-reference for replies.
        modelBuilder.Entity<DirectMessage>()
            .HasOne<DirectMessage>()
            .WithMany()
            .HasForeignKey(dm => dm.ReplyToDirectMessageId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<DirectMessage>()
            .HasIndex(dm => dm.ReplyToDirectMessageId);

        // VoiceState relationships and indexes.
        modelBuilder.Entity<VoiceState>()
            .HasOne(vs => vs.User)
            .WithMany()
            .HasForeignKey(vs => vs.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<VoiceState>()
            .HasOne(vs => vs.Channel)
            .WithMany()
            .HasForeignKey(vs => vs.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        // A user can only be in one voice channel at a time.
        modelBuilder.Entity<VoiceState>()
            .HasIndex(vs => vs.UserId)
            .IsUnique();

        modelBuilder.Entity<VoiceState>()
            .HasIndex(vs => vs.ChannelId);

        modelBuilder.Entity<VoiceState>()
            .HasIndex(vs => vs.ConnectionId);

        // VoiceCall relationships and indexes.
        modelBuilder.Entity<VoiceCall>()
            .HasOne(vc => vc.DmChannel)
            .WithMany()
            .HasForeignKey(vc => vc.DmChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<VoiceCall>()
            .HasOne(vc => vc.CallerUser)
            .WithMany()
            .HasForeignKey(vc => vc.CallerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<VoiceCall>()
            .HasOne(vc => vc.RecipientUser)
            .WithMany()
            .HasForeignKey(vc => vc.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<VoiceCall>()
            .HasIndex(vc => vc.DmChannelId);

        // Prevent concurrent active/ringing calls on the same DM channel.
        modelBuilder.Entity<VoiceCall>()
            .HasIndex(vc => vc.DmChannelId)
            .HasFilter("\"Status\" IN (0, 1)")
            .IsUnique()
            .HasDatabaseName("IX_VoiceCalls_DmChannelId_ActiveOrRinging");

        modelBuilder.Entity<VoiceCall>()
            .HasIndex(vc => vc.CallerUserId);

        modelBuilder.Entity<VoiceCall>()
            .HasIndex(vc => vc.RecipientUserId);

        // VoiceState -> DmChannel relationship for direct calls.
        modelBuilder.Entity<VoiceState>()
            .HasOne(vs => vs.DmChannel)
            .WithMany()
            .HasForeignKey(vs => vs.DmChannelId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<VoiceState>()
            .HasIndex(vs => vs.DmChannelId);

        // Link preview relationships, indexes, and check constraint.
        modelBuilder.Entity<LinkPreview>(entity =>
        {
            entity.HasOne(lp => lp.Message)
                .WithMany(m => m.LinkPreviews)
                .HasForeignKey(lp => lp.MessageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(lp => lp.DirectMessage)
                .WithMany(dm => dm.LinkPreviews)
                .HasForeignKey(lp => lp.DirectMessageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(lp => lp.MessageId);
            entity.HasIndex(lp => lp.DirectMessageId);

            // Exactly one of MessageId or DirectMessageId must be non-null.
            entity.ToTable(t => t.HasCheckConstraint("CK_LinkPreview_SingleParent",
                "(\"MessageId\" IS NOT NULL AND \"DirectMessageId\" IS NULL) OR (\"MessageId\" IS NULL AND \"DirectMessageId\" IS NOT NULL)"));
        });

        modelBuilder.Entity<CustomEmoji>()
            .HasOne(e => e.Server)
            .WithMany(s => s.CustomEmojis)
            .HasForeignKey(e => e.ServerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CustomEmoji>()
            .HasOne(e => e.UploadedByUser)
            .WithMany()
            .HasForeignKey(e => e.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CustomEmoji>()
            .HasIndex(e => new { e.ServerId, e.Name })
            .IsUnique();

        modelBuilder.Entity<CustomEmoji>()
            .Property(e => e.Name)
            .HasMaxLength(32);

        // PresenceState relationships and indexes.
        modelBuilder.Entity<PresenceState>(entity =>
        {
            entity.HasOne(ps => ps.User)
                .WithMany()
                .HasForeignKey(ps => ps.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(ps => ps.UserId);
            entity.HasIndex(ps => ps.ConnectionId);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasOne(rt => rt.User)
                .WithMany()
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(rt => rt.TokenHash)
                .IsUnique();

            // Use PostgreSQL's xmin system column for optimistic concurrency.
            // This ensures RotateRefreshTokenAsync detects concurrent revocations.
            entity.Property<uint>("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<ChannelCategory>(e =>
        {
            e.HasOne(c => c.Server)
                .WithMany(s => s.Categories)
                .HasForeignKey(c => c.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(c => c.Name).HasMaxLength(50);
        });

        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.HasOne(a => a.Server)
                .WithMany(s => s.AuditLogEntries)
                .HasForeignKey(a => a.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.ActorUser)
                .WithMany()
                .HasForeignKey(a => a.ActorUserId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(a => new { a.ServerId, a.CreatedAt })
                .IsDescending(false, true);
            e.Property(a => a.Action).HasConversion<string>();
        });

        modelBuilder.Entity<ChannelNotificationOverride>(e =>
        {
            e.HasKey(o => new { o.UserId, o.ChannelId });
            e.HasOne(o => o.User)
                .WithMany()
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(o => o.Channel)
                .WithMany()
                .HasForeignKey(o => o.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Message>()
            .Property(m => m.MessageType)
            .HasDefaultValue(MessageType.Regular);

        modelBuilder.Entity<SamlIdentityProvider>(e =>
        {
            e.HasIndex(p => p.EntityId).IsUnique();
            e.Property(p => p.EntityId).HasMaxLength(500);
            e.Property(p => p.DisplayName).HasMaxLength(200);
            e.Property(p => p.SingleSignOnUrl).HasMaxLength(2000);
        });

        modelBuilder.Entity<PinnedMessage>(e =>
        {
            e.HasOne(p => p.Message)
                .WithMany()
                .HasForeignKey(p => p.MessageId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(p => p.Channel)
                .WithMany()
                .HasForeignKey(p => p.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(p => p.PinnedByUser)
                .WithMany()
                .HasForeignKey(p => p.PinnedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(p => new { p.ChannelId, p.MessageId }).IsUnique();
            e.HasIndex(p => new { p.ChannelId, p.PinnedAt });
        });

        modelBuilder.Entity<Webhook>(e =>
        {
            e.HasOne(w => w.Server)
                .WithMany(s => s.Webhooks)
                .HasForeignKey(w => w.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(w => w.CreatedByUser)
                .WithMany()
                .HasForeignKey(w => w.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(w => w.ServerId);
            e.Property(w => w.Name).HasMaxLength(100);
            e.Property(w => w.Url).HasMaxLength(2048);
            e.Property(w => w.Secret).HasMaxLength(256);
        });

        modelBuilder.Entity<WebhookDeliveryLog>(e =>
        {
            e.HasOne(l => l.Webhook)
                .WithMany(w => w.DeliveryLogs)
                .HasForeignKey(l => l.WebhookId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => new { l.WebhookId, l.CreatedAt })
                .IsDescending(false, true);
        });

        modelBuilder.Entity<PushSubscription>(e =>
        {
            e.HasOne(ps => ps.User)
                .WithMany()
                .HasForeignKey(ps => ps.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(ps => ps.UserId);
            e.HasIndex(ps => ps.Endpoint).IsUnique();
        });

        modelBuilder.Entity<BannedMember>(e =>
        {
            e.HasKey(b => new { b.ServerId, b.UserId });
            e.HasOne(b => b.Server)
                .WithMany()
                .HasForeignKey(b => b.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(b => b.User)
                .WithMany()
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(b => b.BannedByUser)
                .WithMany()
                .HasForeignKey(b => b.BannedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(b => b.Reason).HasMaxLength(512);
        });
    }
}
