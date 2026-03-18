using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record CategoryOrderItem([Required] Guid CategoryId, [Required] int Position);

public record UpdateCategoryOrderRequest([Required] List<CategoryOrderItem> Categories);
