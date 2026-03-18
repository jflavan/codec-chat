using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record RenameCategoryRequest([Required, StringLength(50, MinimumLength = 1)] string Name);
