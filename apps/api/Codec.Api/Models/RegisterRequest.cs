using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class RegisterRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [Required, StringLength(32, MinimumLength = 2)]
    public string Nickname { get; set; } = string.Empty;
}
