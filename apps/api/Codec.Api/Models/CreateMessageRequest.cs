using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record CreateMessageRequest(
    [param: StringLength(8000)] string Body,
    string? ImageUrl = null,
    [param: StringLength(2048)] string? FileUrl = null,
    [param: StringLength(255)] string? FileName = null,
    long? FileSize = null,
    [param: StringLength(255)] string? FileContentType = null,
    Guid? ReplyToMessageId = null,
    Guid? ReplyToDirectMessageId = null);
