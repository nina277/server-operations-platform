using ServerOperations.Core.Models.Auth;

namespace ServerOperations.Core.Repositories.Interfaces;

/// <summary>監査ログの検索条件。指定しなかった項目は絞り込みに使わない。</summary>
public record AuditLogFilter
{
    public string? ActorName { get; init; }

    public string? TargetType { get; init; }

    public string? Action { get; init; }

    public AuditResult? Result { get; init; }

    public DateTime? OccurredFromUtc { get; init; }

    public DateTime? OccurredToUtc { get; init; }
}

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog entry, CancellationToken ct = default);

    /// <summary>監査ログを新しい順に検索する。</summary>
    Task<(List<AuditLog> Items, long TotalCount)> SearchAsync(
        AuditLogFilter filter, int skip, int take, CancellationToken ct = default);

    /// <summary>絞り込みに使える対象種別と操作の一覧を返す。</summary>
    Task<(List<string> TargetTypes, List<string> Actions)> GetFilterOptionsAsync(
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
