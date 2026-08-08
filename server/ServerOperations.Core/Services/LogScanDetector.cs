using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Services;

/// <summary>1コンテナ分のログ末尾。秘密情報を伏せる前の生の値。</summary>
public record ContainerLogSample(string ContainerName, string LogExcerpt);

/// <summary>
/// ログのルールに当たった検知。インシデント化の材料になる。
/// </summary>
/// <param name="MatchedValue">
/// 一致した部分だけ。障害署名に使う。ログ末尾そのものを使うと
/// 行が流れるたびに署名が変わり、同じ障害が別件として積み上がる。
/// </param>
/// <param name="MaskedLog">秘密情報を伏せたログ末尾。インシデントの根拠として保存する。</param>
public record LogAlert(
    string ContainerName,
    DiagnosticRule Rule,
    string Rationale,
    string MatchedValue,
    string MaskedLog,
    DiagnosticContext Context);

public interface ILogScanDetector
{
    /// <summary>
    /// コンテナのログ末尾を有効なルールにかけ、インシデントにすべきものを返す。
    /// コンテナごとに最優先の一致を1件だけ返す。
    /// </summary>
    Task<IReadOnlyList<LogAlert>> DetectAsync(
        IReadOnlyList<ContainerLogSample> samples, CancellationToken ct = default);
}

/// <summary>
/// ログのルールを「検知」に使うための判定。
///
/// これまでログ末尾は、**停止した(exited/dead)コンテナからしか取得していなかった。**
/// そのため「稼働したままエラーを出し続けるコンテナ」は一度も読まれず、
/// ログ検知のルール(メモリ逼迫・ディスク逼迫)は停止後にしか当たらなかった。
/// 実環境試験のSC-04(tmpfsを満たす。コンテナは動き続ける)が検知できなかったのはこのため。
///
/// しきい値ルールに対する <see cref="ResourceThresholdDetector"/> と同じ位置づけで、
/// ログを評価し、当たったものをインシデントの発生源として扱う。
/// </summary>
public class LogScanDetector(
    IDiagnosticRuleRepository rules,
    IRuleEngine ruleEngine) : ILogScanDetector
{
    public async Task<IReadOnlyList<LogAlert>> DetectAsync(
        IReadOnlyList<ContainerLogSample> samples, CancellationToken ct = default)
    {
        if (samples.Count == 0)
        {
            return [];
        }

        // ルールの取得は件数ぶんではなく1回だけ行う
        var enabledRules = await rules.GetEnabledAsync(ct);
        var alerts = new List<LogAlert>();

        foreach (var sample in samples)
        {
            if (string.IsNullOrWhiteSpace(sample.LogExcerpt))
            {
                continue;
            }

            // 判定に渡す前に伏せる。以降の経路(診断・AI・保存)へ生の値を出さない。
            var masked = LogMasker.MaskSecrets(sample.LogExcerpt);

            var context = BuildContext(masked);
            var matches = ruleEngine.Evaluate(enabledRules, context);
            if (matches.Count == 0)
            {
                continue;
            }

            var top = matches[0];
            alerts.Add(new LogAlert(
                sample.ContainerName,
                top.Rule,
                top.Rationale,
                // 一致した実値が取れない種別のルールは署名を作れないため、分類で代用する
                top.MatchedValue ?? top.Rule.Classification,
                masked,
                context));
        }

        return alerts;
    }

    /// <summary>
    /// 判定に渡す文脈を作る。**ログ以外の項目は入れない。**
    ///
    /// <see cref="ResourceThresholdDetector"/> と同じ理由による。
    /// 例えばコンテナ状態を入れると、状態ルール(コンテナ停止)が
    /// 稼働中のコンテナに対して評価され、検知の意味が変わってしまう。
    /// ログだけを入れておけば、ログを見るルール以外は一致しようがない。
    /// </summary>
    private static DiagnosticContext BuildContext(string maskedLog) =>
        new() { LogExcerpt = maskedLog };
}
