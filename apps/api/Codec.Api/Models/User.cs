namespace Codec.Api.Models;

public class User
{
    public Guid Id { get; set; }
    public string GoogleSubject { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Message> Messages { get; set; } = new();
    public List<ServerMember> ServerMemberships { get; set; } = new();
}
