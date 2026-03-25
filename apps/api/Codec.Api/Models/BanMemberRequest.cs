using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

/// <summary>
/// Request payload for banning a member from a server.
/// </summary>
public class BanMemberRequest
{
    /// <summary>
    /// Optional reason for the ban.
    /// </summary>
    [StringLength(512)]
    public string? Reason { get; set; }

    /// <summary>
    /// Whether to delete the user's messages from the server.
    /// </summary>
    public bool DeleteMessages { get; set; }
}
