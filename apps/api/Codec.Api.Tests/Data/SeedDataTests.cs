using Codec.Api.Data;
using Codec.Api.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Tests.Data;

public class SeedDataTests : IDisposable
{
    private readonly CodecDbContext _db;

    public SeedDataTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    // ── EnsureDefaultServerAsync ──

    [Fact]
    public async Task EnsureDefaultServerAsync_CreatesDefaultServer()
    {
        await SeedData.EnsureDefaultServerAsync(_db);

        var server = await _db.Servers.FindAsync(Server.DefaultServerId);
        server.Should().NotBeNull();
        server!.Name.Should().Be("Codec HQ");
    }

    [Fact]
    public async Task EnsureDefaultServerAsync_CreatesChannels()
    {
        await SeedData.EnsureDefaultServerAsync(_db);

        var channels = await _db.Channels
            .Where(c => c.ServerId == Server.DefaultServerId)
            .OrderBy(c => c.Name)
            .ToListAsync();

        channels.Should().HaveCount(2);
        channels.Select(c => c.Name).Should().Contain("general");
        channels.Select(c => c.Name).Should().Contain("announcements");
    }

    [Fact]
    public async Task EnsureDefaultServerAsync_CreatesDefaultRoles()
    {
        await SeedData.EnsureDefaultServerAsync(_db);

        var roles = await _db.ServerRoles
            .Where(r => r.ServerId == Server.DefaultServerId)
            .ToListAsync();

        roles.Should().HaveCountGreaterThanOrEqualTo(3);
        roles.Select(r => r.Name).Should().Contain("Owner");
        roles.Select(r => r.Name).Should().Contain("Admin");
        roles.Select(r => r.Name).Should().Contain("Member");
    }

    [Fact]
    public async Task EnsureDefaultServerAsync_Idempotent_DoesNotDuplicate()
    {
        await SeedData.EnsureDefaultServerAsync(_db);
        await SeedData.EnsureDefaultServerAsync(_db);

        var servers = await _db.Servers.Where(s => s.Id == Server.DefaultServerId).CountAsync();
        servers.Should().Be(1);

        var channels = await _db.Channels.Where(c => c.ServerId == Server.DefaultServerId).CountAsync();
        channels.Should().Be(2);
    }

    [Fact]
    public async Task EnsureDefaultServerAsync_ExistingServerWithoutRoles_CreatesRoles()
    {
        // Pre-create server without roles
        _db.Servers.Add(new Server { Id = Server.DefaultServerId, Name = "Codec HQ" });
        await _db.SaveChangesAsync();

        var rolesBefore = await _db.ServerRoles
            .Where(r => r.ServerId == Server.DefaultServerId).CountAsync();
        rolesBefore.Should().Be(0);

        await SeedData.EnsureDefaultServerAsync(_db);

        var rolesAfter = await _db.ServerRoles
            .Where(r => r.ServerId == Server.DefaultServerId).CountAsync();
        rolesAfter.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task EnsureDefaultServerAsync_ExistingServerWithRoles_DoesNotDuplicateRoles()
    {
        await SeedData.EnsureDefaultServerAsync(_db);

        var roleCountBefore = await _db.ServerRoles
            .Where(r => r.ServerId == Server.DefaultServerId).CountAsync();

        await SeedData.EnsureDefaultServerAsync(_db);

        var roleCountAfter = await _db.ServerRoles
            .Where(r => r.ServerId == Server.DefaultServerId).CountAsync();

        roleCountAfter.Should().Be(roleCountBefore);
    }

    // ── EnsureGlobalAdminAsync ──

    [Fact]
    public async Task EnsureGlobalAdminAsync_NullEmail_DoesNothing()
    {
        await SeedData.EnsureGlobalAdminAsync(_db, null);
        // No exception is the success condition
    }

    [Fact]
    public async Task EnsureGlobalAdminAsync_EmptyEmail_DoesNothing()
    {
        await SeedData.EnsureGlobalAdminAsync(_db, "");
        await SeedData.EnsureGlobalAdminAsync(_db, "   ");
    }

    [Fact]
    public async Task EnsureGlobalAdminAsync_UserNotFound_DoesNothing()
    {
        await SeedData.EnsureGlobalAdminAsync(_db, "nobody@example.com");
    }

    [Fact]
    public async Task EnsureGlobalAdminAsync_PromotesUser()
    {
        var user = new User { GoogleSubject = "g-admin", DisplayName = "Admin", Email = "admin@example.com" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await SeedData.EnsureGlobalAdminAsync(_db, "admin@example.com");

        var dbUser = await _db.Users.FirstAsync(u => u.Email == "admin@example.com");
        dbUser.IsGlobalAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureGlobalAdminAsync_AlreadyAdmin_DoesNotThrow()
    {
        var user = new User { GoogleSubject = "g-admin", DisplayName = "Admin", Email = "admin@example.com", IsGlobalAdmin = true };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await SeedData.EnsureGlobalAdminAsync(_db, "admin@example.com");

        var dbUser = await _db.Users.FirstAsync(u => u.Email == "admin@example.com");
        dbUser.IsGlobalAdmin.Should().BeTrue();
    }

    // ── InitializeAsync ──

    [Fact]
    public async Task InitializeAsync_CreatesDevSeedUsers()
    {
        // Must create default server first
        await SeedData.EnsureDefaultServerAsync(_db);

        await SeedData.InitializeAsync(_db);

        var users = await _db.Users.ToListAsync();
        users.Should().HaveCountGreaterThanOrEqualTo(3);
        users.Select(u => u.DisplayName).Should().Contain("Avery");
        users.Select(u => u.DisplayName).Should().Contain("Morgan");
        users.Select(u => u.DisplayName).Should().Contain("Rae");
    }

    [Fact]
    public async Task InitializeAsync_UsersAlreadyExist_DoesNothing()
    {
        await SeedData.EnsureDefaultServerAsync(_db);

        _db.Users.Add(new User { GoogleSubject = "existing", DisplayName = "Existing" });
        await _db.SaveChangesAsync();

        var countBefore = await _db.Users.CountAsync();

        await SeedData.InitializeAsync(_db);

        var countAfter = await _db.Users.CountAsync();
        countAfter.Should().Be(countBefore);
    }

    [Fact]
    public async Task InitializeAsync_CreatesMemberships()
    {
        await SeedData.EnsureDefaultServerAsync(_db);
        await SeedData.InitializeAsync(_db);

        var memberships = await _db.ServerMembers
            .Where(m => m.ServerId == Server.DefaultServerId)
            .ToListAsync();

        memberships.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task InitializeAsync_CreatesBuildLogChannelAndMessages()
    {
        await SeedData.EnsureDefaultServerAsync(_db);
        await SeedData.InitializeAsync(_db);

        var buildLog = await _db.Channels
            .FirstOrDefaultAsync(c => c.ServerId == Server.DefaultServerId && c.Name == "build-log");
        buildLog.Should().NotBeNull();

        var messages = await _db.Messages
            .Where(m => m.ChannelId == buildLog!.Id)
            .ToListAsync();
        messages.Should().HaveCountGreaterThanOrEqualTo(2);
    }
}
