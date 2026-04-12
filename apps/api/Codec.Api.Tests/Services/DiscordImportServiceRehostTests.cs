using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Codec.Api.Tests.Services;

public class DiscordImportServiceRehostTests
{
    private readonly Mock<IHubContext<ChatHub>> _hubMock = new();
    private readonly Mock<ILogger<DiscordImportService>> _loggerMock = new();
    private readonly Mock<IClientProxy> _groupMock = new();
    private readonly Mock<DiscordMediaRehostService> _rehostMock;

    public DiscordImportServiceRehostTests()
    {
        // DiscordMediaRehostService requires HttpClient, IFileStorageService, ILogger — provide dummies
        _rehostMock = new Mock<DiscordMediaRehostService>(
            new HttpClient(),
            Mock.Of<IFileStorageService>(),
            Mock.Of<ILogger<DiscordMediaRehostService>>());

        _groupMock.Setup(g => g.SendCoreAsync(
            It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private CodecDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CodecDbContext(options);
    }

    private DiscordImportService CreateService()
    {
        var scopeMock = new Mock<IServiceScope>();
        var spMock = new Mock<IServiceProvider>();
        spMock.Setup(sp => sp.GetService(typeof(DiscordMediaRehostService)))
            .Returns(_rehostMock.Object);
        scopeMock.Setup(s => s.ServiceProvider).Returns(spMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        return new DiscordImportService(scopeFactoryMock.Object, _hubMock.Object, _loggerMock.Object);
    }

    private static (Guid serverId, Guid importId) SeedServerAndImport(CodecDbContext db)
    {
        var serverId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        db.Servers.Add(new Server { Id = serverId, Name = "Test Server" });
        db.DiscordImports.Add(new DiscordImport
        {
            Id = importId,
            ServerId = serverId,
            DiscordGuildId = "123",
            InitiatedByUserId = Guid.NewGuid(),
            Status = DiscordImportStatus.RehostingMedia
        });
        db.SaveChanges();
        return (serverId, importId);
    }

    private void SeedEmoji(CodecDbContext db, Guid serverId, Guid importId, Guid emojiId, string imageUrl)
    {
        db.CustomEmojis.Add(new CustomEmoji
        {
            Id = emojiId,
            ServerId = serverId,
            Name = $"emoji_{emojiId.ToString()[..8]}",
            ImageUrl = imageUrl,
            ContentType = "image/png"
        });
        db.DiscordEntityMappings.Add(new DiscordEntityMapping
        {
            Id = Guid.NewGuid(),
            DiscordImportId = importId,
            ServerId = serverId,
            DiscordEntityId = emojiId.ToString()[..8],
            EntityType = DiscordEntityType.Emoji,
            CodecEntityId = emojiId
        });
        db.SaveChanges();
    }

    private static Guid SeedMessageWithImage(CodecDbContext db, Guid channelId, string imageUrl)
    {
        var msg = new Message
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            AuthorName = "testuser",
            Body = "test",
            ImageUrl = imageUrl,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Messages.Add(msg);
        db.SaveChanges();
        return msg.Id;
    }

    private static Guid SeedMessageWithFile(CodecDbContext db, Guid channelId, string fileUrl)
    {
        var msg = new Message
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            AuthorName = "testuser",
            Body = "test",
            FileUrl = fileUrl,
            FileName = "attachment.pdf",
            FileContentType = "application/pdf",
            FileSize = 1024,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Messages.Add(msg);
        db.SaveChanges();
        return msg.Id;
    }

    // ========== RehostEmojisAsync tests ==========

    [Fact]
    public async Task RehostEmojisAsync_NoEmojisToRehost_ReturnsWithoutCreatingScope()
    {
        using var db = CreateDb();
        var (serverId, importId) = SeedServerAndImport(db);

        // No emojis seeded — method should return early
        var service = CreateService();
        await service.RehostEmojisAsync(db, serverId, importId, _groupMock.Object, CancellationToken.None);

        // RehostImageAsync should never be called
        _rehostMock.Verify(r => r.RehostImageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RehostEmojisAsync_AllEmojisSucceed_UpdatesImageUrls()
    {
        using var db = CreateDb();
        var (serverId, importId) = SeedServerAndImport(db);

        var emojiIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var id in emojiIds)
            SeedEmoji(db, serverId, importId, id, $"https://cdn.discordapp.com/emojis/{id}.png");

        var callIndex = 0;
        _rehostMock.Setup(r => r.RehostImageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callIndex++;
                return RehostResult.Success($"https://codec.chat/uploads/emojis/import/rehosted_{callIndex}.png");
            });

        var service = CreateService();
        await service.RehostEmojisAsync(db, serverId, importId, _groupMock.Object, CancellationToken.None);

        // Verify all emojis have updated URLs
        foreach (var id in emojiIds)
        {
            var emoji = await db.CustomEmojis.FindAsync(id);
            Assert.NotNull(emoji);
            Assert.StartsWith("https://codec.chat/uploads/", emoji.ImageUrl);
            Assert.DoesNotContain("cdn.discordapp.com", emoji.ImageUrl);
        }
    }

    [Fact]
    public async Task RehostEmojisAsync_SkippedDoesNotCountAsFailure()
    {
        using var db = CreateDb();
        var (serverId, importId) = SeedServerAndImport(db);

        var emojiId = Guid.NewGuid();
        var originalUrl = $"https://cdn.discordapp.com/emojis/{emojiId}.gif";
        SeedEmoji(db, serverId, importId, emojiId, originalUrl);

        _rehostMock.Setup(r => r.RehostImageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RehostResult.Skipped);

        var service = CreateService();
        // Should not throw
        await service.RehostEmojisAsync(db, serverId, importId, _groupMock.Object, CancellationToken.None);

        var emoji = await db.CustomEmojis.FindAsync(emojiId);
        Assert.NotNull(emoji);
        // Skipped emojis keep their original URL
        Assert.Equal(originalUrl, emoji.ImageUrl);
    }

    [Fact]
    public async Task RehostEmojisAsync_TenConsecutiveFailures_ThrowsInvalidOperationException()
    {
        using var db = CreateDb();
        var (serverId, importId) = SeedServerAndImport(db);

        // Seed 10 emojis
        for (var i = 0; i < 10; i++)
            SeedEmoji(db, serverId, importId, Guid.NewGuid(), $"https://cdn.discordapp.com/emojis/{i}.png");

        _rehostMock.Setup(r => r.RehostImageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RehostResult.Failed);

        var service = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RehostEmojisAsync(db, serverId, importId, _groupMock.Object, CancellationToken.None));
    }

    [Fact]
    public async Task RehostEmojisAsync_FailureResetsOnSuccess_NoThrow()
    {
        using var db = CreateDb();
        var (serverId, importId) = SeedServerAndImport(db);

        // Seed 11 emojis: first 9 fail, 10th succeeds, 11th fails — should not throw
        for (var i = 0; i < 11; i++)
            SeedEmoji(db, serverId, importId, Guid.NewGuid(), $"https://cdn.discordapp.com/emojis/{i}.png");

        var callCount = 0;
        _rehostMock.Setup(r => r.RehostImageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                // 10th call succeeds (resets counter), all others fail
                return callCount == 10
                    ? RehostResult.Success("https://codec.chat/uploads/emojis/import/ok.png")
                    : RehostResult.Failed;
            });

        var service = CreateService();
        // Should not throw because the 10th call resets consecutiveFailures
        await service.RehostEmojisAsync(db, serverId, importId, _groupMock.Object, CancellationToken.None);
    }

    // ========== RehostAttachmentsAsync tests ==========

    [Fact]
    public async Task RehostAttachmentsAsync_NoMessagesToRehost_ReturnsWithoutCreatingScope()
    {
        using var db = CreateDb();
        var (serverId, _) = SeedServerAndImport(db);

        // Add a channel but no messages
        db.Channels.Add(new Channel { Id = Guid.NewGuid(), ServerId = serverId, Name = "general" });
        await db.SaveChangesAsync();

        var service = CreateService();
        await service.RehostAttachmentsAsync(db, serverId, _groupMock.Object, CancellationToken.None);

        _rehostMock.Verify(r => r.RehostImageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RehostAttachmentsAsync_AllMessagesSucceed_UpdatesImageUrls()
    {
        using var db = CreateDb();
        var (serverId, _) = SeedServerAndImport(db);

        var channelId = Guid.NewGuid();
        db.Channels.Add(new Channel { Id = channelId, ServerId = serverId, Name = "general" });
        await db.SaveChangesAsync();

        var msgIds = new[]
        {
            SeedMessageWithImage(db, channelId, "https://cdn.discordapp.com/attachments/1/a.png"),
            SeedMessageWithImage(db, channelId, "https://cdn.discordapp.com/attachments/2/b.png"),
            SeedMessageWithImage(db, channelId, "https://cdn.discordapp.com/attachments/3/c.png")
        };

        var callIndex = 0;
        _rehostMock.Setup(r => r.RehostImageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callIndex++;
                return RehostResult.Success($"https://codec.chat/uploads/images/import/img_{callIndex}.png");
            });

        var service = CreateService();
        await service.RehostAttachmentsAsync(db, serverId, _groupMock.Object, CancellationToken.None);

        foreach (var id in msgIds)
        {
            var msg = await db.Messages.FindAsync(id);
            Assert.NotNull(msg);
            Assert.StartsWith("https://codec.chat/uploads/", msg.ImageUrl);
            Assert.DoesNotContain("cdn.discordapp.com", msg.ImageUrl!);
        }
    }

    [Fact]
    public async Task RehostAttachmentsAsync_FailedClearsImageUrl()
    {
        using var db = CreateDb();
        var (serverId, _) = SeedServerAndImport(db);

        var channelId = Guid.NewGuid();
        db.Channels.Add(new Channel { Id = channelId, ServerId = serverId, Name = "general" });
        await db.SaveChangesAsync();

        var msgId = SeedMessageWithImage(db, channelId, "https://cdn.discordapp.com/attachments/1/a.png");

        _rehostMock.Setup(r => r.RehostImageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RehostResult.Failed);

        var service = CreateService();
        // Only 1 message, so will not hit 10 consecutive failures
        await service.RehostAttachmentsAsync(db, serverId, _groupMock.Object, CancellationToken.None);

        var msg = await db.Messages.FindAsync(msgId);
        Assert.NotNull(msg);
        Assert.Null(msg.ImageUrl);
    }

    [Fact]
    public async Task RehostAttachmentsAsync_SkippedClearsImageUrl()
    {
        using var db = CreateDb();
        var (serverId, _) = SeedServerAndImport(db);

        var channelId = Guid.NewGuid();
        db.Channels.Add(new Channel { Id = channelId, ServerId = serverId, Name = "general" });
        await db.SaveChangesAsync();

        var msgId = SeedMessageWithImage(db, channelId, "https://cdn.discordapp.com/attachments/1/a.png");

        _rehostMock.Setup(r => r.RehostImageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RehostResult.Skipped);

        var service = CreateService();
        await service.RehostAttachmentsAsync(db, serverId, _groupMock.Object, CancellationToken.None);

        var msg = await db.Messages.FindAsync(msgId);
        Assert.NotNull(msg);
        Assert.Null(msg.ImageUrl);
    }

    [Fact]
    public async Task RehostAttachmentsAsync_TenConsecutiveFailures_ThrowsInvalidOperationException()
    {
        using var db = CreateDb();
        var (serverId, _) = SeedServerAndImport(db);

        var channelId = Guid.NewGuid();
        db.Channels.Add(new Channel { Id = channelId, ServerId = serverId, Name = "general" });
        await db.SaveChangesAsync();

        for (var i = 0; i < 10; i++)
            SeedMessageWithImage(db, channelId, $"https://cdn.discordapp.com/attachments/{i}/img.png");

        _rehostMock.Setup(r => r.RehostImageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RehostResult.Failed);

        var service = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RehostAttachmentsAsync(db, serverId, _groupMock.Object, CancellationToken.None));
    }

    [Fact]
    public async Task RehostAttachmentsAsync_BatchLoopTerminates_AfterProcessingAll()
    {
        using var db = CreateDb();
        var (serverId, _) = SeedServerAndImport(db);

        var channelId = Guid.NewGuid();
        db.Channels.Add(new Channel { Id = channelId, ServerId = serverId, Name = "general" });
        await db.SaveChangesAsync();

        // Seed exactly 3 messages — should process all and then exit the loop
        for (var i = 0; i < 3; i++)
            SeedMessageWithImage(db, channelId, $"https://cdn.discordapp.com/attachments/{i}/img.png");

        _rehostMock.Setup(r => r.RehostImageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RehostResult.Success("https://codec.chat/uploads/images/import/ok.png"));

        var service = CreateService();
        await service.RehostAttachmentsAsync(db, serverId, _groupMock.Object, CancellationToken.None);

        // All messages should have been rehosted (no more CDN URLs)
        var remaining = await db.Messages
            .Where(m => m.ImageUrl != null && m.ImageUrl.Contains("cdn.discordapp.com"))
            .CountAsync();
        Assert.Equal(0, remaining);

        // Exactly 3 calls to rehost
        _rehostMock.Verify(r => r.RehostImageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    // ========== RehostFileAttachmentsAsync tests ==========

    [Fact]
    public async Task RehostFileAttachmentsAsync_NoFilesToRehost_ReturnsWithoutCreatingScope()
    {
        using var db = CreateDb();
        var (serverId, _) = SeedServerAndImport(db);

        db.Channels.Add(new Channel { Id = Guid.NewGuid(), ServerId = serverId, Name = "general" });
        await db.SaveChangesAsync();

        var service = CreateService();
        await service.RehostFileAttachmentsAsync(db, serverId, _groupMock.Object, CancellationToken.None);

        _rehostMock.Verify(r => r.RehostFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RehostFileAttachmentsAsync_AllFilesSucceed_UpdatesFileUrls()
    {
        using var db = CreateDb();
        var (serverId, _) = SeedServerAndImport(db);

        var channelId = Guid.NewGuid();
        db.Channels.Add(new Channel { Id = channelId, ServerId = serverId, Name = "general" });
        await db.SaveChangesAsync();

        var msgIds = new[]
        {
            SeedMessageWithFile(db, channelId, "https://cdn.discordapp.com/attachments/1/a.pdf"),
            SeedMessageWithFile(db, channelId, "https://cdn.discordapp.com/attachments/2/b.zip"),
            SeedMessageWithFile(db, channelId, "https://cdn.discordapp.com/attachments/3/c.docx")
        };

        var callIndex = 0;
        _rehostMock.Setup(r => r.RehostFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callIndex++;
                return RehostResult.Success($"https://codec.chat/uploads/files/import/file_{callIndex}.pdf");
            });

        var service = CreateService();
        await service.RehostFileAttachmentsAsync(db, serverId, _groupMock.Object, CancellationToken.None);

        foreach (var id in msgIds)
        {
            var msg = await db.Messages.FindAsync(id);
            Assert.NotNull(msg);
            Assert.StartsWith("https://codec.chat/uploads/", msg.FileUrl);
            Assert.DoesNotContain("cdn.discordapp.com", msg.FileUrl!);
        }
    }

    [Fact]
    public async Task RehostFileAttachmentsAsync_FailedClearsFileUrl()
    {
        using var db = CreateDb();
        var (serverId, _) = SeedServerAndImport(db);

        var channelId = Guid.NewGuid();
        db.Channels.Add(new Channel { Id = channelId, ServerId = serverId, Name = "general" });
        await db.SaveChangesAsync();

        var msgId = SeedMessageWithFile(db, channelId, "https://cdn.discordapp.com/attachments/1/a.pdf");

        _rehostMock.Setup(r => r.RehostFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RehostResult.Failed);

        var service = CreateService();
        await service.RehostFileAttachmentsAsync(db, serverId, _groupMock.Object, CancellationToken.None);

        var msg = await db.Messages.FindAsync(msgId);
        Assert.NotNull(msg);
        Assert.Null(msg.FileUrl);
    }

    [Fact]
    public async Task RehostFileAttachmentsAsync_SkippedClearsFileUrl()
    {
        using var db = CreateDb();
        var (serverId, _) = SeedServerAndImport(db);

        var channelId = Guid.NewGuid();
        db.Channels.Add(new Channel { Id = channelId, ServerId = serverId, Name = "general" });
        await db.SaveChangesAsync();

        var msgId = SeedMessageWithFile(db, channelId, "https://cdn.discordapp.com/attachments/1/a.pdf");

        _rehostMock.Setup(r => r.RehostFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RehostResult.Skipped);

        var service = CreateService();
        await service.RehostFileAttachmentsAsync(db, serverId, _groupMock.Object, CancellationToken.None);

        var msg = await db.Messages.FindAsync(msgId);
        Assert.NotNull(msg);
        Assert.Null(msg.FileUrl);
    }

    [Fact]
    public async Task RehostFileAttachmentsAsync_TenConsecutiveFailures_ThrowsInvalidOperationException()
    {
        using var db = CreateDb();
        var (serverId, _) = SeedServerAndImport(db);

        var channelId = Guid.NewGuid();
        db.Channels.Add(new Channel { Id = channelId, ServerId = serverId, Name = "general" });
        await db.SaveChangesAsync();

        for (var i = 0; i < 10; i++)
            SeedMessageWithFile(db, channelId, $"https://cdn.discordapp.com/attachments/{i}/file.pdf");

        _rehostMock.Setup(r => r.RehostFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RehostResult.Failed);

        var service = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RehostFileAttachmentsAsync(db, serverId, _groupMock.Object, CancellationToken.None));
    }

    [Fact]
    public async Task RehostFileAttachmentsAsync_BatchLoopTerminates_AfterProcessingAll()
    {
        using var db = CreateDb();
        var (serverId, _) = SeedServerAndImport(db);

        var channelId = Guid.NewGuid();
        db.Channels.Add(new Channel { Id = channelId, ServerId = serverId, Name = "general" });
        await db.SaveChangesAsync();

        for (var i = 0; i < 3; i++)
            SeedMessageWithFile(db, channelId, $"https://cdn.discordapp.com/attachments/{i}/file.pdf");

        _rehostMock.Setup(r => r.RehostFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RehostResult.Success("https://codec.chat/uploads/files/import/ok.pdf"));

        var service = CreateService();
        await service.RehostFileAttachmentsAsync(db, serverId, _groupMock.Object, CancellationToken.None);

        var remaining = await db.Messages
            .Where(m => m.FileUrl != null && m.FileUrl.Contains("cdn.discordapp.com"))
            .CountAsync();
        Assert.Equal(0, remaining);

        _rehostMock.Verify(r => r.RehostFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task RehostFileAttachmentsAsync_IgnoresMessagesWithOnlyImageUrl()
    {
        using var db = CreateDb();
        var (serverId, _) = SeedServerAndImport(db);

        var channelId = Guid.NewGuid();
        db.Channels.Add(new Channel { Id = channelId, ServerId = serverId, Name = "general" });
        await db.SaveChangesAsync();

        // Seed a message with ImageUrl (not FileUrl) — should not be processed by file rehost
        SeedMessageWithImage(db, channelId, "https://cdn.discordapp.com/attachments/1/image.png");

        var service = CreateService();
        await service.RehostFileAttachmentsAsync(db, serverId, _groupMock.Object, CancellationToken.None);

        _rehostMock.Verify(r => r.RehostFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
