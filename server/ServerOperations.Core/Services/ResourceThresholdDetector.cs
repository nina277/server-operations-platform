using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Services;

/// <summary>1コンテナ分のリソース使用率。</summary>
public record ContainerResourceSample(string ContainerName, ContainerStats Stats);

/// <summary>
/// しきい値ルールに当たった使用率。インシデント化の材料になる。
/// Subjectは逼迫している当事者(コンテナ名またはマウントポイント)。
/// </summary>
public record ResourceAlert(
    string Subject,
    DiagnosticRule Rule,
    string Rationale,
    DiagnosticContext Context);

public interface IResourceThresholdDetector
{
    /// <summary>
    /// コンテナのCPU・メモリ使用率を有効なルールにかけ、インシデントにすべきものを返す。
    /// コンテナごとに最優先の一致を1件だけ返す。
    /// </summary>
    Task<IReadOnlyList<ResourceAlert>> DetectContainerAsync(
        IReadOnlyList<ContainerResourceSample> samples, CancellationToken ct = default);

    /// <summary>
    /// ホストのファイルシステム使用率を有効なルールにかける。
    /// マウントポイントごとに最優先の一致を1件だけ返す。
    /// </summary>
    Task<IReadOnlyList<ResourceAlert>> DetectFilesystemAsync(
        IReadOnlyList<FilesystemUsage> filesystems, CancellationToken ct = default);
}

/// <summary>
/// しきい値ルールを「検知」に使うための判定。
///
/// これまでルールは、別の経路で作られたインシデントを説明するためだけに使われていた。
/// そのため使用率のようなしきい値ルールは、どれだけ逼迫しても
/// 自分ではインシデントを作れず、事実上動かないルールになっていた。
/// ここでルールを評価し、当たったものをインシデントの発生源として扱う。
/// </summary>
public class ResourceThresholdDetector(
    IDiagnosticRuleRepository rules,
    IRuleEngine ruleEngine) : IResourceThresholdDetector
{
    public Task<IReadOnlyList<ResourceAlert>> DetectContainerAsync(
        IReadOnlyList<ContainerResourceSample> samples, CancellationToken ct = default) =>
        DetectAsync(
            samples,
            s => s.ContainerName,
            s => BuildContext(s.Stats.CpuUsagePercent, s.Stats.MemoryUsagePercent, diskPercent: null),
            ct);

    public Task<IReadOnlyList<ResourceAlert>> DetectFilesystemAsync(
        IReadOnlyList<FilesystemUsage> filesystems, CancellationToken ct = default) =>
        DetectAsync(
            filesystems,
            f => f.Mountpoint,
            f => BuildContext(cpuPercent: null, memoryPercent: null, diskPercent: f.UsagePercent),
            ct);

    private async Task<IReadOnlyList<ResourceAlert>> DetectAsync<T>(
        IReadOnlyList<T> samples,
        Func<T, string> subjectOf,
        Func<T, DiagnosticContext?> contextOf,
        CancellationToken ct)
    {
        if (samples.Count == 0)
        {
            return [];
        }

        // ルールの取得は件数ぶんではなく1回だけ行う
        var enabledRules = await rules.GetEnabledAsync(ct);
        var alerts = new List<ResourceAlert>();

        foreach (var sample in samples)
        {
            var context = contextOf(sample);
            if (context is null)
            {
                // 使用率が1つも取れていない。値が無いことを正常と読み替えないため、
                // ここでは何も判定しない
                continue;
            }

            var matches = ruleEngine.Evaluate(enabledRules, context);
            if (matches.Count == 0)
            {
                continue;
            }

            var top = matches[0];
            alerts.Add(new ResourceAlert(subjectOf(sample), top.Rule, top.Rationale, context));
        }

        return alerts;
    }

    /// <summary>
    /// 判定に渡す文脈を作る。**使用率以外の項目は入れない。**
    ///
    /// 例えばコンテナ名を入れると、コンテナ名を条件にした状態ルールが
    /// 毎回の収集で一致してしまい、正常なコンテナのインシデントを作り続ける。
    /// 使用率だけを入れておけば、使用率を見るルール以外は一致しようがない。
    /// 当事者の名前はインシデント側(対象サービス名)で保持する。
    /// </summary>
    private static DiagnosticContext? BuildContext(
        double? cpuPercent, double? memoryPercent, double? diskPercent)
    {
        if (cpuPercent is null && memoryPercent is null && diskPercent is null)
        {
            return null;
        }

        return new DiagnosticContext
        {
            CpuUsagePercent = cpuPercent,
            MemoryUsagePercent = memoryPercent,
            DiskUsagePercent = diskPercent,
        };
    }
}
