using System.Security.Claims;
using Codec.Api.Controllers;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class IssuesControllerTests
{
    private readonly Mock<IUserService> _userService = new();
    private readonly IssuesController _controller;
    private readonly User _testUser = new() { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "Test User" };

    public IssuesControllerTests()
    {
        _controller = new IssuesController(_userService.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("sub", "g-1"), new Claim("name", "Test User")
                ], "Bearer"))
            }
        };

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync((_testUser, false));
    }

    [Fact]
    public async Task CreateIssue_NoGitHubService_Returns501()
    {
        var request = new CreateIssueRequest("Bug", "Description", null, null);
        var result = await _controller.CreateIssue(request, null, CancellationToken.None);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(501);
    }

    [Fact]
    public async Task CreateIssue_EmptyTitle_ReturnsBadRequest()
    {
        var ghService = new Mock<IGitHubIssueService>();
        var request = new CreateIssueRequest("", "Description", null, null);

        var result = await _controller.CreateIssue(request, ghService.Object, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateIssue_TitleTooLong_ReturnsBadRequest()
    {
        var ghService = new Mock<IGitHubIssueService>();
        var request = new CreateIssueRequest(new string('A', 201), "Description", null, null);

        var result = await _controller.CreateIssue(request, ghService.Object, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateIssue_EmptyDescription_ReturnsBadRequest()
    {
        var ghService = new Mock<IGitHubIssueService>();
        var request = new CreateIssueRequest("Bug", "", null, null);

        var result = await _controller.CreateIssue(request, ghService.Object, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateIssue_Valid_ReturnsOk()
    {
        var ghService = new Mock<IGitHubIssueService>();
        ghService.Setup(g => g.CreateIssueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://github.com/org/repo/issues/1");

        var request = new CreateIssueRequest("Bug Title", "Bug description", "Mozilla/5.0", "/dashboard");

        var result = await _controller.CreateIssue(request, ghService.Object, CancellationToken.None);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateIssue_GitHubError_Returns502()
    {
        var ghService = new Mock<IGitHubIssueService>();
        ghService.Setup(g => g.CreateIssueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("GitHub is down"));

        var request = new CreateIssueRequest("Bug", "Description", null, null);

        var result = await _controller.CreateIssue(request, ghService.Object, CancellationToken.None);
        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(502);
    }
}
