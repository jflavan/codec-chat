using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class ClaimDiscordIdentityRequest
{
    [Required]
    [MaxLength(20)]
    public string DiscordUserId { get; set; } = string.Empty;
}
