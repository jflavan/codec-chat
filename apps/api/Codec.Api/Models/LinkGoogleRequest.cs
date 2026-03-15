using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class LinkGoogleRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string GoogleCredential { get; set; } = string.Empty;
}
