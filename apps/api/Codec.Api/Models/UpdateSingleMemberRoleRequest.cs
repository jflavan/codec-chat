using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

/// <summary>
/// Request body for the legacy single-role PATCH endpoint.
/// Use PUT /members/{userId}/roles for full multi-role management.
/// </summary>
public class UpdateSingleMemberRoleRequest
{
    [Required]
    public string Role { get; set; } = string.Empty;
}
