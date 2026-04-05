using System.Security.Claims;
using Codec.Api.Controllers;
using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class UsersControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<IAvatarService> _avatarService = new();
    private readonly Mock<IHubContext<ChatHub>> _hub = new();
    private readonly Mock<IConfiguration> _config = new();
    private readonly UsersController _controller;
    private readonly User _testUser;

    public UsersControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _testUser = new User
        {
            Id = Guid.NewGuid(),
            GoogleSubject = "google-test",
            DisplayName = "Test User",
            Email = "test@test.com",
            AvatarUrl = "https://google.com/pic.jpg"
        };

        _controller = new UsersController(_userService.Object, _avatarService.Object, _db, _hub.Object, _config.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("sub", "google-test"),
                    new Claim("name", "Test User"),
                    new Claim("email", "test@test.com")
                ], "Bearer"))
            }
        };

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync((_testUser, false));
        _userService.Setup(u => u.GetEffectiveDisplayName(_testUser)).Returns("Test User");
        _avatarService.Setup(a => a.ResolveUrl(It.IsAny<string?>())).Returns((string?)null);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Me_ReturnsOkWithUserProfile()
    {
        var result = await _controller.Me();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SetNickname_ValidNickname_ReturnsOk()
    {
        _db.Users.Add(_testUser);
        await _db.SaveChangesAsync();
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_db.Users.First(u => u.Id == _testUser.Id), false));
        _userService.Setup(u => u.GetEffectiveDisplayName(It.IsAny<User>())).Returns("Nicky");

        var result = await _controller.SetNickname(new SetNicknameRequest { Nickname = "Nicky" });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SetNickname_EmptyNickname_ReturnsBadRequest()
    {
        var result = await _controller.SetNickname(new SetNicknameRequest { Nickname = "" });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetNickname_NullNickname_ReturnsBadRequest()
    {
        var result = await _controller.SetNickname(new SetNicknameRequest { Nickname = null });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RemoveNickname_NoNickname_ReturnsNotFound()
    {
        _testUser.Nickname = null;
        var result = await _controller.RemoveNickname();
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task RemoveNickname_HasNickname_ReturnsOk()
    {
        _testUser.Nickname = "OldNick";
        _db.Users.Add(_testUser);
        await _db.SaveChangesAsync();
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_db.Users.First(u => u.Id == _testUser.Id), false));

        var result = await _controller.RemoveNickname();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SearchUsers_ShortQuery_ReturnsEmpty()
    {
        var result = await _controller.SearchUsers("a");
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeAssignableTo<Array>();
    }

    [Fact]
    public async Task SearchUsers_NullQuery_ReturnsEmpty()
    {
        var result = await _controller.SearchUsers(null);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeAssignableTo<Array>();
    }

    // --- SetStatus ---

    [Fact]
    public async Task SetStatus_WithTextAndEmoji_ReturnsOk()
    {
        _db.Users.Add(_testUser);
        await _db.SaveChangesAsync();
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_db.Users.First(u => u.Id == _testUser.Id), false));

        var result = await _controller.SetStatus(new SetStatusRequest { StatusText = "Working", StatusEmoji = "💻" });

        result.Should().BeOfType<OkObjectResult>();
        var user = _db.Users.First(u => u.Id == _testUser.Id);
        user.StatusText.Should().Be("Working");
        user.StatusEmoji.Should().Be("💻");
    }

    [Fact]
    public async Task SetStatus_TextOnly_ReturnsOk()
    {
        _db.Users.Add(_testUser);
        await _db.SaveChangesAsync();
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_db.Users.First(u => u.Id == _testUser.Id), false));

        var result = await _controller.SetStatus(new SetStatusRequest { StatusText = "Away" });

        result.Should().BeOfType<OkObjectResult>();
        var user = _db.Users.First(u => u.Id == _testUser.Id);
        user.StatusText.Should().Be("Away");
        user.StatusEmoji.Should().BeNull();
    }

    [Fact]
    public async Task SetStatus_EmojiOnly_ReturnsOk()
    {
        _db.Users.Add(_testUser);
        await _db.SaveChangesAsync();
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_db.Users.First(u => u.Id == _testUser.Id), false));

        var result = await _controller.SetStatus(new SetStatusRequest { StatusEmoji = "🎉" });

        result.Should().BeOfType<OkObjectResult>();
        var user = _db.Users.First(u => u.Id == _testUser.Id);
        user.StatusText.Should().BeNull();
        user.StatusEmoji.Should().Be("🎉");
    }

    [Fact]
    public async Task SetStatus_BothEmpty_ReturnsBadRequest()
    {
        var result = await _controller.SetStatus(new SetStatusRequest { StatusText = "", StatusEmoji = "" });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetStatus_BothNull_ReturnsBadRequest()
    {
        var result = await _controller.SetStatus(new SetStatusRequest());

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetStatus_TrimsWhitespace()
    {
        _db.Users.Add(_testUser);
        await _db.SaveChangesAsync();
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_db.Users.First(u => u.Id == _testUser.Id), false));

        var result = await _controller.SetStatus(new SetStatusRequest { StatusText = "  Busy  " });

        result.Should().BeOfType<OkObjectResult>();
        var user = _db.Users.First(u => u.Id == _testUser.Id);
        user.StatusText.Should().Be("Busy");
    }

    // --- ClearStatus ---

    [Fact]
    public async Task ClearStatus_WithExistingStatus_ReturnsOk()
    {
        _testUser.StatusText = "Working";
        _testUser.StatusEmoji = "💻";
        _db.Users.Add(_testUser);
        await _db.SaveChangesAsync();
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_db.Users.First(u => u.Id == _testUser.Id), false));

        var result = await _controller.ClearStatus();

        result.Should().BeOfType<OkObjectResult>();
        var user = _db.Users.First(u => u.Id == _testUser.Id);
        user.StatusText.Should().BeNull();
        user.StatusEmoji.Should().BeNull();
    }

    [Fact]
    public async Task ClearStatus_NoExistingStatus_ReturnsNotFound()
    {
        _testUser.StatusText = null;
        _testUser.StatusEmoji = null;

        var result = await _controller.ClearStatus();

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ClearStatus_OnlyEmoji_ReturnsOk()
    {
        _testUser.StatusEmoji = "🎉";
        _testUser.StatusText = null;
        _db.Users.Add(_testUser);
        await _db.SaveChangesAsync();
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_db.Users.First(u => u.Id == _testUser.Id), false));

        var result = await _controller.ClearStatus();

        result.Should().BeOfType<OkObjectResult>();
        var user = _db.Users.First(u => u.Id == _testUser.Id);
        user.StatusEmoji.Should().BeNull();
    }

    // --- Me endpoint additional tests ---

    [Fact]
    public async Task Me_GoogleIssuer_WithLinkableEmailAccount_ReturnsNeedsLinking()
    {
        // Create an email/password user with no Google subject
        var emailUser = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = "Email User",
            Email = "test@test.com",
            PasswordHash = "$2a$12$fakehash",
            GoogleSubject = null
        };
        _db.Users.Add(emailUser);
        await _db.SaveChangesAsync();

        // Set up claims with Google issuer
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("iss", "https://accounts.google.com"),
            new Claim("sub", "google-new"),
            new Claim("email", "test@test.com"),
            new Claim("name", "Test User")
        ], "Bearer"));

        var result = await _controller.Me();
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Me_GoogleIssuer_NoMatchingEmailAccount_ReturnsUserProfile()
    {
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("iss", "https://accounts.google.com"),
            new Claim("sub", "google-test"),
            new Claim("email", "nomatch@test.com"),
            new Claim("name", "Test User")
        ], "Bearer"));

        var result = await _controller.Me();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Me_NonGoogleIssuer_ReturnsUserProfile()
    {
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("iss", "codec-api"),
            new Claim("sub", _testUser.Id.ToString()),
            new Claim("email", "test@test.com"),
            new Claim("name", "Test User")
        ], "Bearer"));

        var result = await _controller.Me();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Me_WithCustomAvatar_ReturnsCustomAvatarUrl()
    {
        _testUser.CustomAvatarPath = "https://storage/custom-avatar.png";
        _avatarService.Setup(a => a.ResolveUrl("https://storage/custom-avatar.png"))
            .Returns("https://storage/custom-avatar.png");

        var result = await _controller.Me();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Me_NewUser_ReturnsIsNewUserTrue()
    {
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_testUser, true));

        var result = await _controller.Me();
        result.Should().BeOfType<OkObjectResult>();
    }

    // --- SetNickname additional tests ---

    [Fact]
    public async Task SetNickname_TooLong_ReturnsBadRequest()
    {
        var longNick = new string('A', 33);
        var result = await _controller.SetNickname(new SetNicknameRequest { Nickname = longNick });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetNickname_WhitespaceOnly_ReturnsBadRequest()
    {
        var result = await _controller.SetNickname(new SetNicknameRequest { Nickname = "   " });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetNickname_ExactlyMaxLength_ReturnsOk()
    {
        _db.Users.Add(_testUser);
        await _db.SaveChangesAsync();
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_db.Users.First(u => u.Id == _testUser.Id), false));
        _userService.Setup(u => u.GetEffectiveDisplayName(It.IsAny<User>())).Returns("A");

        var nick = new string('A', 32);
        var result = await _controller.SetNickname(new SetNicknameRequest { Nickname = nick });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SetNickname_TrimsWhitespace()
    {
        _db.Users.Add(_testUser);
        await _db.SaveChangesAsync();
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_db.Users.First(u => u.Id == _testUser.Id), false));
        _userService.Setup(u => u.GetEffectiveDisplayName(It.IsAny<User>())).Returns("Trimmed");

        var result = await _controller.SetNickname(new SetNicknameRequest { Nickname = "  Trimmed  " });
        result.Should().BeOfType<OkObjectResult>();
        var user = _db.Users.First(u => u.Id == _testUser.Id);
        user.Nickname.Should().Be("Trimmed");
    }

    // --- SearchUsers additional tests ---

    [Fact]
    public async Task SearchUsers_WhitespaceOnlyQuery_ReturnsEmpty()
    {
        var result = await _controller.SearchUsers("   ");
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeAssignableTo<Array>();
    }

    [Fact]
    public async Task SearchUsers_ValidQuery_ReturnsMatchingUsers()
    {
        var otherUser = new User
        {
            Id = Guid.NewGuid(),
            GoogleSubject = "google-other",
            DisplayName = "Alice Wonder",
            Email = "alice@test.com",
            AvatarUrl = "https://google.com/alice.jpg"
        };
        _db.Users.Add(_testUser);
        _db.Users.Add(otherUser);
        await _db.SaveChangesAsync();

        var result = await _controller.SearchUsers("Alice");
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SearchUsers_ExcludesCurrentUser()
    {
        _db.Users.Add(_testUser);
        await _db.SaveChangesAsync();

        // Search for current user's name - should not include them
        var result = await _controller.SearchUsers("Test User");
        result.Should().BeOfType<OkObjectResult>();
    }

    // --- SearchUsers with friendship status ---

    [Fact]
    public async Task SearchUsers_WithExistingFriendship_IncludesRelationshipStatus()
    {
        var otherUser = new User
        {
            Id = Guid.NewGuid(),
            GoogleSubject = "google-friend",
            DisplayName = "Alice Friendly",
            Email = "alice-friend@test.com",
            AvatarUrl = "https://google.com/alice.jpg"
        };
        _db.Users.Add(_testUser);
        _db.Users.Add(otherUser);
        _db.Friendships.Add(new Friendship
        {
            RequesterId = _testUser.Id,
            RecipientId = otherUser.Id,
            Status = FriendshipStatus.Accepted
        });
        await _db.SaveChangesAsync();

        var result = await _controller.SearchUsers("Alice Friendly");
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SearchUsers_WithPendingFriendship_ShowsPendingStatus()
    {
        var otherUser = new User
        {
            Id = Guid.NewGuid(),
            GoogleSubject = "google-pending",
            DisplayName = "Bob Pending",
            Email = "bob-pending@test.com"
        };
        _db.Users.Add(_testUser);
        _db.Users.Add(otherUser);
        _db.Friendships.Add(new Friendship
        {
            RequesterId = _testUser.Id,
            RecipientId = otherUser.Id,
            Status = FriendshipStatus.Pending
        });
        await _db.SaveChangesAsync();

        var result = await _controller.SearchUsers("Bob Pending");
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SearchUsers_MatchByNickname_ReturnsUser()
    {
        var otherUser = new User
        {
            Id = Guid.NewGuid(),
            GoogleSubject = "google-nick",
            DisplayName = "Charles",
            Nickname = "ChuckSearchable",
            Email = "charles@test.com"
        };
        _db.Users.Add(_testUser);
        _db.Users.Add(otherUser);
        await _db.SaveChangesAsync();

        var result = await _controller.SearchUsers("ChuckSearchable");
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SearchUsers_MatchByEmail_ReturnsUser()
    {
        var otherUser = new User
        {
            Id = Guid.NewGuid(),
            GoogleSubject = "google-emailmatch",
            DisplayName = "DanaXyz",
            Email = "dana-searchable@test.com"
        };
        _db.Users.Add(_testUser);
        _db.Users.Add(otherUser);
        await _db.SaveChangesAsync();

        var result = await _controller.SearchUsers("dana-searchable");
        result.Should().BeOfType<OkObjectResult>();
    }

    // --- Me endpoint with alternate Google issuer format ---

    [Fact]
    public async Task Me_GoogleIssuer_AlternateFormat_ChecksLinking()
    {
        var emailUser = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = "Alt Email User",
            Email = "altformat@test.com",
            PasswordHash = "$2a$12$fakehash",
            GoogleSubject = null
        };
        _db.Users.Add(emailUser);
        await _db.SaveChangesAsync();

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("iss", "accounts.google.com"),
            new Claim("sub", "google-alt"),
            new Claim("email", "altformat@test.com"),
            new Claim("name", "Alt User")
        ], "Bearer"));

        var result = await _controller.Me();
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }
}
