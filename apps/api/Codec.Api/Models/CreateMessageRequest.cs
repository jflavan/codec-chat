using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record CreateMessageRequest(
    [property: StringLength(8000)] string Body,
    string? ImageUrl = null,
    [property: StringLength(2048)] string? FileUrl = null,
    [property: StringLength(255)] string? FileName = null,
    long? FileSize = null,
    [property: StringLength(255)] string? FileContentType = null,
    Guid? ReplyToMessageId = null,
    Guid? ReplyToDirectMessageId = null);
