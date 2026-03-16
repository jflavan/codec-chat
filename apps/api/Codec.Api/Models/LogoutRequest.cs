using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class LogoutRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
