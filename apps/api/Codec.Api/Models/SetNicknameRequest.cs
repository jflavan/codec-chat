namespace Codec.Api.Models;

/// <summary>
/// Request body for setting or updating a user's nickname.
/// </summary>
public class SetNicknameRequest
{
    public string? Nickname { get; set; }
}
