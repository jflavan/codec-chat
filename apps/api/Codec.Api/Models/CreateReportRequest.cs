using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class CreateReportRequest
{
    public required ReportType ReportType { get; init; }
    public required string TargetId { get; init; }

    [MaxLength(2000)]
    public required string Reason { get; init; }
}
