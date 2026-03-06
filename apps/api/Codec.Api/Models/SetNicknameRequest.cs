using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

/// <summary>
/// Request body for setting or updating a user's nickname.
/// </summary>
public class SetNicknameRequest
{
    [Required, StringLength(32, MinimumLength = 1)]
    public string? Nickname { get; set; }
}
