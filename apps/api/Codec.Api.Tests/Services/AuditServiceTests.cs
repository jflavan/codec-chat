using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Tests.Services;

public class AuditServiceTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly AuditService _service;

    public AuditServiceTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);
        _service = new AuditService(_db);
    }

    [Fact]
    public async Task LogAsync_CreatesEntry()
    {
        var serverId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await _service.LogAsync(serverId, actorId, AuditAction.ServerRenamed,
            "Server", serverId.ToString(), "Renamed to New Name");

        var entries = await _db.AuditLogEntries.ToListAsync();
        entries.Should().HaveCount(1);
        entries[0].ServerId.Should().Be(serverId);
        entries[0].ActorUserId.Should().Be(actorId);
        entries[0].Action.Should().Be(AuditAction.ServerRenamed);
        entries[0].Details.Should().Be("Renamed to New Name");
    }

    public void Dispose() => _db.Dispose();
}
