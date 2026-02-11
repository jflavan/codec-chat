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
