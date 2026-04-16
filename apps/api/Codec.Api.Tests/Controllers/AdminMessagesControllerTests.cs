using System.Security.Claims;
using Codec.Api.Controllers.Admin;
using Codec.Api.Data;
using Codec.Api.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Tests.Controllers;

public class AdminMessagesControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly AdminMessagesController _controller;
    private readonly User _testUser;
    private readonly Server _testServer;
    private readonly Channel _testChannel;

    public AdminMessagesControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _testUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "Test User" };
        _testServer = new Server { Name = "Test Server" };
        _testChannel = new Channel { Server = _testServer, Name = "general" };

        _db.Users.Add(_testUser);
        _db.Servers.Add(_testServer);
        _db.Channels.Add(_testChannel);
        _db.Messages.Add(new Message
        {
            ChannelId = _testChannel.Id,
            AuthorUserId = _testUser.Id,
            Body = "Hello world test message"
        });
        _db.Messages.Add(new Message
        {
            ChannelId = _testChannel.Id,
            AuthorUserId = _testUser.Id,
            Body = "Another message with keyword"
        });
        _db.SaveChanges();

        _controller = new AdminMessagesController(_db);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("sub", "admin-1"), new Claim("name", "Admin")
                ], "Bearer"))
            }
        };
    }

    // Note: SearchMessages uses EF.Functions.ILike which isn't supported by InMemoryDb.
    // These tests verify the controller handles null/empty input gracefully.
    // Full search tests are covered in integration tests.

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("x")]
    public async Task SearchMessages_InvalidSearchTerms_ReturnsBadRequest(string? search)
    {
        var result = await _controller.SearchMessages(search!, new PaginationParams());

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    public void Dispose() => _db.Dispose();
}
