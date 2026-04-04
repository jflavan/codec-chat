using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Tests.Services;

public class AdminActionServiceTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly AdminActionService _service;

    public AdminActionServiceTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);
        _service = new AdminActionService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task LogAsync_CreatesAdminAction()
    {
        var actorId = Guid.NewGuid();
        await _service.LogAsync(actorId, AdminActionType.UserDisabled, "User", Guid.NewGuid().ToString(), "Spam account");

        var actions = await _db.AdminActions.ToListAsync();
        actions.Should().HaveCount(1);
        actions[0].ActorUserId.Should().Be(actorId);
        actions[0].ActionType.Should().Be(AdminActionType.UserDisabled);
        actions[0].Reason.Should().Be("Spam account");
    }
}
