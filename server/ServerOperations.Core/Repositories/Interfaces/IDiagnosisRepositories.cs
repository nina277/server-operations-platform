using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Repositories.Interfaces;

public interface IDiagnosticRuleRepository
{
    Task<List<DiagnosticRule>> GetEnabledAsync(CancellationToken ct = default);

    Task<List<DiagnosticRule>> GetAllAsync(CancellationToken ct = default);

    Task<DiagnosticRule?> FindByIdAsync(long id, CancellationToken ct = default);

    /// <summary>同じ名前のルールが既にあるか。idを渡すとそのルール自身は除いて調べる。</summary>
    Task<bool> ExistsByNameAsync(string name, long? excludeId = null, CancellationToken ct = default);

    Task<bool> AnyAsync(CancellationToken ct = default);

    Task AddAsync(DiagnosticRule rule, CancellationToken ct = default);

    Task AddRangeAsync(IEnumerable<DiagnosticRule> rules, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IDiagnosisRepository
{
    Task AddAsync(Diagnosis diagnosis, CancellationToken ct = default);

    Task<List<Diagnosis>> GetByIncidentAsync(long incidentId, CancellationToken ct = default);

    /// <summary>
    /// 障害署名が一致する過去インシデントの最新診断を検索する(履歴再利用用)。
    /// 現在のインシデント自身は除外する。
    /// </summary>
    Task<Diagnosis?> FindLatestBySignatureAsync(
        string signature, long excludeIncidentId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
