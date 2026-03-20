using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class RegisterRequest : IRecaptchaRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    [Required, StringLength(32, MinimumLength = 2)]
    public string Nickname { get; set; } = string.Empty;

    public string? RecaptchaToken { get; set; }
}
