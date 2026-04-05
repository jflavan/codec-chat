using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class DeleteAccountRequest
{
    [MaxLength(128)]
    public string? Password { get; set; }

    public string? GoogleCredential { get; set; }

    [Required]
    public string ConfirmationText { get; set; } = string.Empty;
}
