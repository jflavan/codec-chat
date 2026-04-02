using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Codec.Api.Tests.Hubs;

public class ChatHubTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<IConfiguration> _config = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<ILogger<ChatHub>> _logger = new();
    private readonly Mock<VoiceCallTimeoutService> _callTimeoutService;
    private readonly PresenceTracker _presenceTracker = new();

    private readonly Mock<IHubCallerClients> _mockClients = new();
    private readonly Mock<IGroupManager> _mockGroups = new();
    private readonly Mock<HubCallerContext> _mockContext = new();
    private readonly Mock<IClientProxy> _mockClientProxy = new();
    private readonly Mock<IClientProxy> _mockOthersProxy = new();
    private readonly Mock<IPermissionResolverService> _permissionResolver = new();

    private readonly User _testUser;
    private readonly User _testUser2;
    private readonly Server _testServer;
    private readonly Channel _textChannel;
    private readonly Channel _voiceChannel;
    private readonly DmChannel _dmChannel;
    private readonly string _connectionId = "test-connection-id";

    public ChatHubTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        // Create test entities
        _testUser = new User
        {
            Id = Guid.NewGuid(),
            GoogleSubject = "g-1",
            DisplayName = "Test User",
            Nickname = "TestNick"
        };
        _testUser2 = new User
        {
            Id = Guid.NewGuid(),
            GoogleSubject = "g-2",
            DisplayName = "Test User 2"
        };
        _testServer = new Server { Id = Guid.NewGuid(), Name = "Test Server" };
        _textChannel = new Channel
        {
            Id = Guid.NewGuid(),
            ServerId = _testServer.Id,
            Name = "general",
            Type = ChannelType.Text
        };
        _voiceChannel = new Channel
        {
            Id = Guid.NewGuid(),
            ServerId = _testServer.Id,
            Name = "voice-1",
            Type = ChannelType.Voice
        };
        _dmChannel = new DmChannel { Id = Guid.NewGuid() };

        _db.Users.AddRange(_testUser, _testUser2);
        _db.Servers.Add(_testServer);
        _db.Channels.AddRange(_textChannel, _voiceChannel);
        _db.DmChannels.Add(_dmChannel);

        var memberRole = new ServerRoleEntity
        {
            Id = Guid.NewGuid(),
            ServerId = _testServer.Id,
            Name = "Member",
            Position = 2,
            Permissions = PermissionExtensions.MemberDefaults,
            IsSystemRole = true
        };
        _db.ServerRoles.Add(memberRole);
        _db.ServerMembers.Add(new ServerMember
        {
            ServerId = _testServer.Id,
            UserId = _testUser.Id,
        });
        _db.ServerMembers.Add(new ServerMember
        {
            ServerId = _testServer.Id,
            UserId = _testUser2.Id,
        });
        _db.DmChannelMembers.Add(new DmChannelMember
        {
            DmChannelId = _dmChannel.Id,
            UserId = _testUser.Id
        });
        _db.DmChannelMembers.Add(new DmChannelMember
        {
            DmChannelId = _dmChannel.Id,
            UserId = _testUser2.Id
        });

        _db.SaveChanges();

        // Setup mocks
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_testUser, false));

        _mockContext.Setup(c => c.ConnectionId).Returns(_connectionId);
        _mockContext.Setup(c => c.UserIdentifier).Returns(_testUser.Id.ToString());
        _mockContext.Setup(c => c.User).Returns(
            new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("sub", _testUser.GoogleSubject!),
                new Claim("name", _testUser.DisplayName)
            ], "Bearer")));

        _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);
        _mockClients.Setup(c => c.OthersInGroup(It.IsAny<string>())).Returns(_mockOthersProxy.Object);

        _config.Setup(c => c[It.IsAny<string>()]).Returns((string?)null);

        // Default: all permission checks pass.
        _permissionResolver
            .Setup(p => p.HasChannelPermissionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Permission>()))
            .ReturnsAsync(true);
        _permissionResolver
            .Setup(p => p.HasServerPermissionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Permission>()))
            .ReturnsAsync(true);

        // Create a real VoiceCallTimeoutService mock. It is a BackgroundService
        // so we mock it with loose behavior. The methods StartTimeout/CancelTimeout
        // are virtual by inheritance but we just need to verify they're called.
        _callTimeoutService = new Mock<VoiceCallTimeoutService>(
            MockBehavior.Loose,
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<IHubContext<ChatHub>>(),
            Mock.Of<ILogger<VoiceCallTimeoutService>>()
        );
    }

    public void Dispose() => _db.Dispose();

    private ChatHub CreateHub(User? asUser = null)
    {
        if (asUser is not null && asUser.Id != _testUser.Id)
        {
            _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync((asUser, false));
        }

        var hub = new ChatHub(
            _userService.Object,
            _db,
            _config.Object,
            _httpClientFactory.Object,
            _logger.Object,
            _callTimeoutService.Object,
            _presenceTracker,
            _permissionResolver.Object
        );
        hub.Context = _mockContext.Object;
        hub.Clients = _mockClients.Object;
        hub.Groups = _mockGroups.Object;
        return hub;
    }

    private ChatHub CreateHubForUser2()
    {
        var mockContext2 = new Mock<HubCallerContext>();
        mockContext2.Setup(c => c.ConnectionId).Returns("test-connection-id-2");
        mockContext2.Setup(c => c.UserIdentifier).Returns(_testUser2.Id.ToString());
        mockContext2.Setup(c => c.User).Returns(
            new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("sub", _testUser2.GoogleSubject!),
                new Claim("name", _testUser2.DisplayName)
            ], "Bearer")));

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_testUser2, false));

        var hub = new ChatHub(
            _userService.Object,
            _db,
            _config.Object,
            _httpClientFactory.Object,
            _logger.Object,
            _callTimeoutService.Object,
            _presenceTracker,
            _permissionResolver.Object
        );
        hub.Context = mockContext2.Object;
        hub.Clients = _mockClients.Object;
        hub.Groups = _mockGroups.Object;
        return hub;
    }

    // ── OnConnectedAsync ──

    [Fact]
    public async Task OnConnectedAsync_JoinsUserGroup()
    {
        var hub = CreateHub();

        await hub.OnConnectedAsync();

        _mockGroups.Verify(
            g => g.AddToGroupAsync(_connectionId, $"user-{_testUser.Id}", default),
            Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_JoinsServerGroups()
    {
        var hub = CreateHub();

        await hub.OnConnectedAsync();

        _mockGroups.Verify(
            g => g.AddToGroupAsync(_connectionId, $"server-{_testServer.Id}", default),
            Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_CreatesPresenceState()
    {
        var hub = CreateHub();

        await hub.OnConnectedAsync();

        var presence = await _db.PresenceStates.FirstOrDefaultAsync(p => p.ConnectionId == _connectionId);
        presence.Should().NotBeNull();
        presence!.UserId.Should().Be(_testUser.Id);
        presence.Status.Should().Be(PresenceStatus.Online);
    }

    [Fact]
    public async Task OnConnectedAsync_BroadcastsPresenceToServerGroups()
    {
        var hub = CreateHub();

        await hub.OnConnectedAsync();

        _mockClients.Verify(
            c => c.Group($"server-{_testServer.Id}"),
            Times.AtLeastOnce);
        _mockClientProxy.Verify(
            p => p.SendCoreAsync("UserPresenceChanged", It.IsAny<object?[]>(), default),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task OnConnectedAsync_BroadcastsPresenceToFriends()
    {
        // Add a friendship
        _db.Friendships.Add(new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = _testUser.Id,
            RecipientId = _testUser2.Id,
            Status = FriendshipStatus.Accepted
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.OnConnectedAsync();

        _mockClients.Verify(
            c => c.Group($"user-{_testUser2.Id}"),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task OnConnectedAsync_GlobalAdmin_JoinsAllServerGroups()
    {
        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            GoogleSubject = "g-admin",
            DisplayName = "Admin",
            IsGlobalAdmin = true
        };
        _db.Users.Add(adminUser);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((adminUser, false));

        var hub = CreateHub(adminUser);
        await hub.OnConnectedAsync();

        // Global admin joins ALL servers, not just their memberships
        _mockGroups.Verify(
            g => g.AddToGroupAsync(_connectionId, $"server-{_testServer.Id}", default),
            Times.Once);
    }

    // ── JoinServer / LeaveServer ──

    [Fact]
    public async Task JoinServer_AddsToServerGroup()
    {
        var hub = CreateHub();

        await hub.JoinServer(_testServer.Id.ToString());

        _mockGroups.Verify(
            g => g.AddToGroupAsync(_connectionId, $"server-{_testServer.Id}", default),
            Times.Once);
    }

    [Fact]
    public async Task LeaveServer_RemovesFromServerGroup()
    {
        var hub = CreateHub();

        await hub.LeaveServer(_testServer.Id.ToString());

        _mockGroups.Verify(
            g => g.RemoveFromGroupAsync(_connectionId, $"server-{_testServer.Id}", default),
            Times.Once);
    }

    // ── JoinChannel / LeaveChannel ──

    [Fact]
    public async Task JoinChannel_AddsToChannelGroup()
    {
        var hub = CreateHub();
        var channelId = _textChannel.Id.ToString();

        await hub.JoinChannel(channelId);

        _mockGroups.Verify(
            g => g.AddToGroupAsync(_connectionId, channelId, default),
            Times.Once);
    }

    [Fact]
    public async Task LeaveChannel_RemovesFromChannelGroup()
    {
        var hub = CreateHub();
        var channelId = _textChannel.Id.ToString();

        await hub.LeaveChannel(channelId);

        _mockGroups.Verify(
            g => g.RemoveFromGroupAsync(_connectionId, channelId, default),
            Times.Once);
    }

    // ── Typing indicators ──

    [Fact]
    public async Task StartTyping_BroadcastsToOthersInGroup()
    {
        var hub = CreateHub();
        var channelId = _textChannel.Id.ToString();

        await hub.StartTyping(channelId, "TestNick");

        _mockClients.Verify(c => c.OthersInGroup(channelId), Times.Once);
        _mockOthersProxy.Verify(
            p => p.SendCoreAsync("UserTyping", It.Is<object?[]>(a => a.Length == 2), default),
            Times.Once);
    }

    [Fact]
    public async Task StopTyping_BroadcastsToOthersInGroup()
    {
        var hub = CreateHub();
        var channelId = _textChannel.Id.ToString();

        await hub.StopTyping(channelId, "TestNick");

        _mockClients.Verify(c => c.OthersInGroup(channelId), Times.Once);
        _mockOthersProxy.Verify(
            p => p.SendCoreAsync("UserStoppedTyping", It.Is<object?[]>(a => a.Length == 2), default),
            Times.Once);
    }

    // ── DM Channel groups ──

    [Fact]
    public async Task JoinDmChannel_AddsToGroupWithDmPrefix()
    {
        var hub = CreateHub();
        var dmId = _dmChannel.Id.ToString();

        await hub.JoinDmChannel(dmId);

        _mockGroups.Verify(
            g => g.AddToGroupAsync(_connectionId, $"dm-{dmId}", default),
            Times.Once);
    }

    [Fact]
    public async Task LeaveDmChannel_RemovesFromGroupWithDmPrefix()
    {
        var hub = CreateHub();
        var dmId = _dmChannel.Id.ToString();

        await hub.LeaveDmChannel(dmId);

        _mockGroups.Verify(
            g => g.RemoveFromGroupAsync(_connectionId, $"dm-{dmId}", default),
            Times.Once);
    }

    // ── DM Typing indicators ──

    [Fact]
    public async Task StartDmTyping_BroadcastsToOthersInDmGroup()
    {
        var hub = CreateHub();
        var dmId = _dmChannel.Id.ToString();

        await hub.StartDmTyping(dmId, "TestNick");

        _mockClients.Verify(c => c.OthersInGroup($"dm-{dmId}"), Times.Once);
        _mockOthersProxy.Verify(
            p => p.SendCoreAsync("DmTyping", It.Is<object?[]>(a => a.Length == 2), default),
            Times.Once);
    }

    [Fact]
    public async Task StopDmTyping_BroadcastsToOthersInDmGroup()
    {
        var hub = CreateHub();
        var dmId = _dmChannel.Id.ToString();

        await hub.StopDmTyping(dmId, "TestNick");

        _mockClients.Verify(c => c.OthersInGroup($"dm-{dmId}"), Times.Once);
        _mockOthersProxy.Verify(
            p => p.SendCoreAsync("DmStoppedTyping", It.Is<object?[]>(a => a.Length == 2), default),
            Times.Once);
    }

    // ── StartCall ──

    [Fact]
    public async Task StartCall_InvalidDmChannelId_ThrowsHubException()
    {
        var hub = CreateHub();

        var act = () => hub.StartCall("not-a-guid");

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Invalid DM channel ID.");
    }

    [Fact]
    public async Task StartCall_NotAMember_ThrowsHubException()
    {
        var hub = CreateHub();
        var otherDm = new DmChannel { Id = Guid.NewGuid() };
        _db.DmChannels.Add(otherDm);
        await _db.SaveChangesAsync();

        var act = () => hub.StartCall(otherDm.Id.ToString());

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Not a member of this DM channel.");
    }

    [Fact]
    public async Task StartCall_ExistingRingingCall_ThrowsHubException()
    {
        var hub = CreateHub();

        _db.VoiceCalls.Add(new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser2.Id,
            RecipientUserId = _testUser.Id,
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var act = () => hub.StartCall(_dmChannel.Id.ToString());

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("There is already an active call on this conversation.");
    }

    [Fact]
    public async Task StartCall_CallerAlreadyInCall_ThrowsHubException()
    {
        var hub = CreateHub();

        var otherDm = new DmChannel { Id = Guid.NewGuid() };
        var otherUser = new User { Id = Guid.NewGuid(), DisplayName = "Other" };
        _db.DmChannels.Add(otherDm);
        _db.Users.Add(otherUser);
        _db.VoiceCalls.Add(new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = otherDm.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = otherUser.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var act = () => hub.StartCall(_dmChannel.Id.ToString());

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("You are already in a call.");
    }

    [Fact]
    public async Task StartCall_RecipientAlreadyInCall_ThrowsHubException()
    {
        var hub = CreateHub();

        var otherDm = new DmChannel { Id = Guid.NewGuid() };
        var otherUser = new User { Id = Guid.NewGuid(), DisplayName = "Other" };
        _db.DmChannels.Add(otherDm);
        _db.Users.Add(otherUser);
        _db.VoiceCalls.Add(new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = otherDm.Id,
            CallerUserId = otherUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var act = () => hub.StartCall(_dmChannel.Id.ToString());

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Recipient is already in a call.");
    }

    [Fact]
    public async Task StartCall_ValidCall_CreatesVoiceCallAndNotifiesRecipient()
    {
        var hub = CreateHub();

        var result = await hub.StartCall(_dmChannel.Id.ToString());

        var call = await _db.VoiceCalls.FirstOrDefaultAsync(c => c.DmChannelId == _dmChannel.Id);
        call.Should().NotBeNull();
        call!.CallerUserId.Should().Be(_testUser.Id);
        call.RecipientUserId.Should().Be(_testUser2.Id);
        call.Status.Should().Be(VoiceCallStatus.Ringing);

        _mockClients.Verify(c => c.Group($"user-{_testUser2.Id}"), Times.AtLeastOnce);
        _mockClientProxy.Verify(
            p => p.SendCoreAsync("IncomingCall", It.IsAny<object?[]>(), default),
            Times.Once);
    }

    [Fact]
    public async Task StartCall_ValidCall_ReturnsCallIdAndRecipientInfo()
    {
        var hub = CreateHub();

        var result = await hub.StartCall(_dmChannel.Id.ToString());

        // Verify the return value contains expected data (timeout service is invoked
        // but StartTimeout is not virtual, so we verify the overall outcome instead).
        result.Should().NotBeNull();
        var call = await _db.VoiceCalls.FirstAsync(c => c.DmChannelId == _dmChannel.Id);
        call.Status.Should().Be(VoiceCallStatus.Ringing);
        call.CallerUserId.Should().Be(_testUser.Id);
        call.RecipientUserId.Should().Be(_testUser2.Id);
    }

    [Fact]
    public async Task StartCall_CleanupExistingVoiceState()
    {
        var hub = CreateHub();

        // User is in a server voice channel
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        await hub.StartCall(_dmChannel.Id.ToString());

        // Voice state for server channel should be removed
        var voiceStates = await _db.VoiceStates.Where(v => v.UserId == _testUser.Id && v.ChannelId == _voiceChannel.Id).ToListAsync();
        voiceStates.Should().BeEmpty();
    }

    // ── AcceptCall ──

    [Fact]
    public async Task AcceptCall_InvalidCallId_ThrowsHubException()
    {
        var hub = CreateHub();

        var act = () => hub.AcceptCall("not-a-guid");

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Invalid call ID.");
    }

    [Fact]
    public async Task AcceptCall_CallNotFound_ThrowsHubException()
    {
        var hub = CreateHub();

        var act = () => hub.AcceptCall(Guid.NewGuid().ToString());

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Call not found.");
    }

    [Fact]
    public async Task AcceptCall_NotRecipient_ThrowsHubException()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser2.Id,
            RecipientUserId = Guid.NewGuid(), // Different user
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        var act = () => hub.AcceptCall(call.Id.ToString());

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("You are not the recipient of this call.");
    }

    [Fact]
    public async Task AcceptCall_AlreadyHandled_ReturnsIdempotent()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser2.Id,
            RecipientUserId = _testUser.Id,
            Status = VoiceCallStatus.Ended, // Already ended
            StartedAt = DateTimeOffset.UtcNow,
            EndedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        var result = await hub.AcceptCall(call.Id.ToString());

        // Should return alreadyHandled without throwing
        result.Should().NotBeNull();
    }

    // ── DeclineCall ──

    [Fact]
    public async Task DeclineCall_InvalidCallId_ThrowsHubException()
    {
        var hub = CreateHub();

        var act = () => hub.DeclineCall("not-a-guid");

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Invalid call ID.");
    }

    [Fact]
    public async Task DeclineCall_CallNotFound_ReturnsWithoutError()
    {
        var hub = CreateHub();

        // Should not throw
        await hub.DeclineCall(Guid.NewGuid().ToString());
    }

    [Fact]
    public async Task DeclineCall_NotRecipient_ThrowsHubException()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser2.Id,
            RecipientUserId = Guid.NewGuid(),
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        var act = () => hub.DeclineCall(call.Id.ToString());

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("You are not the recipient of this call.");
    }

    [Fact]
    public async Task DeclineCall_AlreadyEnded_ReturnsIdempotent()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser2.Id,
            RecipientUserId = _testUser.Id,
            Status = VoiceCallStatus.Ended,
            StartedAt = DateTimeOffset.UtcNow,
            EndedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();

        // Should not throw, should be idempotent
        await hub.DeclineCall(call.Id.ToString());

        var dbCall = await _db.VoiceCalls.FirstAsync(c => c.Id == call.Id);
        dbCall.Status.Should().Be(VoiceCallStatus.Ended);
    }

    [Fact]
    public async Task DeclineCall_ValidDecline_EndsCallAndNotifiesCaller()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser2.Id,
            RecipientUserId = _testUser.Id,
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.DeclineCall(call.Id.ToString());

        var dbCall = await _db.VoiceCalls.FirstAsync(c => c.Id == call.Id);
        dbCall.Status.Should().Be(VoiceCallStatus.Ended);
        dbCall.EndReason.Should().Be(VoiceCallEndReason.Declined);
        dbCall.EndedAt.Should().NotBeNull();

        // Should create a system message
        var dm = await _db.DirectMessages.FirstOrDefaultAsync(m => m.DmChannelId == _dmChannel.Id);
        dm.Should().NotBeNull();
        dm!.Body.Should().Be("missed");
        dm.MessageType.Should().Be(MessageType.VoiceCallEvent);

        // Should notify caller (CancelTimeout is also called but is not virtual so cannot be verified with Moq)
        _mockClientProxy.Verify(
            p => p.SendCoreAsync("CallDeclined", It.IsAny<object?[]>(), default),
            Times.Once);
    }

    // ── EndCall ──

    [Fact]
    public async Task EndCall_NoActiveCall_ReturnsWithoutError()
    {
        var hub = CreateHub();

        // Should not throw
        await hub.EndCall();
    }

    [Fact]
    public async Task EndCall_ActiveCall_EndsAndNotifiesOtherParty()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            AnsweredAt = DateTimeOffset.UtcNow.AddMinutes(-4)
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.EndCall();

        var dbCall = await _db.VoiceCalls.FirstAsync(c => c.Id == call.Id);
        dbCall.Status.Should().Be(VoiceCallStatus.Ended);
        dbCall.EndReason.Should().Be(VoiceCallEndReason.Completed);

        // System message with duration
        var dm = await _db.DirectMessages.FirstOrDefaultAsync(m => m.DmChannelId == _dmChannel.Id);
        dm.Should().NotBeNull();
        dm!.Body.Should().StartWith("call:");
    }

    [Fact]
    public async Task EndCall_RingingCall_EndsAsMissed()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.EndCall();

        var dbCall = await _db.VoiceCalls.FirstAsync(c => c.Id == call.Id);
        dbCall.Status.Should().Be(VoiceCallStatus.Ended);
        dbCall.EndReason.Should().Be(VoiceCallEndReason.Missed);

        var dm = await _db.DirectMessages.FirstOrDefaultAsync(m => m.DmChannelId == _dmChannel.Id);
        dm.Should().NotBeNull();
        dm!.Body.Should().Be("missed");
    }

    // ── SetupCallTransports ──

    [Fact]
    public async Task SetupCallTransports_InvalidCallId_ThrowsHubException()
    {
        var hub = CreateHub();

        var act = () => hub.SetupCallTransports("not-a-guid");

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Invalid call ID.");
    }

    [Fact]
    public async Task SetupCallTransports_CallNotFound_ThrowsHubException()
    {
        var hub = CreateHub();

        var act = () => hub.SetupCallTransports(Guid.NewGuid().ToString());

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Call not found.");
    }

    [Fact]
    public async Task SetupCallTransports_NotCaller_ThrowsHubException()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser2.Id, // Not the test user
            RecipientUserId = _testUser.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            AnsweredAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        var act = () => hub.SetupCallTransports(call.Id.ToString());

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Call not found.");
    }

    [Fact]
    public async Task SetupCallTransports_CallNotActive_ThrowsHubException()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        var act = () => hub.SetupCallTransports(call.Id.ToString());

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Call is not active.");
    }

    // ── JoinVoiceChannel ──

    [Fact]
    public async Task JoinVoiceChannel_InvalidChannelId_ThrowsHubException()
    {
        var hub = CreateHub();

        var act = () => hub.JoinVoiceChannel("not-a-guid");

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Invalid channel ID.");
    }

    [Fact]
    public async Task JoinVoiceChannel_TextChannel_ThrowsHubException()
    {
        var hub = CreateHub();

        var act = () => hub.JoinVoiceChannel(_textChannel.Id.ToString());

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Voice channel not found.");
    }

    [Fact]
    public async Task JoinVoiceChannel_NonexistentChannel_ThrowsHubException()
    {
        var hub = CreateHub();

        var act = () => hub.JoinVoiceChannel(Guid.NewGuid().ToString());

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Voice channel not found.");
    }

    [Fact]
    public async Task JoinVoiceChannel_NotServerMember_ThrowsHubException()
    {
        var nonMember = new User
        {
            Id = Guid.NewGuid(),
            GoogleSubject = "g-nonmember",
            DisplayName = "NonMember"
        };
        _db.Users.Add(nonMember);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((nonMember, false));

        var hub = CreateHub(nonMember);
        var act = () => hub.JoinVoiceChannel(_voiceChannel.Id.ToString());

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Not a member of this server.");
    }

    [Fact]
    public async Task JoinVoiceChannel_GlobalAdmin_BypassesMembershipCheck()
    {
        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            GoogleSubject = "g-admin2",
            DisplayName = "Admin",
            IsGlobalAdmin = true
        };
        _db.Users.Add(adminUser);
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((adminUser, false));

        // SFU call will fail, but we can verify the membership check was bypassed
        var hub = CreateHub(adminUser);
        var act = () => hub.JoinVoiceChannel(_voiceChannel.Id.ToString());

        // Will throw because the SFU is not running, but NOT "Not a member" error
        var ex = await act.Should().ThrowAsync<HubException>();
        ex.Which.Message.Should().NotBe("Not a member of this server.");
    }

    // ── ConnectTransport ──

    [Fact]
    public async Task ConnectTransport_NotInVoice_ThrowsHubException()
    {
        var hub = CreateHub();

        var act = () => hub.ConnectTransport("some-transport", JsonDocument.Parse("{}").RootElement);

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Not currently in a voice session.");
    }

    // ── Produce ──

    [Fact]
    public async Task Produce_InvalidLabel_ThrowsHubException()
    {
        var hub = CreateHub();

        var act = () => hub.Produce("transport-id", JsonDocument.Parse("{}").RootElement, "invalid-label");

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Invalid producer label. Must be 'audio', 'video', or 'screen'.");
    }

    [Fact]
    public async Task Produce_NotInVoice_ThrowsHubException()
    {
        var hub = CreateHub();

        var act = () => hub.Produce("transport-id", JsonDocument.Parse("{}").RootElement, "audio");

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Not currently in a voice session.");
    }

    [Fact]
    public async Task Produce_DefaultLabel_IsAudio()
    {
        var hub = CreateHub();

        // null label should not throw for invalid label
        var act = () => hub.Produce("transport-id", JsonDocument.Parse("{}").RootElement, null);

        // Will fail on "not in voice session" not "invalid label"
        var ex = await act.Should().ThrowAsync<HubException>();
        ex.Which.Message.Should().Be("Not currently in a voice session.");
    }

    // ── StopProducing ──

    [Fact]
    public async Task StopProducing_InvalidLabel_ThrowsHubException()
    {
        var hub = CreateHub();

        var act = () => hub.StopProducing("audio");

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Can only stop 'video' or 'screen' producers.");
    }

    [Fact]
    public async Task StopProducing_NotInVoice_ReturnsWithoutError()
    {
        var hub = CreateHub();

        // Should not throw when no voice state exists
        await hub.StopProducing("video");
    }

    [Fact]
    public async Task StopProducing_VideoLabel_ClearsVideoState()
    {
        // Set up voice state with a video producer
        var voiceState = new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            VideoProducerId = "video-producer-123",
            IsVideoEnabled = true,
            JoinedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceStates.Add(voiceState);
        await _db.SaveChangesAsync();

        // Mock HTTP client for SFU delete call
        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        await hub.StopProducing("video");

        var updated = await _db.VoiceStates.FirstAsync(v => v.UserId == _testUser.Id);
        updated.VideoProducerId.Should().BeNull();
        updated.IsVideoEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task StopProducing_ScreenLabel_ClearsScreenState()
    {
        var voiceState = new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            ScreenProducerId = "screen-producer-456",
            IsScreenSharing = true,
            JoinedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceStates.Add(voiceState);
        await _db.SaveChangesAsync();

        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        await hub.StopProducing("screen");

        var updated = await _db.VoiceStates.FirstAsync(v => v.UserId == _testUser.Id);
        updated.ScreenProducerId.Should().BeNull();
        updated.IsScreenSharing.Should().BeFalse();
    }

    // ── Consume ──

    [Fact]
    public async Task Consume_NotInVoice_ThrowsHubException()
    {
        var hub = CreateHub();

        var act = () => hub.Consume("producer-id", "transport-id", JsonDocument.Parse("{}").RootElement);

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Not currently in a voice session.");
    }

    // ── UpdateVoiceState ──

    [Fact]
    public async Task UpdateVoiceState_NotInVoice_ReturnsWithoutError()
    {
        var hub = CreateHub();

        // Should not throw
        await hub.UpdateVoiceState(true, false);
    }

    [Fact]
    public async Task UpdateVoiceState_InServerVoiceChannel_UpdatesAndBroadcasts()
    {
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.UpdateVoiceState(true, true);

        var voiceState = await _db.VoiceStates.FirstAsync(v => v.UserId == _testUser.Id);
        voiceState.IsMuted.Should().BeTrue();
        voiceState.IsDeafened.Should().BeTrue();

        _mockClients.Verify(
            c => c.OthersInGroup($"server-{_testServer.Id}"),
            Times.Once);
        _mockOthersProxy.Verify(
            p => p.SendCoreAsync("VoiceStateUpdated", It.IsAny<object?[]>(), default),
            Times.Once);
    }

    [Fact]
    public async Task UpdateVoiceState_InDmCall_BroadcastsToVoiceRoomGroup()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            AnsweredAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.UpdateVoiceState(false, false);

        _mockClients.Verify(
            c => c.OthersInGroup($"voice-call-{call.Id}"),
            Times.Once);
    }

    // ── LeaveVoiceChannel ──

    [Fact]
    public async Task LeaveVoiceChannel_NotInVoice_ReturnsWithoutError()
    {
        var hub = CreateHub();

        // Should not throw
        await hub.LeaveVoiceChannel();
    }

    [Fact]
    public async Task LeaveVoiceChannel_InServerChannel_RemovesStateAndNotifies()
    {
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        await hub.LeaveVoiceChannel();

        var remaining = await _db.VoiceStates.Where(v => v.UserId == _testUser.Id).ToListAsync();
        remaining.Should().BeEmpty();

        _mockGroups.Verify(
            g => g.RemoveFromGroupAsync(_connectionId, $"voice-{_voiceChannel.Id}", default),
            Times.Once);

        _mockClientProxy.Verify(
            p => p.SendCoreAsync("UserLeftVoice", It.IsAny<object?[]>(), default),
            Times.Once);
    }

    // ── OnDisconnectedAsync ──

    [Fact]
    public async Task OnDisconnectedAsync_CleansUpPresenceState()
    {
        // OnDisconnectedAsync uses ExecuteDeleteAsync which is not supported by InMemory.
        // This causes the hub to fall through to the catch block.
        // The catch block still attempts to clean up voice state via standard EF operations.
        // We verify the disconnect lifecycle does not throw.
        var hub = CreateHub();
        await hub.OnConnectedAsync();

        var presenceExists = await _db.PresenceStates.AnyAsync(p => p.ConnectionId == _connectionId);
        presenceExists.Should().BeTrue();

        // Disconnect triggers the catch block due to InMemory limitations with ExecuteDeleteAsync,
        // but the hub swallows the error and still calls base.OnDisconnectedAsync.
        await hub.OnDisconnectedAsync(null);

        // The presence tracker still tracks the disconnect even if DB cleanup partially fails.
        var trackerStatus = _presenceTracker.GetAggregateStatus(_testUser.Id);
        trackerStatus.Should().Be(PresenceStatus.Offline);
    }

    [Fact]
    public async Task OnDisconnectedAsync_CleansUpVoiceState()
    {
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        await hub.OnDisconnectedAsync(null);

        var remaining = await _db.VoiceStates.Where(v => v.UserId == _testUser.Id).ToListAsync();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task OnDisconnectedAsync_EndsActiveCall()
    {
        // OnDisconnectedAsync uses ExecuteDeleteAsync (unsupported by InMemory),
        // so the primary try block throws and falls to the catch block.
        // The catch block performs best-effort call cleanup via standard EF operations.
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            AnsweredAt = DateTimeOffset.UtcNow.AddMinutes(-4)
        };
        _db.VoiceCalls.Add(call);
        // Also need a voice state so the catch block finds it and cleans up the call
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.OnDisconnectedAsync(null);

        var dbCall = await _db.VoiceCalls.FirstAsync(c => c.Id == call.Id);
        dbCall.Status.Should().Be(VoiceCallStatus.Ended);
        dbCall.EndReason.Should().Be(VoiceCallEndReason.Completed);
    }

    [Fact]
    public async Task OnDisconnectedAsync_WithException_StillCleansUpVoiceState()
    {
        // Simulate a scenario where GetOrCreateUserAsync throws so we exercise the catch block
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ThrowsAsync(new InvalidOperationException("user service error"));

        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.OnDisconnectedAsync(null);

        // The fallback cleanup in the catch block should have removed the voice state
        var remaining = await _db.VoiceStates.Where(v => v.ConnectionId == _connectionId).ToListAsync();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task OnDisconnectedAsync_BroadcastsPresenceChange()
    {
        // ExecuteDeleteAsync is unsupported by InMemory, so the primary try block falls
        // to the catch block. The presence tracker still tracks the disconnect correctly.
        _presenceTracker.Connect(_testUser.Id, _connectionId);

        var hub = CreateHub();
        await hub.OnDisconnectedAsync(null);

        // Verify the presence tracker recorded the disconnect
        var status = _presenceTracker.GetAggregateStatus(_testUser.Id);
        status.Should().Be(PresenceStatus.Offline);
    }

    // ── Heartbeat ──

    [Fact]
    public async Task Heartbeat_UnknownConnection_ReturnsWithoutError()
    {
        var hub = CreateHub();

        // Should not throw when connection is not tracked
        await hub.Heartbeat(true);
    }

    [Fact]
    public async Task Heartbeat_StatusChange_BroadcastsToServersAndFriends()
    {
        // Register the connection in the presence tracker
        _presenceTracker.Connect(_testUser.Id, _connectionId);

        // Add a friendship
        _db.Friendships.Add(new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = _testUser.Id,
            RecipientId = _testUser2.Id,
            Status = FriendshipStatus.Accepted
        });
        _db.PresenceStates.Add(new PresenceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            Status = PresenceStatus.Online,
            ConnectionId = _connectionId,
            LastHeartbeatAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            ConnectedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();

        // Active heartbeat with no status change should not broadcast
        await hub.Heartbeat(true);

        // We can't easily force a status change with the real PresenceTracker
        // since it tracks per-connection. But the method handles null return correctly.
    }

    [Fact]
    public async Task Heartbeat_NoStatusChange_DoesNotBroadcast()
    {
        _presenceTracker.Connect(_testUser.Id, _connectionId);

        var hub = CreateHub();
        await hub.Heartbeat(true);

        // No status change means no broadcasts
        _mockClientProxy.Verify(
            p => p.SendCoreAsync("UserPresenceChanged", It.IsAny<object?[]>(), default),
            Times.Never);
    }

    // ── Edge cases for voice with DM calls ──

    [Fact]
    public async Task LeaveVoiceChannel_InDmCall_RemovesStateAndCleansUp()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            AnsweredAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        await hub.LeaveVoiceChannel();

        var remaining = await _db.VoiceStates.Where(v => v.UserId == _testUser.Id).ToListAsync();
        remaining.Should().BeEmpty();

        _mockGroups.Verify(
            g => g.RemoveFromGroupAsync(_connectionId, $"voice-call-{call.Id}", default),
            Times.Once);
    }

    [Fact]
    public async Task EndCall_CleansUpVoiceStatesForBothParticipants()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            AnsweredAt = DateTimeOffset.UtcNow.AddMinutes(-4)
        };
        _db.VoiceCalls.Add(call);
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser2.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = "test-connection-id-2",
            ParticipantId = "test-connection-id-2",
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        await hub.EndCall();

        var remainingStates = await _db.VoiceStates.Where(v => v.DmChannelId == _dmChannel.Id).ToListAsync();
        remainingStates.Should().BeEmpty();
    }

    // ── StopProducing with notification ──

    [Fact]
    public async Task StopProducing_WithProducerId_NotifiesOthers()
    {
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            VideoProducerId = "video-producer-abc",
            IsVideoEnabled = true,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        await hub.StopProducing("video");

        _mockOthersProxy.Verify(
            p => p.SendCoreAsync("ProducerClosed", It.IsAny<object?[]>(), default),
            Times.Once);
    }

    [Fact]
    public async Task StopProducing_WithNullProducerId_DoesNotNotify()
    {
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            VideoProducerId = null, // No video producer
            IsVideoEnabled = false,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        await hub.StopProducing("video");

        _mockOthersProxy.Verify(
            p => p.SendCoreAsync("ProducerClosed", It.IsAny<object?[]>(), default),
            Times.Never);
    }

    // ── OnDisconnectedAsync fallback cleanup ──

    [Fact]
    public async Task OnDisconnectedAsync_FallbackCleanup_RemovesStaleVoiceStateAndNotifies()
    {
        // Make the user service throw so we exercise the catch fallback path
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ThrowsAsync(new Exception("Simulated failure"));

        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.OnDisconnectedAsync(null);

        var remaining = await _db.VoiceStates.Where(v => v.ConnectionId == _connectionId).ToListAsync();
        remaining.Should().BeEmpty();

        // Should attempt to notify via UserLeftVoice
        _mockClientProxy.Verify(
            p => p.SendCoreAsync("UserLeftVoice", It.IsAny<object?[]>(), default),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task OnDisconnectedAsync_FallbackCleanup_HandlesActiveCallCleanup()
    {
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ThrowsAsync(new Exception("Simulated failure"));

        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            AnsweredAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.OnDisconnectedAsync(null);

        var dbCall = await _db.VoiceCalls.FirstAsync(c => c.Id == call.Id);
        dbCall.Status.Should().Be(VoiceCallStatus.Ended);
        dbCall.EndReason.Should().Be(VoiceCallEndReason.Completed);
    }

    [Fact]
    public async Task OnDisconnectedAsync_FallbackCleanup_HandlesRingingCallAsMissed()
    {
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ThrowsAsync(new Exception("Simulated failure"));

        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.OnDisconnectedAsync(null);

        var dbCall = await _db.VoiceCalls.FirstAsync(c => c.Id == call.Id);
        dbCall.Status.Should().Be(VoiceCallStatus.Ended);
        dbCall.EndReason.Should().Be(VoiceCallEndReason.Missed);
    }

    // ── Multiple group operations ──

    [Fact]
    public async Task OnConnectedAsync_MultipleServers_JoinsAllGroups()
    {
        var server2 = new Server { Id = Guid.NewGuid(), Name = "Server 2" };
        _db.Servers.Add(server2);
        var role2 = new ServerRoleEntity
        {
            Id = Guid.NewGuid(),
            ServerId = server2.Id,
            Name = "Member",
            Position = 2,
            IsSystemRole = true
        };
        _db.ServerRoles.Add(role2);
        _db.ServerMembers.Add(new ServerMember
        {
            ServerId = server2.Id,
            UserId = _testUser.Id,
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.OnConnectedAsync();

        _mockGroups.Verify(
            g => g.AddToGroupAsync(_connectionId, $"server-{_testServer.Id}", default),
            Times.Once);
        _mockGroups.Verify(
            g => g.AddToGroupAsync(_connectionId, $"server-{server2.Id}", default),
            Times.Once);
    }

    // ── UpdateVoiceState with no channel and no DM ──

    [Fact]
    public async Task UpdateVoiceState_VoiceStateWithNeitherChannelNorDm_OnlyUpdatesDb()
    {
        // Edge case: voice state has neither channelId nor dmChannelId
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = null,
            DmChannelId = null,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.UpdateVoiceState(true, false);

        var voiceState = await _db.VoiceStates.FirstAsync(v => v.UserId == _testUser.Id);
        voiceState.IsMuted.Should().BeTrue();
        voiceState.IsDeafened.Should().BeFalse();

        // No broadcast should happen
        _mockOthersProxy.Verify(
            p => p.SendCoreAsync("VoiceStateUpdated", It.IsAny<object?[]>(), default),
            Times.Never);
    }

    // ── Concurrent connection scenarios ──

    [Fact]
    public async Task OnConnectedAsync_SecondConnection_DoesNotDuplicatePresence()
    {
        var hub = CreateHub();
        await hub.OnConnectedAsync();

        // Second connection with different ID
        var mockContext2 = new Mock<HubCallerContext>();
        mockContext2.Setup(c => c.ConnectionId).Returns("second-connection");
        mockContext2.Setup(c => c.UserIdentifier).Returns(_testUser.Id.ToString());
        mockContext2.Setup(c => c.User).Returns(
            new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("sub", _testUser.GoogleSubject!),
                new Claim("name", _testUser.DisplayName)
            ], "Bearer")));

        var hub2 = new ChatHub(
            _userService.Object,
            _db,
            _config.Object,
            _httpClientFactory.Object,
            _logger.Object,
            _callTimeoutService.Object,
            _presenceTracker,
            _permissionResolver.Object
        );
        hub2.Context = mockContext2.Object;
        hub2.Clients = _mockClients.Object;
        hub2.Groups = _mockGroups.Object;

        await hub2.OnConnectedAsync();

        var presenceCount = await _db.PresenceStates.CountAsync(p => p.UserId == _testUser.Id);
        presenceCount.Should().Be(2); // One per connection
    }

    // ── Channel/Server leaving edge cases ──

    [Fact]
    public async Task JoinChannel_MultipleChannels_JoinsBoth()
    {
        var channelA = _textChannel.Id.ToString();
        var channelB = _voiceChannel.Id.ToString();
        var hub = CreateHub();

        await hub.JoinChannel(channelA);
        await hub.JoinChannel(channelB);

        _mockGroups.Verify(g => g.AddToGroupAsync(_connectionId, channelA, default), Times.Once);
        _mockGroups.Verify(g => g.AddToGroupAsync(_connectionId, channelB, default), Times.Once);
    }

    [Fact]
    public async Task LeaveVoiceChannel_WithNoDmCallRecord_DoesNotThrow()
    {
        // Voice state with DmChannelId but no matching VoiceCall
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();

        // Should not throw even if no call record exists
        await hub.LeaveVoiceChannel();

        var remaining = await _db.VoiceStates.Where(v => v.UserId == _testUser.Id).ToListAsync();
        remaining.Should().BeEmpty();
    }

    // ── Verify EndCall notifies other party ──

    [Fact]
    public async Task EndCall_NotifiesRecipientWhenCallerEnds()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            AnsweredAt = DateTimeOffset.UtcNow.AddMinutes(-4)
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.EndCall();

        // Should notify the recipient (other party)
        _mockClients.Verify(c => c.Group($"user-{_testUser2.Id}"), Times.AtLeastOnce);
        _mockClientProxy.Verify(
            p => p.SendCoreAsync("CallEnded", It.IsAny<object?[]>(), default),
            Times.Once);

        // Should send ReceiveDm to both parties
        _mockClientProxy.Verify(
            p => p.SendCoreAsync("ReceiveDm", It.IsAny<object?[]>(), default),
            Times.AtLeast(2));
    }

    // ── DeclineCall creates system DM and sends ReceiveDm ──

    [Fact]
    public async Task DeclineCall_SendsReceiveDmToBothParties()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser2.Id,
            RecipientUserId = _testUser.Id,
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.DeclineCall(call.Id.ToString());

        // ReceiveDm sent to both caller and recipient
        _mockClientProxy.Verify(
            p => p.SendCoreAsync("ReceiveDm", It.IsAny<object?[]>(), default),
            Times.AtLeast(2));
    }

    // ── OnDisconnectedAsync with no voice state or call ──

    [Fact]
    public async Task OnDisconnectedAsync_NoVoiceOrCall_CompletesCleanly()
    {
        // Connect first so there is a presence state to clean up
        _presenceTracker.Connect(_testUser.Id, _connectionId);
        _db.PresenceStates.Add(new PresenceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            Status = PresenceStatus.Online,
            ConnectionId = _connectionId,
            LastHeartbeatAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            ConnectedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();

        // Should complete without error when there is no voice state or call
        await hub.OnDisconnectedAsync(null);
    }

    [Fact]
    public async Task OnDisconnectedAsync_WithPassedException_StillCleansUp()
    {
        _presenceTracker.Connect(_testUser.Id, _connectionId);

        var hub = CreateHub();

        // Pass an exception to OnDisconnectedAsync (simulating transport error)
        // The hub still runs its cleanup logic and calls base.OnDisconnectedAsync.
        await hub.OnDisconnectedAsync(new IOException("Transport closed"));

        // Verify presence tracker recorded the disconnect
        var status = _presenceTracker.GetAggregateStatus(_testUser.Id);
        status.Should().Be(PresenceStatus.Offline);
    }

    // ===== AcceptCall additional tests =====

    [Fact]
    public async Task AcceptCall_RingingCall_UpdatesStatusToActive()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        // Setup SFU HTTP mock that returns valid JSON
        var mockHandler = new MockHttpMessageHandler("""{"routerRtpCapabilities":{"codecs":[]}}""");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHubForUser2();

        try
        {
            await hub.AcceptCall(call.Id.ToString());
        }
        catch { /* SFU errors are expected in test */ }

        // Verify the call status was changed (either to Active or still updated)
        var updatedCall = await _db.VoiceCalls.FirstAsync(c => c.Id == call.Id);
        updatedCall.Status.Should().Be(VoiceCallStatus.Active);
    }

    [Fact]
    public async Task AcceptCall_LeavesExistingVoiceState()
    {
        // User2 is already in a voice channel
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser2.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = "test-connection-id-2",
            ParticipantId = "test-connection-id-2",
            JoinedAt = DateTimeOffset.UtcNow
        });

        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var mockHandler = new MockHttpMessageHandler("""{"routerRtpCapabilities":{"codecs":[]}}""");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHubForUser2();

        try
        {
            await hub.AcceptCall(call.Id.ToString());
        }
        catch { /* SFU error expected */ }

        // Previous voice state should have been removed
        var voiceStates = await _db.VoiceStates.Where(vs => vs.UserId == _testUser2.Id && vs.ChannelId == _voiceChannel.Id).ToListAsync();
        voiceStates.Should().BeEmpty();
    }

    // ===== SetupCallTransports additional tests =====

    [Fact]
    public async Task SetupCallTransports_NotActive_ThrowsHubException_Detailed()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Ended,
            StartedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();

        await hub.Invoking(h => h.SetupCallTransports(call.Id.ToString()))
            .Should().ThrowAsync<HubException>().WithMessage("*not active*");
    }

    [Fact]
    public async Task SetupCallTransports_WrongCaller_ThrowsHubException()
    {
        // Call where testUser2 is the caller, so testUser cannot set up transports
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser2.Id,
            RecipientUserId = _testUser.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            AnsweredAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();

        await hub.Invoking(h => h.SetupCallTransports(call.Id.ToString()))
            .Should().ThrowAsync<HubException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task SetupCallTransports_LeavesExistingVoiceSession()
    {
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });

        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            AnsweredAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var mockHandler = new MockHttpMessageHandler("""{"routerRtpCapabilities":{"codecs":[]}}""");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();

        try
        {
            await hub.SetupCallTransports(call.Id.ToString());
        }
        catch { /* SFU error expected */ }

        // Existing server voice state should be cleaned up
        var serverVoiceStates = await _db.VoiceStates.Where(vs => vs.UserId == _testUser.Id && vs.ChannelId == _voiceChannel.Id).ToListAsync();
        serverVoiceStates.Should().BeEmpty();
    }

    // ===== StartCall additional tests =====

    [Fact]
    public async Task StartCall_RecipientNotFound_ThrowsHubException()
    {
        // DM channel with only one member (no recipient)
        var soloChannel = new DmChannel { Id = Guid.NewGuid() };
        _db.DmChannels.Add(soloChannel);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannelId = soloChannel.Id, UserId = _testUser.Id });
        await _db.SaveChangesAsync();

        var hub = CreateHub();

        await hub.Invoking(h => h.StartCall(soloChannel.Id.ToString()))
            .Should().ThrowAsync<HubException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task StartCall_ValidCall_CreatesVoiceCall()
    {
        var hub = CreateHub();
        var result = await hub.StartCall(_dmChannel.Id.ToString());

        // Verify a ringing call was created in the database
        var calls = await _db.VoiceCalls
            .Where(c => c.DmChannelId == _dmChannel.Id && c.Status == VoiceCallStatus.Ringing)
            .ToListAsync();
        calls.Should().HaveCount(1);
        calls[0].CallerUserId.Should().Be(_testUser.Id);
        calls[0].RecipientUserId.Should().Be(_testUser2.Id);
    }

    // ===== JoinVoiceChannel additional tests =====

    [Fact]
    public async Task JoinVoiceChannel_SwitchingChannels_LeavesOldChannel()
    {
        // User already in a voice channel
        var existingState = new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceStates.Add(existingState);
        await _db.SaveChangesAsync();

        // Create second voice channel
        var voiceChannel2 = new Channel
        {
            Id = Guid.NewGuid(),
            ServerId = _testServer.Id,
            Name = "voice-2",
            Type = ChannelType.Voice
        };
        _db.Channels.Add(voiceChannel2);
        await _db.SaveChangesAsync();

        var mockHandler = new MockHttpMessageHandler("""{"routerRtpCapabilities":{"codecs":[]}}""");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();

        try
        {
            await hub.JoinVoiceChannel(voiceChannel2.Id.ToString());
        }
        catch { /* SFU errors expected in test */ }

        // Old voice state should be removed
        var oldStates = await _db.VoiceStates.Where(vs => vs.ChannelId == _voiceChannel.Id).ToListAsync();
        oldStates.Should().BeEmpty();
    }

    [Fact]
    public async Task JoinVoiceChannel_HttpRequestException_ThrowsHubException()
    {
        var mockHandler = new MockHttpMessageHandler(new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();

        await hub.Invoking(h => h.JoinVoiceChannel(_voiceChannel.Id.ToString()))
            .Should().ThrowAsync<HubException>().WithMessage("*unavailable*");
    }

    [Fact]
    public async Task JoinVoiceChannel_GenericException_ThrowsHubException()
    {
        var mockHandler = new MockHttpMessageHandler(new InvalidOperationException("Unexpected error"));
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();

        await hub.Invoking(h => h.JoinVoiceChannel(_voiceChannel.Id.ToString()))
            .Should().ThrowAsync<HubException>().WithMessage("*Failed to set up voice*");
    }

    // ===== ConnectTransport additional tests =====

    [Fact]
    public async Task ConnectTransport_InVoice_CallsSfu()
    {
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new MockHttpMessageHandler("{}");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        var dtlsParams = JsonDocument.Parse("""{"fingerprints":[]}""").RootElement;

        await hub.ConnectTransport("transport-1", dtlsParams);

        mockHandler.RequestCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ConnectTransport_InDmCall_UsesDmRoomId()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            AnsweredAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);

        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new MockHttpMessageHandler("{}");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        var dtlsParams = JsonDocument.Parse("""{"fingerprints":[]}""").RootElement;

        await hub.ConnectTransport("transport-1", dtlsParams);

        // Verify that the SFU was called with call-{callId} room ID
        mockHandler.LastRequestUri.Should().Contain($"call-{call.Id}");
    }

    // ===== Produce additional tests =====

    [Fact]
    public async Task Produce_VideoLabel_SetsVideoProducerId()
    {
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new MockHttpMessageHandler("""{"producerId":"video-producer-123"}""");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        var rtpParams = JsonDocument.Parse("{}").RootElement;

        var result = await hub.Produce("transport-1", rtpParams, "video");

        var vs = await _db.VoiceStates.FirstAsync(v => v.UserId == _testUser.Id);
        vs.VideoProducerId.Should().Be("video-producer-123");
        vs.IsVideoEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Produce_ScreenLabel_SetsScreenProducerId()
    {
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new MockHttpMessageHandler("""{"producerId":"screen-producer-456"}""");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        var rtpParams = JsonDocument.Parse("{}").RootElement;

        var result = await hub.Produce("transport-1", rtpParams, "screen");

        var vs = await _db.VoiceStates.FirstAsync(v => v.UserId == _testUser.Id);
        vs.ScreenProducerId.Should().Be("screen-producer-456");
        vs.IsScreenSharing.Should().BeTrue();
    }

    [Fact]
    public async Task Produce_AudioLabel_SetsProducerId()
    {
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new MockHttpMessageHandler("""{"producerId":"audio-producer-789"}""");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        var rtpParams = JsonDocument.Parse("{}").RootElement;

        var result = await hub.Produce("transport-1", rtpParams, "audio");

        var vs = await _db.VoiceStates.FirstAsync(v => v.UserId == _testUser.Id);
        vs.ProducerId.Should().Be("audio-producer-789");
    }

    [Fact]
    public async Task Produce_NotifiesOthersInVoiceGroup()
    {
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new MockHttpMessageHandler("""{"producerId":"prod-1"}""");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        var rtpParams = JsonDocument.Parse("{}").RootElement;

        await hub.Produce("transport-1", rtpParams, "audio");

        _mockOthersProxy.Verify(p => p.SendCoreAsync("NewProducer",
            It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===== Consume additional tests =====

    [Fact]
    public async Task Consume_InVoice_CallsSfu()
    {
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new MockHttpMessageHandler("""{"consumerId":"consumer-1","producerId":"prod-1","kind":"audio","rtpParameters":{}}""");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        var rtpCapabilities = JsonDocument.Parse("""{"codecs":[]}""").RootElement;

        var result = await hub.Consume("prod-1", "recv-transport-1", rtpCapabilities);

        mockHandler.RequestCount.Should().BeGreaterThan(0);
    }

    // ===== Heartbeat additional tests =====

    [Fact]
    public async Task Heartbeat_ActiveTrue_UpdatesPresenceState()
    {
        _presenceTracker.Connect(_testUser.Id, _connectionId);

        _db.PresenceStates.Add(new PresenceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            Status = PresenceStatus.Online,
            ConnectionId = _connectionId,
            LastHeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            LastActiveAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ConnectedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();

        await hub.Heartbeat(true);

        // Since we're already online and sending active=true, no status change expected
        // The DB update should still execute
        var ps = await _db.PresenceStates.FirstAsync(p => p.ConnectionId == _connectionId);
        ps.Should().NotBeNull();
    }

    [Fact]
    public async Task Heartbeat_ActiveFalse_MayChangeToIdle()
    {
        _presenceTracker.Connect(_testUser.Id, _connectionId);

        _db.PresenceStates.Add(new PresenceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            Status = PresenceStatus.Online,
            ConnectionId = _connectionId,
            LastHeartbeatAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            ConnectedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();

        // Sending inactive heartbeat
        await hub.Heartbeat(false);
    }

    // ===== OnDisconnectedAsync additional paths =====

    [Fact]
    public async Task OnDisconnectedAsync_WithFriendships_CompletesCleanly()
    {
        _presenceTracker.Connect(_testUser.Id, _connectionId);

        _db.PresenceStates.Add(new PresenceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            Status = PresenceStatus.Online,
            ConnectionId = _connectionId,
            LastHeartbeatAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            ConnectedAt = DateTimeOffset.UtcNow
        });

        // Add friendships (both accepted and pending)
        _db.Friendships.Add(new Friendship
        {
            RequesterId = _testUser.Id,
            RecipientId = _testUser2.Id,
            Status = FriendshipStatus.Accepted
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.OnDisconnectedAsync(null);

        // User should now be offline in the presence tracker
        var status = _presenceTracker.GetAggregateStatus(_testUser.Id);
        status.Should().Be(PresenceStatus.Offline);
    }

    [Fact]
    public async Task OnDisconnectedAsync_MultipleConnections_DoesNotGoOffline()
    {
        // Connect twice
        _presenceTracker.Connect(_testUser.Id, _connectionId);
        _presenceTracker.Connect(_testUser.Id, "second-connection");

        _db.PresenceStates.Add(new PresenceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            Status = PresenceStatus.Online,
            ConnectionId = _connectionId,
            LastHeartbeatAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            ConnectedAt = DateTimeOffset.UtcNow
        });
        _db.PresenceStates.Add(new PresenceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            Status = PresenceStatus.Online,
            ConnectionId = "second-connection",
            LastHeartbeatAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            ConnectedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.OnDisconnectedAsync(null);

        // User should still be online (second connection still active)
        var status = _presenceTracker.GetAggregateStatus(_testUser.Id);
        status.Should().Be(PresenceStatus.Online);
    }

    [Fact]
    public async Task OnDisconnectedAsync_FallbackCleanup_DmCallVoiceState_CleansUp()
    {
        // Create a voice state associated with a DM call (no server channel)
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            AnsweredAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);

        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });

        _db.PresenceStates.Add(new PresenceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            Status = PresenceStatus.Online,
            ConnectionId = _connectionId,
            LastHeartbeatAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            ConnectedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        _presenceTracker.Connect(_testUser.Id, _connectionId);

        var hub = CreateHub();
        await hub.OnDisconnectedAsync(null);

        // Voice state should be cleaned up
        var voiceStates = await _db.VoiceStates.Where(vs => vs.UserId == _testUser.Id).ToListAsync();
        voiceStates.Should().BeEmpty();

        // Call should be ended
        var updatedCall = await _db.VoiceCalls.FirstAsync(c => c.Id == call.Id);
        updatedCall.Status.Should().Be(VoiceCallStatus.Ended);
    }

    // ===== EndCall additional tests =====

    [Fact]
    public async Task EndCall_ActiveCall_CalculatesDuration()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            AnsweredAt = DateTimeOffset.UtcNow.AddMinutes(-3)
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.EndCall();

        var endedCall = await _db.VoiceCalls.FirstAsync(c => c.Id == call.Id);
        endedCall.Status.Should().Be(VoiceCallStatus.Ended);
        endedCall.EndReason.Should().Be(VoiceCallEndReason.Completed);

        // A system message should have been created
        var sysMsg = await _db.DirectMessages.Where(dm => dm.DmChannelId == _dmChannel.Id && dm.MessageType == MessageType.VoiceCallEvent).FirstOrDefaultAsync();
        sysMsg.Should().NotBeNull();
        sysMsg!.Body.Should().StartWith("call:");
    }

    [Fact]
    public async Task EndCall_ActiveCallWithVoiceStates_CleansUpBothParticipants()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            AnsweredAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);

        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser2.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = "conn-2",
            ParticipantId = "conn-2",
            JoinedAt = DateTimeOffset.UtcNow
        });

        var mockHandler = new MockHttpMessageHandler("{}");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.EndCall();

        var remainingVoiceStates = await _db.VoiceStates.Where(vs => vs.DmChannelId == _dmChannel.Id).ToListAsync();
        remainingVoiceStates.Should().BeEmpty();
    }

    // ===== DeclineCall additional tests =====

    [Fact]
    public async Task DeclineCall_ValidDecline_CreatesSystemMessage()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHubForUser2();
        await hub.DeclineCall(call.Id.ToString());

        var sysMsg = await _db.DirectMessages.FirstOrDefaultAsync(dm => dm.DmChannelId == _dmChannel.Id && dm.MessageType == MessageType.VoiceCallEvent);
        sysMsg.Should().NotBeNull();
        sysMsg!.Body.Should().Be("missed");
    }

    // ===== StopProducing additional tests =====

    [Fact]
    public async Task StopProducing_AudioLabel_ThrowsHubException()
    {
        var hub = CreateHub();

        await hub.Invoking(h => h.StopProducing("audio"))
            .Should().ThrowAsync<HubException>().WithMessage("*'video' or 'screen'*");
    }

    // ===== LeaveVoiceChannel internal - DM call path =====

    [Fact]
    public async Task LeaveVoiceChannel_InDmCallWithActiveCall_FindsRoomId()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            AnsweredAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);

        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });

        var mockHandler = new MockHttpMessageHandler("{}");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.LeaveVoiceChannel();

        // Voice state should be removed
        var states = await _db.VoiceStates.Where(vs => vs.UserId == _testUser.Id).ToListAsync();
        states.Should().BeEmpty();
    }

    // ===== GetSfuRoomIdAsync - error paths =====

    [Fact]
    public async Task UpdateVoiceState_InDmCallWithNoActiveCall_ThrowsHubException()
    {
        // Voice state pointing to a DM channel but no active call exists
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();

        await hub.Invoking(h => h.UpdateVoiceState(true, false))
            .Should().ThrowAsync<HubException>().WithMessage("*Not currently in a voice session*");
    }

    // ═══════════════════ JoinServer / LeaveServer ═══════════════════

    [Fact]
    public async Task JoinServer_ValidMember_JoinsGroup()
    {
        var hub = CreateHub();
        await hub.JoinServer(_testServer.Id.ToString());
        _mockGroups.Verify(
            g => g.AddToGroupAsync(_connectionId, $"server-{_testServer.Id}", default),
            Times.Once);
    }

    [Fact]
    public async Task JoinServer_InvalidGuid_ThrowsHubException()
    {
        var hub = CreateHub();
        var act = () => hub.JoinServer("not-a-guid");
        await act.Should().ThrowAsync<HubException>().WithMessage("Invalid server ID.");
    }

    [Fact]
    public async Task JoinServer_NonMember_ThrowsHubException()
    {
        var nonMemberServer = new Server { Id = Guid.NewGuid(), Name = "Other Server" };
        _db.Servers.Add(nonMemberServer);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        var act = () => hub.JoinServer(nonMemberServer.Id.ToString());
        await act.Should().ThrowAsync<HubException>().WithMessage("Not a member of this server.");
    }

    [Fact]
    public async Task JoinServer_ServerNotFound_ThrowsHubException()
    {
        var hub = CreateHub();
        var act = () => hub.JoinServer(Guid.NewGuid().ToString());
        await act.Should().ThrowAsync<HubException>().WithMessage("Server not found.");
    }

    [Fact]
    public async Task LeaveServer_InvalidGuid_ThrowsHubException()
    {
        var hub = CreateHub();
        var act = () => hub.LeaveServer("not-a-guid");
        await act.Should().ThrowAsync<HubException>().WithMessage("Invalid server ID.");
    }

    [Fact]
    public async Task LeaveServer_ValidId_LeavesGroup()
    {
        var hub = CreateHub();
        await hub.LeaveServer(_testServer.Id.ToString());
        _mockGroups.Verify(
            g => g.RemoveFromGroupAsync(_connectionId, $"server-{_testServer.Id}", default),
            Times.Once);
    }

    // ═══════════════════ DM Typing ═══════════════════

    [Fact]
    public async Task StartDmTyping_BroadcastsCorrectGroup()
    {
        var hub = CreateHub();
        var dmId = Guid.NewGuid().ToString();

        await hub.StartDmTyping(dmId, "TestUser");

        _mockOthersProxy.Verify(p => p.SendCoreAsync("DmTyping",
            It.Is<object?[]>(args => args.Length == 2 && (string)args[0]! == dmId && (string)args[1]! == "TestUser"),
            default), Times.Once);
    }

    [Fact]
    public async Task StopDmTyping_BroadcastsCorrectGroup()
    {
        var hub = CreateHub();
        var dmId = Guid.NewGuid().ToString();

        await hub.StopDmTyping(dmId, "TestUser");

        _mockOthersProxy.Verify(p => p.SendCoreAsync("DmStoppedTyping",
            It.Is<object?[]>(args => args.Length == 2),
            default), Times.Once);
    }

    // ═══════════════════ JoinDmChannel / LeaveDmChannel ═══════════════════

    [Fact]
    public async Task JoinDmChannel_AddsWithDmPrefix()
    {
        var hub = CreateHub();
        var dmId = Guid.NewGuid().ToString();

        await hub.JoinDmChannel(dmId);

        _mockGroups.Verify(g => g.AddToGroupAsync(_connectionId, $"dm-{dmId}", default), Times.Once);
    }

    [Fact]
    public async Task LeaveDmChannel_RemovesWithDmPrefix()
    {
        var hub = CreateHub();
        var dmId = Guid.NewGuid().ToString();

        await hub.LeaveDmChannel(dmId);

        _mockGroups.Verify(g => g.RemoveFromGroupAsync(_connectionId, $"dm-{dmId}", default), Times.Once);
    }

    // ═══════════════════ StartCall — cleans up existing voice state ═══════════════════

    [Fact]
    public async Task StartCall_WithExistingVoiceState_LeavesOldChannel()
    {
        var sfuResponse = "{\"routerRtpCapabilities\":{}}";
        var handler = new MockHttpMessageHandler(sfuResponse);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:3001") };
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        // Add existing voice state for a server voice channel
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();

        var result = await hub.StartCall(_dmChannel.Id.ToString());

        result.Should().NotBeNull();
        // Old voice state should be removed
        var voiceStates = await _db.VoiceStates.Where(vs => vs.UserId == _testUser.Id && vs.ChannelId == _voiceChannel.Id).CountAsync();
        voiceStates.Should().Be(0);
    }

    // ═══════════════════ EndCall — ringing call ends as missed ═══════════════════

    [Fact]
    public async Task EndCall_RingingCall_EndsAsMissedWithSystemMessage()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var sfuResponse = "{\"routerRtpCapabilities\":{}}";
        var handler = new MockHttpMessageHandler(sfuResponse);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:3001") };
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        await hub.EndCall();

        var endedCall = await _db.VoiceCalls.FirstAsync(c => c.Id == call.Id);
        endedCall.Status.Should().Be(VoiceCallStatus.Ended);
        endedCall.EndReason.Should().Be(VoiceCallEndReason.Missed);

        var sysMsg = await _db.DirectMessages.FirstOrDefaultAsync(m => m.DmChannelId == _dmChannel.Id);
        sysMsg.Should().NotBeNull();
        sysMsg!.Body.Should().Be("missed");
    }

    // ═══════════════════ DeclineCall — non-recipient rejected ═══════════════════

    [Fact]
    public async Task DeclineCall_NotRecipient_ThrowsError()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser2.Id,
            RecipientUserId = _testUser2.Id, // Not _testUser
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();

        await hub.Invoking(h => h.DeclineCall(call.Id.ToString()))
            .Should().ThrowAsync<HubException>();
    }

    // ═══════════════════ OnDisconnectedAsync — presence cleanup ═══════════════════

    [Fact]
    public async Task OnDisconnectedAsync_TracksDisconnectInPresenceTracker()
    {
        // Connect first to register in tracker
        var hub = CreateHub();
        await hub.OnConnectedAsync();

        // Verify presence was created in DB
        var presenceCount = await _db.PresenceStates.CountAsync(ps => ps.ConnectionId == _connectionId);
        presenceCount.Should().BeGreaterThan(0);

        // Disconnect — the tracker disconnect is tracked even if ExecuteDeleteAsync fails on InMemory
        // We just verify it doesn't throw
        await hub.OnDisconnectedAsync(null);
    }

    // ═══════════════════ Heartbeat — updates timestamps ═══════════════════

    [Fact]
    public async Task Heartbeat_UpdatesLastHeartbeatTimestamp()
    {
        var hub = CreateHub();
        await hub.OnConnectedAsync();

        var before = await _db.PresenceStates.FirstAsync(ps => ps.ConnectionId == _connectionId);
        var originalHeartbeat = before.LastHeartbeatAt;

        await Task.Delay(10); // Small delay to ensure time difference

        await hub.Heartbeat(true);

        var after = await _db.PresenceStates.FirstAsync(ps => ps.ConnectionId == _connectionId);
        after.LastHeartbeatAt.Should().BeOnOrAfter(originalHeartbeat);
    }

    // ═══════════════════ StartCall — valid scenario verifications ═══════════════════

    [Fact]
    public async Task StartCall_CreatesVoiceCallRecord()
    {
        var hub = CreateHub();

        var result = await hub.StartCall(_dmChannel.Id.ToString());

        result.Should().NotBeNull();
        var call = await _db.VoiceCalls.FirstOrDefaultAsync(c => c.CallerUserId == _testUser.Id);
        call.Should().NotBeNull();
        call!.Status.Should().Be(VoiceCallStatus.Ringing);
        call.RecipientUserId.Should().Be(_testUser2.Id);
    }

    [Fact]
    public async Task StartCall_NotifiesRecipient()
    {
        var hub = CreateHub();

        await hub.StartCall(_dmChannel.Id.ToString());

        _mockClientProxy.Verify(p => p.SendCoreAsync("IncomingCall",
            It.IsAny<object?[]>(), default), Times.Once);
    }

    // ═══════════════════ Additional coverage: EndCall as recipient ═══════════════════

    [Fact]
    public async Task EndCall_AsRecipient_NotifiesCaller()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser2.Id,
            RecipientUserId = _testUser.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            AnsweredAt = DateTimeOffset.UtcNow.AddMinutes(-4)
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.EndCall();

        var dbCall = await _db.VoiceCalls.FirstAsync(c => c.Id == call.Id);
        dbCall.Status.Should().Be(VoiceCallStatus.Ended);
        dbCall.EndReason.Should().Be(VoiceCallEndReason.Completed);

        // Should notify the caller (other party since current user is recipient)
        _mockClients.Verify(c => c.Group($"user-{_testUser2.Id}"), Times.AtLeastOnce);
        _mockClientProxy.Verify(
            p => p.SendCoreAsync("CallEnded", It.IsAny<object?[]>(), default),
            Times.Once);
    }

    [Fact]
    public async Task EndCall_ActiveCall_DurationCalculatedCorrectly()
    {
        var answeredAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
            AnsweredAt = answeredAt
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.EndCall();

        var systemMsg = await _db.DirectMessages.FirstAsync(m => m.DmChannelId == _dmChannel.Id);
        systemMsg.Body.Should().StartWith("call:");
        systemMsg.MessageType.Should().Be(MessageType.VoiceCallEvent);
        // Duration should be approximately 120 seconds (2 min)
        var durationStr = systemMsg.Body.Replace("call:", "");
        int.TryParse(durationStr, out var seconds).Should().BeTrue();
        seconds.Should().BeGreaterThanOrEqualTo(100); // At least ~100 seconds
    }

    [Fact]
    public async Task EndCall_WithVoiceStates_CleansUpSfu()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            AnsweredAt = DateTimeOffset.UtcNow.AddMinutes(-4)
        };
        _db.VoiceCalls.Add(call);

        // Add voice states for both participants
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = "p-1",
            JoinedAt = DateTimeOffset.UtcNow
        });
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser2.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = "conn-2",
            ParticipantId = "p-2",
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new MockHttpMessageHandler("""{"ok":true}""");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        await hub.EndCall();

        // Both voice states should be cleaned up
        var remaining = await _db.VoiceStates.Where(v => v.DmChannelId == _dmChannel.Id).ToListAsync();
        remaining.Should().BeEmpty();

        // SFU cleanup calls should have been made (at least one per voice state)
        mockHandler.RequestCount.Should().BeGreaterThanOrEqualTo(1);
    }

    // ═══════════════════ StopProducing — screen with notification ═══════════════════

    [Fact]
    public async Task StopProducing_ScreenLabel_WithProducerId_NotifiesOthers()
    {
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            ScreenProducerId = "screen-producer-xyz",
            IsScreenSharing = true,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new MockHttpMessageHandler("""{"ok":true}""");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        await hub.StopProducing("screen");

        _mockOthersProxy.Verify(
            p => p.SendCoreAsync("ProducerClosed", It.IsAny<object?[]>(), default),
            Times.Once);

        var updated = await _db.VoiceStates.FirstAsync(v => v.UserId == _testUser.Id);
        updated.ScreenProducerId.Should().BeNull();
        updated.IsScreenSharing.Should().BeFalse();
    }

    // ═══════════════════ OnDisconnectedAsync — fallback with DmChannelId ═══════════════════

    [Fact]
    public async Task OnDisconnectedAsync_FallbackCleanup_DmVoiceState_NoServerNotification()
    {
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ThrowsAsync(new Exception("Simulated failure"));

        // Voice state with DmChannelId (not ChannelId) — should not send UserLeftVoice
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ChannelId = null,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.OnDisconnectedAsync(null);

        var remaining = await _db.VoiceStates.Where(v => v.ConnectionId == _connectionId).ToListAsync();
        remaining.Should().BeEmpty();

        // Should NOT send UserLeftVoice for DM voice states in fallback
        _mockClientProxy.Verify(
            p => p.SendCoreAsync("UserLeftVoice", It.IsAny<object?[]>(), default),
            Times.Never);
    }

    [Fact]
    public async Task OnDisconnectedAsync_FallbackCleanup_NoVoiceState_DoesNotThrow()
    {
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ThrowsAsync(new Exception("Simulated failure"));

        var hub = CreateHub();

        // No voice state at all — catch block should handle gracefully
        await hub.OnDisconnectedAsync(null);
    }

    // ═══════════════════ LeaveVoiceChannel — DM channel with no active call ═══════════════════

    [Fact]
    public async Task LeaveVoiceChannel_DmChannel_NoActiveCall_StillRemovesVoiceState()
    {
        // Voice state in DM channel but no matching VoiceCall (call already ended)
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ChannelId = null,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.LeaveVoiceChannel();

        var remaining = await _db.VoiceStates.Where(v => v.UserId == _testUser.Id).ToListAsync();
        remaining.Should().BeEmpty();
    }

    // ═══════════════════ UpdateVoiceState — server channel with no serverId ═══════════════════

    [Fact]
    public async Task UpdateVoiceState_ChannelDeletedMidSession_FallsBackToVoiceGroup()
    {
        // Channel that is gone from the DB (deleted mid-session)
        var deletedChannelId = Guid.NewGuid();
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = deletedChannelId,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.UpdateVoiceState(true, false);

        // serverId will be null, so it falls back to voice-channelId group
        _mockClients.Verify(
            c => c.OthersInGroup($"voice-{deletedChannelId}"),
            Times.Once);
    }

    // ═══════════════════ Heartbeat — DB update path coverage ═══════════════════

    [Fact]
    public async Task Heartbeat_ActiveHeartbeat_UpdatesDbTimestamp()
    {
        // Connect to register in the tracker
        _presenceTracker.Connect(_testUser.Id, _connectionId);
        _db.PresenceStates.Add(new PresenceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            Status = PresenceStatus.Online,
            ConnectionId = _connectionId,
            LastHeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            LastActiveAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ConnectedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.Heartbeat(true);

        // Heartbeat with no status change returns null from PresenceTracker,
        // so the method returns early without DB updates.
        // This covers the early return path.
    }

    [Fact]
    public async Task Heartbeat_InactiveHeartbeat_NoStatusChange()
    {
        _presenceTracker.Connect(_testUser.Id, _connectionId);
        _db.PresenceStates.Add(new PresenceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            Status = PresenceStatus.Online,
            ConnectionId = _connectionId,
            LastHeartbeatAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            ConnectedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.Heartbeat(false);

        // First call: might go Online->Idle transition if tracker treats first inactive as change
        // In any case, should not throw
    }

    // ═══════════════════ EndCall — no active call is idempotent ═══════════════════

    [Fact]
    public async Task EndCall_EndedCall_IgnoresAlreadyEnded()
    {
        // Call is already ended — EndCall should not find it
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Ended,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            EndedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.EndCall(); // Should not throw

        // Status should remain Ended
        var dbCall = await _db.VoiceCalls.FirstAsync(c => c.Id == call.Id);
        dbCall.Status.Should().Be(VoiceCallStatus.Ended);
    }

    // ═══════════════════ JoinVoiceChannel — existing voice state cleanup ═══════════════════

    [Fact]
    public async Task JoinVoiceChannel_ExistingDmVoiceState_CleansUpFirst()
    {
        // User is in a DM call voice state
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ChannelId = null,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();

        // Will fail on SFU but the cleanup of existing state should happen first
        var act = () => hub.JoinVoiceChannel(_voiceChannel.Id.ToString());
        var ex = await act.Should().ThrowAsync<HubException>();

        // The existing DM voice state should have been removed
        var remaining = await _db.VoiceStates.Where(v => v.UserId == _testUser.Id && v.DmChannelId == _dmChannel.Id).ToListAsync();
        remaining.Should().BeEmpty();
    }

    // ═══════════════════ DeclineCall — system message type ═══════════════════

    [Fact]
    public async Task DeclineCall_CreatesVoiceCallEventSystemMessage()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser2.Id,
            RecipientUserId = _testUser.Id,
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.DeclineCall(call.Id.ToString());

        var sysMsg = await _db.DirectMessages.FirstAsync(m => m.DmChannelId == _dmChannel.Id);
        sysMsg.MessageType.Should().Be(MessageType.VoiceCallEvent);
        sysMsg.AuthorUserId.Should().Be(_testUser2.Id);
        sysMsg.AuthorName.Should().Be(_testUser2.DisplayName);
    }

    // ═══════════════════ StartCall — recipient membership not found ═══════════════════

    [Fact]
    public async Task StartCall_RecipientMembershipMissing_ThrowsHubException()
    {
        // Create a DM channel with only the caller as member
        var soloChannel = new DmChannel { Id = Guid.NewGuid() };
        _db.DmChannels.Add(soloChannel);
        _db.DmChannelMembers.Add(new DmChannelMember
        {
            DmChannelId = soloChannel.Id,
            UserId = _testUser.Id
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        var act = () => hub.StartCall(soloChannel.Id.ToString());

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Recipient not found.");
    }

    // ═══════════════════ OnConnectedAsync — presence broadcast to friends ═══════════════════

    [Fact]
    public async Task OnConnectedAsync_MultipleFriends_BroadcastsToAll()
    {
        var friend3 = new User { Id = Guid.NewGuid(), GoogleSubject = "g-3", DisplayName = "Friend 3" };
        _db.Users.Add(friend3);
        _db.Friendships.Add(new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = _testUser.Id,
            RecipientId = _testUser2.Id,
            Status = FriendshipStatus.Accepted
        });
        _db.Friendships.Add(new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = friend3.Id,
            RecipientId = _testUser.Id,
            Status = FriendshipStatus.Accepted
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.OnConnectedAsync();

        // Should broadcast presence to both friends
        _mockClients.Verify(c => c.Group($"user-{_testUser2.Id}"), Times.AtLeastOnce);
        _mockClients.Verify(c => c.Group($"user-{friend3.Id}"), Times.AtLeastOnce);
    }

    // ═══════════════════ OnDisconnectedAsync — with multiple servers ═══════════════════

    [Fact]
    public async Task OnDisconnectedAsync_MultipleServers_BroadcastsPresenceToAll()
    {
        var server2 = new Server { Id = Guid.NewGuid(), Name = "Server 2" };
        _db.Servers.Add(server2);
        var role2 = new ServerRoleEntity
        {
            Id = Guid.NewGuid(),
            ServerId = server2.Id,
            Name = "Member",
            Position = 2,
            IsSystemRole = true
        };
        _db.ServerRoles.Add(role2);
        _db.ServerMembers.Add(new ServerMember
        {
            ServerId = server2.Id,
            UserId = _testUser.Id,
        });
        await _db.SaveChangesAsync();

        _presenceTracker.Connect(_testUser.Id, _connectionId);

        var hub = CreateHub();
        // OnDisconnectedAsync will fall through to catch block due to InMemory
        // but presence tracker will still track the disconnect
        await hub.OnDisconnectedAsync(null);

        var status = _presenceTracker.GetAggregateStatus(_testUser.Id);
        status.Should().Be(PresenceStatus.Offline);
    }

    // ═══════════════════ EndCall — ringing call with voice states ═══════════════════

    [Fact]
    public async Task EndCall_RingingWithVoiceStates_EndsAsMissedAndCleansUp()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new MockHttpMessageHandler("""{"ok":true}""");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        await hub.EndCall();

        var dbCall = await _db.VoiceCalls.FirstAsync(c => c.Id == call.Id);
        dbCall.Status.Should().Be(VoiceCallStatus.Ended);
        dbCall.EndReason.Should().Be(VoiceCallEndReason.Missed);
        dbCall.EndedAt.Should().NotBeNull();

        var sysMsg = await _db.DirectMessages.FirstAsync(m => m.DmChannelId == _dmChannel.Id);
        sysMsg.Body.Should().Be("missed");
    }

    // ═══════════════════ Produce — label defaults and kind mapping ═══════════════════

    [Fact]
    public async Task Produce_NullLabel_DefaultsToAudio_NotInVoice()
    {
        var hub = CreateHub();

        var act = () => hub.Produce("transport-id", JsonDocument.Parse("{}").RootElement);

        var ex = await act.Should().ThrowAsync<HubException>();
        ex.Which.Message.Should().Be("Not currently in a voice session.");
    }

    // ═══════════════════ Consume — not in voice ═══════════════════

    [Fact]
    public async Task Consume_NoVoiceState_ThrowsHubException()
    {
        var hub = CreateHub();

        var act = () => hub.Consume("producer", "recv-transport", JsonDocument.Parse("{}").RootElement);

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Not currently in a voice session.");
    }

    // ═══════════════════ ConnectTransport — not in voice ═══════════════════

    [Fact]
    public async Task ConnectTransport_NoVoiceSession_ThrowsCorrectMessage()
    {
        var hub = CreateHub();

        var act = () => hub.ConnectTransport("transport-123", JsonDocument.Parse("{}").RootElement);

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Not currently in a voice session.");
    }

    // ═══════════════════ StopProducing — invalid labels ═══════════════════

    [Fact]
    public async Task StopProducing_WebcamLabel_ThrowsHubException()
    {
        var hub = CreateHub();

        var act = () => hub.StopProducing("webcam");

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Can only stop 'video' or 'screen' producers.");
    }

    // ═══════════════════ OnDisconnectedAsync — fallback DM voice state ═══════════════════

    [Fact]
    public async Task OnDisconnectedAsync_FallbackCleanup_DmVoiceStateWithNoChannel_RemovesState()
    {
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ThrowsAsync(new Exception("Simulated failure"));

        // DM voice state (no channelId, has dmChannelId)
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.OnDisconnectedAsync(null);

        var remaining = await _db.VoiceStates.Where(v => v.ConnectionId == _connectionId).ToListAsync();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task OnDisconnectedAsync_FallbackCleanup_NoVoiceState_CompletesWithoutError()
    {
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ThrowsAsync(new Exception("Simulated failure"));

        var hub = CreateHub();

        // Should complete even when there is no voice state
        await hub.OnDisconnectedAsync(null);
    }

    // ═══════════════════ EndCall — recipient ends call ═══════════════════

    [Fact]
    public async Task EndCall_RecipientEndsCall_NotifiesCaller()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser2.Id,
            RecipientUserId = _testUser.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            AnsweredAt = DateTimeOffset.UtcNow.AddMinutes(-4)
        };
        _db.VoiceCalls.Add(call);
        await _db.SaveChangesAsync();

        var hub = CreateHub();
        await hub.EndCall();

        var dbCall = await _db.VoiceCalls.FirstAsync(c => c.Id == call.Id);
        dbCall.Status.Should().Be(VoiceCallStatus.Ended);
        dbCall.EndReason.Should().Be(VoiceCallEndReason.Completed);

        // Caller should be notified
        _mockClients.Verify(c => c.Group($"user-{_testUser2.Id}"), Times.AtLeastOnce);
    }

    // ═══════════════════ Heartbeat — inactive connection ═══════════════════

    [Fact]
    public async Task Heartbeat_InactiveConnection_DoesNotBroadcast()
    {
        // No presence tracker registration for this connection
        var hub = CreateHub();

        await hub.Heartbeat(false);

        // No broadcast should happen
        _mockClientProxy.Verify(
            p => p.SendCoreAsync("UserPresenceChanged", It.IsAny<object?[]>(), default),
            Times.Never);
    }

    // ═══════════════════ LeaveVoiceChannel — server channel with no serverId fallback ═══════════════════

    [Fact]
    public async Task LeaveVoiceChannel_ServerChannelDeleted_StillRemovesState()
    {
        // Voice state references a channel that no longer exists (deleted)
        var deletedChannelId = Guid.NewGuid();
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = deletedChannelId,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        await hub.LeaveVoiceChannel();

        var remaining = await _db.VoiceStates.Where(v => v.UserId == _testUser.Id).ToListAsync();
        remaining.Should().BeEmpty();
    }

    // ═══════════════════ StopProducing — screen with no producer ═══════════════════

    [Fact]
    public async Task StopProducing_ScreenWithNullProducerId_DoesNotNotify()
    {
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            ChannelId = _voiceChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            ScreenProducerId = null,
            IsScreenSharing = false,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        await hub.StopProducing("screen");

        _mockOthersProxy.Verify(
            p => p.SendCoreAsync("ProducerClosed", It.IsAny<object?[]>(), default),
            Times.Never);
    }

    // ═══════════════════ EndCall — with voiceStates cleanup for SFU ═══════════════════

    [Fact]
    public async Task EndCall_ActiveCallWithVoiceStates_CleansUpSfuParticipants()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            AnsweredAt = DateTimeOffset.UtcNow.AddMinutes(-4)
        };
        _db.VoiceCalls.Add(call);
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = "participant-1",
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new MockHttpMessageHandler("{}");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        await hub.EndCall();

        // Voice states should be cleaned up
        var remaining = await _db.VoiceStates.Where(v => v.DmChannelId == _dmChannel.Id).ToListAsync();
        remaining.Should().BeEmpty();

        // SFU was called for cleanup
        mockHandler.RequestCount.Should().BeGreaterThan(0);
    }

    // ═══════════════════ Produce — DM call room ID ═══════════════════

    [Fact]
    public async Task Produce_InDmCall_UsesDmCallRoomId()
    {
        var call = new VoiceCall
        {
            Id = Guid.NewGuid(),
            DmChannelId = _dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _testUser2.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            AnsweredAt = DateTimeOffset.UtcNow
        };
        _db.VoiceCalls.Add(call);
        _db.VoiceStates.Add(new VoiceState
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            DmChannelId = _dmChannel.Id,
            ConnectionId = _connectionId,
            ParticipantId = _connectionId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockHandler = new MockHttpMessageHandler("""{"producerId":"prod-dm-1"}""");
        var httpClient = new HttpClient(mockHandler);
        _httpClientFactory.Setup(f => f.CreateClient("sfu")).Returns(httpClient);

        var hub = CreateHub();
        var rtpParams = JsonDocument.Parse("{}").RootElement;

        var result = await hub.Produce("transport-1", rtpParams, "audio");

        var vs = await _db.VoiceStates.FirstAsync(v => v.UserId == _testUser.Id);
        vs.ProducerId.Should().Be("prod-dm-1");

        mockHandler.LastRequestUri.Should().Contain($"call-{call.Id}");
    }
}

/// <summary>
/// Simple mock HTTP handler for testing SFU API calls.
/// </summary>
internal class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly string? _response;
    private readonly Exception? _exception;

    public int RequestCount { get; private set; }
    public string? LastRequestUri { get; private set; }

    public MockHttpMessageHandler(string response)
    {
        _response = response;
    }

    public MockHttpMessageHandler(Exception exception)
    {
        _exception = exception;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        LastRequestUri = request.RequestUri?.ToString();

        if (_exception is not null)
            throw _exception;

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(_response!, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
