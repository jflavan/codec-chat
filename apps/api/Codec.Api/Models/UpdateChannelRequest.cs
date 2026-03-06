using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record UpdateChannelRequest([Required, StringLength(100, MinimumLength = 1)] string Name);
