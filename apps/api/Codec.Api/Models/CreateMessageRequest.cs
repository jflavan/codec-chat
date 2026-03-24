namespace Codec.Api.Models;

public record CreateMessageRequest(
    string Body,
    string? ImageUrl = null,
    string? FileUrl = null,
    string? FileName = null,
    long? FileSize = null,
    string? FileContentType = null,
    Guid? ReplyToMessageId = null,
    Guid? ReplyToDirectMessageId = null);
