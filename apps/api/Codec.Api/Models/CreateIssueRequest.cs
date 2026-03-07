namespace Codec.Api.Models;

public record CreateIssueRequest(
    string Title,
    string Description,
    string? UserAgent = null,
    string? CurrentPage = null
);
