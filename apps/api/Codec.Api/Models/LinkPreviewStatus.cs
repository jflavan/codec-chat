namespace Codec.Api.Models;

/// <summary>
/// Indicates the outcome of fetching metadata for a link preview.
/// </summary>
public enum LinkPreviewStatus
{
    /// <summary>Metadata fetch has not completed yet.</summary>
    Pending = 0,

    /// <summary>Metadata was successfully fetched.</summary>
    Success = 1,

    /// <summary>Fetch failed (timeout, unreachable, or no metadata found).</summary>
    Failed = 2
}
