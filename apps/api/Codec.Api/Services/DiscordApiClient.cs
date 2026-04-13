using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codec.Api.Services;

public class DiscordApiClient
{
    private readonly HttpClient _http;
    private string? _botToken;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public DiscordApiClient(HttpClient http)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://discord.com/api/v10/");
    }

    public void SetBotToken(string token) => _botToken = token;

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        if (_botToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _botToken);
        return request;
    }

    private async Task<T> SendAsync<T>(string url, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, url);
        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct))!;
    }

    public Task<DiscordGuild> GetGuildAsync(string guildId, CancellationToken ct = default)
        => SendAsync<DiscordGuild>($"guilds/{guildId}?with_counts=true", ct);

    public Task<List<DiscordRole>> GetGuildRolesAsync(string guildId, CancellationToken ct = default)
        => SendAsync<List<DiscordRole>>($"guilds/{guildId}/roles", ct);

    public Task<List<DiscordChannel>> GetGuildChannelsAsync(string guildId, CancellationToken ct = default)
        => SendAsync<List<DiscordChannel>>($"guilds/{guildId}/channels", ct);

    public Task<List<DiscordGuildMember>> GetGuildMembersAsync(
        string guildId, int limit = 1000, string? after = null, CancellationToken ct = default)
    {
        var url = $"guilds/{guildId}/members?limit={limit}";
        if (after is not null) url += $"&after={after}";
        return SendAsync<List<DiscordGuildMember>>(url, ct);
    }

    public Task<List<DiscordMessage>> GetChannelMessagesAsync(
        string channelId, int limit = 100, string? before = null, string? after = null, CancellationToken ct = default)
    {
        var url = $"channels/{channelId}/messages?limit={limit}";
        if (before is not null) url += $"&before={before}";
        if (after is not null) url += $"&after={after}";
        return SendAsync<List<DiscordMessage>>(url, ct);
    }

    public Task<List<DiscordMessage>> GetPinnedMessagesAsync(string channelId, CancellationToken ct = default)
        => SendAsync<List<DiscordMessage>>($"channels/{channelId}/pins", ct);

    public Task<List<DiscordEmoji>> GetGuildEmojisAsync(string guildId, CancellationToken ct = default)
        => SendAsync<List<DiscordEmoji>>($"guilds/{guildId}/emojis", ct);

}

// Discord API response DTOs

public record DiscordGuild(string Id, string Name, string? Icon, int? ApproximateMemberCount);

public record DiscordRole(string Id, string Name, int Color, bool Hoist, int Position, long Permissions, bool Managed, bool Mentionable);

public record DiscordChannel(string Id, int Type, string? Name, int? Position, string? ParentId, List<DiscordPermissionOverwrite>? PermissionOverwrites);

public record DiscordPermissionOverwrite(string Id, int Type, string Allow, string Deny);

public record DiscordGuildMember(DiscordUser? User, string? Nick, string? Avatar, List<string>? Roles, string JoinedAt);

public record DiscordUser(string Id, string Username, string? GlobalName, string? Avatar, string? Discriminator);

public record DiscordMessage(string Id, string? Content, DiscordUser Author, string Timestamp, string? EditedTimestamp, List<DiscordAttachment>? Attachments, List<DiscordReaction>? Reactions, DiscordMessageReference? MessageReference, int Type);

public record DiscordAttachment(string Id, string Filename, int Size, string Url, string? ContentType);

public record DiscordReaction(int Count, DiscordReactionEmoji Emoji);

public record DiscordReactionEmoji(string? Id, string? Name);

public record DiscordMessageReference(string? MessageId, string? ChannelId, string? GuildId);

public record DiscordEmoji(string? Id, string? Name, bool? Animated, DiscordUser? User);
