namespace Codec.Api.Models;

public class Report
{
    public Guid Id { get; set; }
    public Guid ReporterId { get; set; }
    public User? Reporter { get; set; }
    public ReportType ReportType { get; set; }
    public string TargetId { get; set; } = string.Empty;
    public string? TargetSnapshot { get; set; }
    public string Reason { get; set; } = string.Empty;
    public ReportStatus Status { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public User? AssignedToUser { get; set; }
    public string? Resolution { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public User? ResolvedByUser { get; set; }
}
