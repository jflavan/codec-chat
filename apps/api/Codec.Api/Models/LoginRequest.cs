using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class LoginRequest : IRecaptchaRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    public string? RecaptchaToken { get; set; }
}
