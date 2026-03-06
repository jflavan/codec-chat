using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

/// <summary>
/// Request body for adding or removing a reaction on a message.
/// </summary>
public record ToggleReactionRequest([Required] string Emoji);
