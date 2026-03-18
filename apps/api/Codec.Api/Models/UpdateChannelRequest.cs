using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record UpdateChannelRequest(
    [StringLength(100, MinimumLength = 1)] string? Name,
    [StringLength(256)] string? Description);
