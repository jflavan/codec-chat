using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class UpdateMemberRolesRequest
{
    [Required]
    [MaxLength(250)]
    public List<Guid> RoleIds { get; set; } = [];
}
