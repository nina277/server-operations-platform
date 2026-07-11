using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Repositories.Interfaces;

public interface IMetricSnapshotRepository
{
    Task AddAsync(MetricSnapshot snapshot, CancellationToken ct = default);

    Task<List<MetricSnapshot>> GetRecentAsync(
        long targetId, int limit, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public record IncidentSearchCriteria
{
    public string? Status { get; init; }

    public string? Severity { get; init; }

    public long? TargetId { get; init; }

    /// <summary>タイトル・分類の部分一致検索。</summary>
    public string? Search { get; init; }

    /// <summary>並び替えキー: lastOccurredAt / firstOccurredAt / severity / status。接頭辞 - で降順。</summary>
    public string Sort { get; init; } = "-lastOccurredAt";

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public interface IIncidentRepository
{
    Task<Incident?> FindByIdAsync(long id, CancellationToken ct = default);

    /// <summary>同一署名でClose以外の既存インシデントを検索する(重複集約用)。</summary>
    Task<Incident?> FindOpenBySignatureAsync(long targetId, string signature, CancellationToken ct = default);

    Task<(List<Incident> Items, long TotalCount)> SearchAsync(
        IncidentSearchCriteria criteria, CancellationToken ct = default);

    Task<Dictionary<IncidentStatus, int>> CountByStatusAsync(CancellationToken ct = default);

    Task<Dictionary<IncidentSeverity, int>> CountActiveBySeverityAsync(CancellationToken ct = default);

    Task AddAsync(Incident incident, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IIncidentLogRepository
{
    Task AddAsync(IncidentLog log, CancellationToken ct = default);

    Task<List<IncidentLog>> GetRecentAsync(long targetId, int limit, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
