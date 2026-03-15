using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class RefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
