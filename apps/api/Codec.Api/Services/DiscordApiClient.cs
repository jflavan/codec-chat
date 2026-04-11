using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codec.Api.Services;

public class DiscordApiClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public DiscordApiClient(HttpClient http)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://discord.com/api/v10/");
    }

    public void SetBotToken(string token)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", token);
    }

    public async Task<DiscordGuild> GetGuildAsync(string guildId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"guilds/{guildId}?with_counts=true", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DiscordGuild>(JsonOptions, ct))!;
    }

    public async Task<List<DiscordRole>> GetGuildRolesAsync(string guildId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"guilds/{guildId}/roles", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<DiscordRole>>(JsonOptions, ct))!;
    }

    public async Task<List<DiscordChannel>> GetGuildChannelsAsync(string guildId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"guilds/{guildId}/channels", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<DiscordChannel>>(JsonOptions, ct))!;
    }

    public async Task<List<DiscordGuildMember>> GetGuildMembersAsync(
        string guildId, int limit = 1000, string? after = null, CancellationToken ct = default)
    {
        var url = $"guilds/{guildId}/members?limit={limit}";
        if (after is not null) url += $"&after={after}";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<DiscordGuildMember>>(JsonOptions, ct))!;
    }

    public async Task<List<DiscordMessage>> GetChannelMessagesAsync(
        string channelId, int limit = 100, string? after = null, CancellationToken ct = default)
    {
        var url = $"channels/{channelId}/messages?limit={limit}";
        if (after is not null) url += $"&after={after}";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<DiscordMessage>>(JsonOptions, ct))!;
    }

    public async Task<List<DiscordMessage>> GetPinnedMessagesAsync(string channelId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"channels/{channelId}/pins", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<DiscordMessage>>(JsonOptions, ct))!;
    }

    public async Task<List<DiscordEmoji>> GetGuildEmojisAsync(string guildId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"guilds/{guildId}/emojis", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<DiscordEmoji>>(JsonOptions, ct))!;
    }

    public async Task<Stream> DownloadFileAsync(string url, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }
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
