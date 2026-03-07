using System.Text;
using System.Text.Json;

namespace Codec.Api.Services;

public interface IGitHubIssueService
{
    Task<string> CreateIssueAsync(string title, string body, CancellationToken ct = default);
}

public class GitHubIssueService(HttpClient httpClient, IConfiguration config) : IGitHubIssueService
{
    public async Task<string> CreateIssueAsync(string title, string body, CancellationToken ct = default)
    {
        var owner = config["GitHub:RepoOwner"] ?? throw new InvalidOperationException("GitHub:RepoOwner not configured.");
        var repo = config["GitHub:RepoName"] ?? throw new InvalidOperationException("GitHub:RepoName not configured.");

        var payload = JsonSerializer.Serialize(new
        {
            title,
            body,
            labels = new[] { "user-report" }
        });

        var request = new HttpRequestMessage(HttpMethod.Post, $"/repos/{owner}/{repo}/issues")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.GetProperty("html_url").GetString()!;
    }
}
