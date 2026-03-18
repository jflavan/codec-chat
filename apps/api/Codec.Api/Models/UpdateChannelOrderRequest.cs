using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record ChannelOrderItem(
    [Required] Guid ChannelId,
    Guid? CategoryId,
    [Required] int Position);

public record UpdateChannelOrderRequest([Required] List<ChannelOrderItem> Channels);
