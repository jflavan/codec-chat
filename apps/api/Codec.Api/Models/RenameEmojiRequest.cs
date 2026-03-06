using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record RenameEmojiRequest(
    [Required, RegularExpression(@"^[a-zA-Z0-9_]{2,32}$")] string Name);
