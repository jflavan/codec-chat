using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class GoogleSignInRequest
{
    [Required]
    public string Credential { get; set; } = string.Empty;
}
