using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class VerifyEmailRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;
}
