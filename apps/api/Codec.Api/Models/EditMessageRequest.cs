using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record EditMessageRequest([Required, MinLength(1)] string Body);
