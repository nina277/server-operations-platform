using System.ComponentModel.DataAnnotations;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Api.DTOs.Operations;

public record CreateApprovalRequest
{
    [Required]
    [MaxLength(64)]
    public required string ActionId { get; init; }

    [MaxLength(200)]
    public string? TargetResource { get; init; }

    /// <summary>true=承認, false=却下。</summary>
    public bool Approve { get; init; } = true;

    [MaxLength(500)]
    public string? Comment { get; init; }
}

public record ApprovalDto
{
    public required long Id { get; init; }

    public required long IncidentId { get; init; }

    public required string ActionId { get; init; }

    public string? TargetResource { get; init; }

    public required string Status { get; init; }

    public string? DecidedByUsername { get; init; }

    public DateTime? DecidedAt { get; init; }

    public required DateTime ExpiresAt { get; init; }

    public required bool IsConsumed { get; init; }

    public string? Comment { get; init; }

    public static ApprovalDto From(RecoveryApproval approval) => new()
    {
        Id = approval.Id,
        IncidentId = approval.IncidentId,
        ActionId = approval.ActionId,
        TargetResource = approval.TargetResource,
        Status = approval.Status.ToString(),
        DecidedByUsername = approval.DecidedByUsername,
        DecidedAt = approval.DecidedAt,
        ExpiresAt = approval.ExpiresAt,
        IsConsumed = approval.IsConsumed,
        Comment = approval.Comment,
    };
}

public record CreateRecoveryActionRequest
{
    [Required]
    [MaxLength(64)]
    public required string ActionId { get; init; }

    [MaxLength(200)]
    public string? TargetResource { get; init; }

    /// <summary>Medium操作で必須。承認済みのApproval ID。</summary>
    public long? ApprovalId { get; init; }
}

public record RecoveryActionDto
{
    public required long Id { get; init; }

    public required long IncidentId { get; init; }

    public required long TargetId { get; init; }

    public required string ActionId { get; init; }

    public string? TargetResource { get; init; }

    public required string RiskLevel { get; init; }

    public required string Status { get; init; }

    public long? ApprovalId { get; init; }

    public required DateTime RequestedAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    public string? ResultMessage { get; init; }

    public string? BlockedReason { get; init; }

    public static RecoveryActionDto From(RecoveryAction action) => new()
    {
        Id = action.Id,
        IncidentId = action.IncidentId,
        TargetId = action.TargetId,
        ActionId = action.ActionId,
        TargetResource = action.TargetResource,
        RiskLevel = action.RiskLevel.ToString(),
        Status = action.Status.ToString(),
        ApprovalId = action.ApprovalId,
        RequestedAt = action.RequestedAt,
        CompletedAt = action.CompletedAt,
        ResultMessage = action.ResultMessage,
        BlockedReason = action.BlockedReason,
    };
}

public record HealthCheckDto
{
    public required long Id { get; init; }

    public required long TargetId { get; init; }

    public long? RecoveryActionId { get; init; }

    public required string Status { get; init; }

    public required string Message { get; init; }

    public long? LatencyMs { get; init; }

    public required DateTime CheckedAt { get; init; }

    public static HealthCheckDto From(HealthCheck check) => new()
    {
        Id = check.Id,
        TargetId = check.TargetId,
        RecoveryActionId = check.RecoveryActionId,
        Status = check.Status.ToString(),
        Message = check.Message,
        LatencyMs = check.LatencyMs,
        CheckedAt = check.CheckedAt,
    };
}
