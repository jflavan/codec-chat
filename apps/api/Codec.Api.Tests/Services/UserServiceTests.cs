using System.Security.Claims;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Codec.Api.Services.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Tests.Services;

public class UserServiceTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly UserService _svc;

    public UserServiceTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);
        _svc = new UserService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static ClaimsPrincipal CreatePrincipal(string sub, string name = "Test", string? email = "test@test.com", string? picture = null)
    {
        var claims = new List<Claim> { new("sub", sub), new("name", name) };
        if (email is not null) claims.Add(new("email", email));
        if (picture is not null) claims.Add(new("picture", picture));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    // --- GetOrCreateUserAsync ---

    [Fact]
    public async Task GetOrCreateUserAsync_CreatesNewUser()
    {
        var principal = CreatePrincipal("google-1", "Alice", "alice@test.com");
        var user = await _svc.GetOrCreateUserAsync(principal);

        user.GoogleSubject.Should().Be("google-1");
        user.DisplayName.Should().Be("Alice");
        user.Email.Should().Be("alice@test.com");
    }

    [Fact]
    public async Task GetOrCreateUserAsync_ReturnsExistingUser()
    {
        _db.Users.Add(new User { GoogleSubject = "google-2", DisplayName = "Bob", Email = "bob@test.com" });
        await _db.SaveChangesAsync();

        var principal = CreatePrincipal("google-2", "Bob", "bob@test.com");
        var user = await _svc.GetOrCreateUserAsync(principal);

        user.DisplayName.Should().Be("Bob");
        _db.Users.Count(u => u.GoogleSubject == "google-2").Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateUserAsync_UpdatesChangedProfile()
    {
        _db.Users.Add(new User { GoogleSubject = "google-3", DisplayName = "OldName", Email = "old@test.com" });
        await _db.SaveChangesAsync();

        var principal = CreatePrincipal("google-3", "NewName", "new@test.com");
        var user = await _svc.GetOrCreateUserAsync(principal);

        user.DisplayName.Should().Be("NewName");
        user.Email.Should().Be("new@test.com");
    }

    [Fact]
    public async Task GetOrCreateUserAsync_MissingSubClaim_Throws()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        await _svc.Invoking(s => s.GetOrCreateUserAsync(principal))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*subject*");
    }

    [Fact]
    public async Task GetOrCreateUserAsync_JoinsDefaultServer()
    {
        // Create default server
        _db.Servers.Add(new Server { Id = Server.DefaultServerId, Name = "Codec HQ" });
        await _db.SaveChangesAsync();

        var principal = CreatePrincipal("google-4", "NewUser");
        var user = await _svc.GetOrCreateUserAsync(principal);

        var membership = await _db.ServerMembers.FirstOrDefaultAsync(m => m.UserId == user.Id && m.ServerId == Server.DefaultServerId);
        membership.Should().NotBeNull();
        membership!.Role.Should().Be(ServerRole.Member);
    }

    // --- GetEffectiveDisplayName ---

    [Fact]
    public void GetEffectiveDisplayName_NoNickname_ReturnsDisplayName()
    {
        var user = new User { DisplayName = "Alice", Nickname = null };
        _svc.GetEffectiveDisplayName(user).Should().Be("Alice");
    }

    [Fact]
    public void GetEffectiveDisplayName_WithNickname_ReturnsNickname()
    {
        var user = new User { DisplayName = "Alice", Nickname = "Ali" };
        _svc.GetEffectiveDisplayName(user).Should().Be("Ali");
    }

    [Fact]
    public void GetEffectiveDisplayName_EmptyNickname_ReturnsDisplayName()
    {
        var user = new User { DisplayName = "Alice", Nickname = "  " };
        _svc.GetEffectiveDisplayName(user).Should().Be("Alice");
    }

    // --- IsMemberAsync ---

    [Fact]
    public async Task IsMemberAsync_IsMember_ReturnsTrue()
    {
        var server = new Server { Name = "Test" };
        var user = new User { GoogleSubject = "g-5", DisplayName = "X" };
        _db.Servers.Add(server);
        _db.Users.Add(user);
        _db.ServerMembers.Add(new ServerMember { Server = server, User = user, Role = ServerRole.Member });
        await _db.SaveChangesAsync();

        (await _svc.IsMemberAsync(server.Id, user.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task IsMemberAsync_NotMember_ReturnsFalse()
    {
        var server = new Server { Name = "Test" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        (await _svc.IsMemberAsync(server.Id, Guid.NewGuid())).Should().BeFalse();
    }

    // --- EnsureMemberAsync ---

    [Fact]
    public async Task EnsureMemberAsync_MemberExists_ReturnsMember()
    {
        var server = new Server { Name = "S" };
        var user = new User { GoogleSubject = "g-6", DisplayName = "X" };
        _db.Servers.Add(server);
        _db.Users.Add(user);
        _db.ServerMembers.Add(new ServerMember { Server = server, User = user, Role = ServerRole.Admin });
        await _db.SaveChangesAsync();

        var member = await _svc.EnsureMemberAsync(server.Id, user.Id);
        member.Role.Should().Be(ServerRole.Admin);
    }

    [Fact]
    public async Task EnsureMemberAsync_NotMember_ThrowsForbidden()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        await _svc.Invoking(s => s.EnsureMemberAsync(server.Id, Guid.NewGuid()))
            .Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task EnsureMemberAsync_ServerNotFound_ThrowsNotFound()
    {
        await _svc.Invoking(s => s.EnsureMemberAsync(Guid.NewGuid(), Guid.NewGuid()))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task EnsureMemberAsync_GlobalAdmin_BypassesMembership()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        var member = await _svc.EnsureMemberAsync(server.Id, Guid.NewGuid(), isGlobalAdmin: true);
        member.Should().NotBeNull();
    }

    // --- EnsureAdminAsync ---

    [Fact]
    public async Task EnsureAdminAsync_AdminRole_Succeeds()
    {
        var server = new Server { Name = "S" };
        var user = new User { GoogleSubject = "g-7", DisplayName = "X" };
        _db.Servers.Add(server);
        _db.Users.Add(user);
        _db.ServerMembers.Add(new ServerMember { Server = server, User = user, Role = ServerRole.Admin });
        await _db.SaveChangesAsync();

        var member = await _svc.EnsureAdminAsync(server.Id, user.Id);
        member.Role.Should().Be(ServerRole.Admin);
    }

    [Fact]
    public async Task EnsureAdminAsync_MemberRole_ThrowsForbidden()
    {
        var server = new Server { Name = "S" };
        var user = new User { GoogleSubject = "g-8", DisplayName = "X" };
        _db.Servers.Add(server);
        _db.Users.Add(user);
        _db.ServerMembers.Add(new ServerMember { Server = server, User = user, Role = ServerRole.Member });
        await _db.SaveChangesAsync();

        await _svc.Invoking(s => s.EnsureAdminAsync(server.Id, user.Id))
            .Should().ThrowAsync<ForbiddenException>();
    }

    // --- EnsureOwnerAsync ---

    [Fact]
    public async Task EnsureOwnerAsync_OwnerRole_Succeeds()
    {
        var server = new Server { Name = "S" };
        var user = new User { GoogleSubject = "g-9", DisplayName = "X" };
        _db.Servers.Add(server);
        _db.Users.Add(user);
        _db.ServerMembers.Add(new ServerMember { Server = server, User = user, Role = ServerRole.Owner });
        await _db.SaveChangesAsync();

        var member = await _svc.EnsureOwnerAsync(server.Id, user.Id);
        member.Role.Should().Be(ServerRole.Owner);
    }

    [Fact]
    public async Task EnsureOwnerAsync_AdminRole_ThrowsForbidden()
    {
        var server = new Server { Name = "S" };
        var user = new User { GoogleSubject = "g-10", DisplayName = "X" };
        _db.Servers.Add(server);
        _db.Users.Add(user);
        _db.ServerMembers.Add(new ServerMember { Server = server, User = user, Role = ServerRole.Admin });
        await _db.SaveChangesAsync();

        await _svc.Invoking(s => s.EnsureOwnerAsync(server.Id, user.Id))
            .Should().ThrowAsync<ForbiddenException>();
    }

    // --- EnsureDmParticipantAsync ---

    [Fact]
    public async Task EnsureDmParticipantAsync_IsParticipant_Succeeds()
    {
        var user = new User { GoogleSubject = "g-11", DisplayName = "X" };
        var channel = new DmChannel();
        _db.Users.Add(user);
        _db.DmChannels.Add(channel);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel, User = user });
        await _db.SaveChangesAsync();

        await _svc.Invoking(s => s.EnsureDmParticipantAsync(channel.Id, user.Id))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureDmParticipantAsync_NotParticipant_ThrowsForbidden()
    {
        var channel = new DmChannel();
        _db.DmChannels.Add(channel);
        await _db.SaveChangesAsync();

        await _svc.Invoking(s => s.EnsureDmParticipantAsync(channel.Id, Guid.NewGuid()))
            .Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task EnsureDmParticipantAsync_ChannelNotFound_ThrowsNotFound()
    {
        await _svc.Invoking(s => s.EnsureDmParticipantAsync(Guid.NewGuid(), Guid.NewGuid()))
            .Should().ThrowAsync<NotFoundException>();
    }
}
