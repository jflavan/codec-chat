using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class UpdateMemberRoleRequest
{
    [Required]
    public string Role { get; set; } = string.Empty;
}
