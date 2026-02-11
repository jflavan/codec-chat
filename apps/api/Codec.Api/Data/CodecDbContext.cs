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
    }
}
