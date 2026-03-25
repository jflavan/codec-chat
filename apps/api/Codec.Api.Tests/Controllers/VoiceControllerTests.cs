using System.Security.Claims;
using Codec.Api.Controllers;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class VoiceControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly IConfiguration _config;
    private readonly VoiceController _controller;
    private readonly User _testUser;
    private readonly User _otherUser;
    private readonly Server _server;

    public VoiceControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _testUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "Test User" };
        _otherUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-2", DisplayName = "Other User" };
        _server = new Server { Id = Guid.NewGuid(), Name = "Test Server" };
        _db.Users.AddRange(_testUser, _otherUser);
        _db.Servers.Add(_server);
        _db.SaveChanges();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Voice:TurnSecret"] = "test-secret-key-for-hmac",
                ["Voice:TurnServerUrl"] = "turn:test:3478"
            })
            .Build();

        _controller = new VoiceController(_db, _userService.Object, _config);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("sub", "g-1"), new Claim("name", "Test User")
                ], "Bearer"))
            }
        };

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_db.Users.First(u => u.Id == _testUser.Id), false));
        _userService.Setup(u => u.EnsureMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>()))
            .ReturnsAsync(new ServerMember { ServerId = _server.Id, UserId = _testUser.Id, RoleId = Guid.NewGuid() });
    }

    public void Dispose() => _db.Dispose();

    private Channel CreateVoiceChannel()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ServerId = _server.Id,
            Name = "Voice",
            Type = ChannelType.Voice,
            Server = _server
        };
        _db.Channels.Add(channel);
        _db.SaveChanges();
        return channel;
    }

    // --- GetVoiceStates ---

    [Fact]
    public async Task GetVoiceStates_VoiceChannel_ReturnsStates()
    {
        var channel = CreateVoiceChannel();
        _db.VoiceStates.Add(new VoiceState
        {
            UserId = _otherUser.Id,
            ChannelId = channel.Id,
            ParticipantId = "p1",
            ConnectionId = "c1",
            IsMuted = true,
            IsDeafened = false,
            IsVideoEnabled = true,
            IsScreenSharing = false,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetVoiceStates(channel.Id);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IEnumerable;
        list.Should().NotBeNull();
    }

    [Fact]
    public async Task GetVoiceStates_ChannelNotFound_ReturnsNotFound()
    {
        var result = await _controller.GetVoiceStates(Guid.NewGuid());

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetVoiceStates_TextChannel_ReturnsBadRequest()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ServerId = _server.Id,
            Name = "Text",
            Type = ChannelType.Text,
            Server = _server
        };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        var result = await _controller.GetVoiceStates(channel.Id);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetVoiceStates_EmptyChannel_ReturnsEmptyList()
    {
        var channel = CreateVoiceChannel();

        var result = await _controller.GetVoiceStates(channel.Id);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IList;
        list.Should().NotBeNull();
        list!.Count.Should().Be(0);
    }

    // --- UpdateVoiceState ---

    [Fact]
    public async Task UpdateVoiceState_UserInVoice_ReturnsUpdatedState()
    {
        _db.VoiceStates.Add(new VoiceState
        {
            UserId = _testUser.Id,
            ChannelId = Guid.NewGuid(),
            ParticipantId = "p1",
            ConnectionId = "c1",
            IsMuted = false,
            IsDeafened = false,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var request = new UpdateVoiceStateRequest(IsMuted: true, IsDeafened: true);
        var result = await _controller.UpdateVoiceState(request);

        result.Should().BeOfType<OkObjectResult>();
        var state = _db.VoiceStates.First(vs => vs.UserId == _testUser.Id);
        state.IsMuted.Should().BeTrue();
        state.IsDeafened.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateVoiceState_NotInVoice_ReturnsBadRequest()
    {
        var request = new UpdateVoiceStateRequest(IsMuted: true, IsDeafened: false);
        var result = await _controller.UpdateVoiceState(request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // --- GetActiveCall ---

    [Fact]
    public async Task GetActiveCall_NoActiveCall_ReturnsNoContent()
    {
        var result = await _controller.GetActiveCall();

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task GetActiveCall_HasRingingCall_ReturnsCall()
    {
        var dmChannel = new DmChannel();
        _db.DmChannels.Add(dmChannel);
        _db.VoiceCalls.Add(new VoiceCall
        {
            DmChannelId = dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _otherUser.Id,
            Status = VoiceCallStatus.Ringing,
            StartedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActiveCall();

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetActiveCall_HasActiveCall_ReturnsCall()
    {
        var dmChannel = new DmChannel();
        _db.DmChannels.Add(dmChannel);
        _db.VoiceCalls.Add(new VoiceCall
        {
            DmChannelId = dmChannel.Id,
            CallerUserId = _otherUser.Id,
            RecipientUserId = _testUser.Id,
            Status = VoiceCallStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            AnsweredAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActiveCall();

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetActiveCall_EndedCall_ReturnsNoContent()
    {
        var dmChannel = new DmChannel();
        _db.DmChannels.Add(dmChannel);
        _db.VoiceCalls.Add(new VoiceCall
        {
            DmChannelId = dmChannel.Id,
            CallerUserId = _testUser.Id,
            RecipientUserId = _otherUser.Id,
            Status = VoiceCallStatus.Ended,
            StartedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetActiveCall();

        result.Should().BeOfType<NoContentResult>();
    }

    // --- GetTurnCredentials ---

    [Fact]
    public async Task GetTurnCredentials_WithSecret_ReturnsCredentials()
    {
        var result = await _controller.GetTurnCredentials();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTurnCredentials_NoSecret_ThrowsInvalidOperation()
    {
        var emptyConfig = new ConfigurationBuilder().Build();
        var controller = new VoiceController(_db, _userService.Object, emptyConfig);
        controller.ControllerContext = _controller.ControllerContext;

        await FluentActions.Invoking(() => controller.GetTurnCredentials())
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetTurnCredentials_UsesConfiguredTurnUrl()
    {
        var result = await _controller.GetTurnCredentials();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        // The response should contain the configured URL
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("turn:test:3478");
    }
}
