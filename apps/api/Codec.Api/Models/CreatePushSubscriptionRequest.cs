using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class CreatePushSubscriptionRequest
{
    [Required]
    [StringLength(2048)]
    public string Endpoint { get; set; } = "";

    [Required]
    [StringLength(512)]
    public string P256dh { get; set; } = "";

    [Required]
    [StringLength(512)]
    public string Auth { get; set; } = "";
}
