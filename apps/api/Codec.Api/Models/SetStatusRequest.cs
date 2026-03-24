using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

/// <summary>
/// Request body for setting or updating a user's custom status message.
/// </summary>
public class SetStatusRequest
{
    [StringLength(128)]
    public string? StatusText { get; set; }

    [StringLength(8)]
    public string? StatusEmoji { get; set; }
}
