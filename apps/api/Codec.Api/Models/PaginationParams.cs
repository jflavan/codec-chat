using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class PaginationParams
{
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 25;

    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public string? SortDir { get; set; }
}
