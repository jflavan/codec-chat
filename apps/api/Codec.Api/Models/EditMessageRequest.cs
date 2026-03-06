using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record EditMessageRequest([Required, StringLength(4000, MinimumLength = 1)] string Body);
