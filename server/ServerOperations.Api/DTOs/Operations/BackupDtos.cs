using ServerOperations.Core.Models.Settings;

namespace ServerOperations.Api.DTOs.Operations;

public record BackupRunDto
{
    public required long Id { get; init; }

    public required string Status { get; init; }

    public required DateTime StartedAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    /// <summary>保存先のオブジェクトキー(接続情報・資格情報は含まない)。</summary>
    public string? ObjectKey { get; init; }

    public long? SizeBytes { get; init; }

    public string? Message { get; init; }

    public static BackupRunDto From(BackupRun run) => new()
    {
        Id = run.Id,
        Status = run.Status.ToString(),
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        ObjectKey = run.ObjectKey,
        SizeBytes = run.SizeBytes,
        Message = run.Message,
    };
}
