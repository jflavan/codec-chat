using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class StartDiscordImportRequest
{
    [Required]
    public string BotToken { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    [RegularExpression(@"^\d+$", ErrorMessage = "Discord guild ID must contain only digits.")]
    public string DiscordGuildId { get; set; } = string.Empty;
}
