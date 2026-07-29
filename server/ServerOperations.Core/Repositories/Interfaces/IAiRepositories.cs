using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Repositories.Interfaces;

public interface IAiUsageRecordRepository
{
    Task AddAsync(AiUsageRecord record, CancellationToken ct = default);

    /// <summary>指定時刻以降の呼び出し回数(上限判定用)。</summary>
    Task<int> CountSinceAsync(DateTime sinceUtc, CancellationToken ct = default);

    Task<List<AiUsageRecord>> GetRecentAsync(int limit, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IAiUsageLimitRepository
{
    /// <summary>設定は1件のみ保持する。存在しない場合はnull。</summary>
    Task<AiUsageLimit?> GetAsync(CancellationToken ct = default);

    Task AddAsync(AiUsageLimit limit, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
