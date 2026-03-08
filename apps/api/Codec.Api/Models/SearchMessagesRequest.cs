namespace Codec.Api.Models;

public class SearchMessagesRequest
{
    public required string Q { get; init; }
    public Guid? ChannelId { get; init; }
    public Guid? AuthorId { get; init; }
    public DateTimeOffset? Before { get; init; }
    public DateTimeOffset? After { get; init; }
    public string? Has { get; init; } // "image", "link"
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
