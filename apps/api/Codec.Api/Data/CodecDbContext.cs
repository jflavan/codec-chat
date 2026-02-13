using Codec.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Server>()
            .HasMany(server => server.Channels)
            .WithOne(channel => channel.Server)
            .HasForeignKey(channel => channel.ServerId);

        modelBuilder.Entity<Channel>()
            .HasMany(channel => channel.Messages)
            .WithOne(message => message.Channel)
            .HasForeignKey(message => message.ChannelId);

        modelBuilder.Entity<User>()
            .HasIndex(user => user.GoogleSubject)
            .IsUnique();

        modelBuilder.Entity<User>()
            .Property(user => user.Nickname)
            .HasMaxLength(32);

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

        modelBuilder.Entity<Reaction>()
            .HasOne(reaction => reaction.Message)
            .WithMany(message => message.Reactions)
            .HasForeignKey(reaction => reaction.MessageId);

        modelBuilder.Entity<Reaction>()
            .HasOne(reaction => reaction.User)
            .WithMany(user => user.Reactions)
            .HasForeignKey(reaction => reaction.UserId);

        // Each user can react with a given emoji on a message at most once.
        modelBuilder.Entity<Reaction>()
            .HasIndex(reaction => new { reaction.MessageId, reaction.UserId, reaction.Emoji })
            .IsUnique();

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

        // SQLite does not natively support DateTimeOffset ordering.
        // Store as ISO 8601 strings so ORDER BY works correctly.
        var dateTimeOffsetConverter = new DateTimeOffsetToStringConverter();
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(dateTimeOffsetConverter);
                }
            }
        }
    }
}
