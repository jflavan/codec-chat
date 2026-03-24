using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class CreatePushSubscriptionRequest
{
    [Required]
    public string Endpoint { get; set; } = "";

    [Required]
    public string P256dh { get; set; } = "";

    [Required]
    public string Auth { get; set; } = "";
}
