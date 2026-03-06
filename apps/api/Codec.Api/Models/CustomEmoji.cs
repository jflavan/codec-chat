namespace Codec.Api.Models;

public class CustomEmoji
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public bool IsAnimated { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Server? Server { get; set; }
    public User? UploadedByUser { get; set; }
}
