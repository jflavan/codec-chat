using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class OAuthCallbackRequest
{
    [Required]
    public string Code { get; set; } = string.Empty;
}
