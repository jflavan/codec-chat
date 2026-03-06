namespace Codec.Api.Services;

public interface ICustomEmojiService
{
    string? Validate(IFormFile file);
    Task<string> SaveEmojiAsync(Guid serverId, string name, IFormFile file);
    Task DeleteEmojiAsync(string imageUrl);
}
