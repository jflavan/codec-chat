namespace Codec.Api.Models;

public record CreateMessageRequest(string Body, string? ImageUrl = null, Guid? ReplyToMessageId = null, Guid? ReplyToDirectMessageId = null);
