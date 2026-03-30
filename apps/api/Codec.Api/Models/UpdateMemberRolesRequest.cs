using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class UpdateMemberRolesRequest
{
    [Required]
    public List<Guid> RoleIds { get; set; } = [];
}
