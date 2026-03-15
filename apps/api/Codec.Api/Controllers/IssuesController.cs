using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Codec.Api.Controllers;

[ApiController]
[Authorize]
[Route("issues")]
public class IssuesController(IUserService userService) : ControllerBase
{
    private const int MaxTitleLength = 200;
    private const int MaxDescriptionLength = 5000;

    [HttpPost]
    public async Task<IActionResult> CreateIssue(
        [FromBody] CreateIssueRequest request,
        [FromServices] IGitHubIssueService? gitHubIssueService,
        CancellationToken ct)
    {
        if (gitHubIssueService is null)
            return StatusCode(501, new { error = "Bug reporting is not configured on this server." });

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > MaxTitleLength)
            return BadRequest(new { error = $"Title is required and must be {MaxTitleLength} characters or fewer." });

        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > MaxDescriptionLength)
            return BadRequest(new { error = $"Description is required and must be {MaxDescriptionLength} characters or fewer." });

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);

        var body = $"""
            ## Description

            {request.Description.Replace("---", "\\---")}

            ---

            **Submitted by:** {appUser.DisplayName}
            **Browser/OS:** {request.UserAgent ?? "Unknown"}
            **Page:** {request.CurrentPage ?? "Unknown"}
            """;

        try
        {
            var issueUrl = await gitHubIssueService.CreateIssueAsync(request.Title, body, ct);
            return Ok(new { issueUrl });
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { error = "Failed to create issue. Please try again later." });
        }
    }
}
