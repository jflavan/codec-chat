namespace Codec.Api.Models;

/// <summary>
/// Payload for updating the user's custom server display order.
/// Contains an ordered list of server IDs representing the desired order.
/// </summary>
public class ReorderServersRequest
{
    /// <summary>
    /// Server IDs in the desired display order. The position in the list
    /// determines the <see cref="ServerMember.SortOrder"/> value.
    /// </summary>
    public List<Guid> ServerIds { get; set; } = new();
}
