using Microsoft.Extensions.Logging;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Services.Ai;

/// <summary>AI呼び出しを行わなかった/失敗した理由。</summary>
public enum AiSkipReason
{
    None = 0,
    Disabled = 1,
    ApiKeyMissing = 2,

    /// <summary>同一障害署名の履歴があるため再利用した。</summary>
    HistoryReused = 3,

    LimitReached = 4,
    ProviderFailure = 5,
    InvalidResponse = 6,
}

public record AiDiagnosisResult(
    Diagnosis? Diagnosis,
    AiSkipReason SkipReason,
    string? Message = null);

public interface IAiDiagnosisGateway
{
    /// <summary>
    /// AIによる診断を試みる。無効・上限到達・失敗時は診断を作らずに縮退する。
    /// 同一障害署名の過去診断があるときはAIを呼ばず履歴を再利用する。
    /// </summary>
    Task<AiDiagnosisResult> DiagnoseAsync(
        Incident incident, string? logExcerpt, CancellationToken ct = default);
}

/// <summary>
/// 外部AIによる診断のゲートウェイ。
///
/// 呼び出し前: 有効判定 → 履歴再利用 → 上限判定 → 匿名化・縮小
/// 呼び出し後: 応答検証 → 推奨アクションを対象能力・許可リストで再検証
/// 失敗時: 診断を作らず、ルール診断・履歴・通知へ縮退する(復旧は開始しない)
/// </summary>
public class AiDiagnosisGateway(
    IAiUsageLimitRepository limits,
    IAiUsageRecordRepository usageRecords,
    IAiApiKeyProvider apiKeyProvider,
    IAiDiagnosisProvider provider,
    IDiagnosisRepository diagnoses,
    IMonitoringTargetRepository targets,
    IAdapterTemplateCatalog templates,
    IRecoveryActionCatalog actionCatalog,
    TimeProvider timeProvider,
    ILogger<AiDiagnosisGateway> logger) : IAiDiagnosisGateway
{
    public async Task<AiDiagnosisResult> DiagnoseAsync(
        Incident incident, string? logExcerpt, CancellationToken ct = default)
    {
        var limit = await limits.GetAsync(ct);
        if (limit is null || !limit.IsEnabled)
        {
            return new AiDiagnosisResult(null, AiSkipReason.Disabled, "AI診断は無効です。");
        }

        // 同一障害署名の過去診断があればAIを呼ばずに再利用する
        var past = await diagnoses.FindLatestBySignatureAsync(incident.SignatureSha256, incident.Id, ct);
        if (past is not null)
        {
            var reused = await SaveDiagnosisAsync(new Diagnosis
            {
                IncidentId = incident.Id,
                TargetId = incident.TargetId,
                Source = DiagnosisSource.History,
                ReusedDiagnosisId = past.Id,
                Classification = past.Classification,
                Severity = past.Severity,
                Rationale = $"同一の障害署名を持つ過去の診断(#{past.Id})を再利用: {past.Rationale}",
                RecommendedActionId = past.RecommendedActionId,
                CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            }, incident.TargetId, ct);

            return new AiDiagnosisResult(
                reused, AiSkipReason.HistoryReused, "過去の診断を再利用したためAIを呼び出しませんでした。");
        }

        var apiKey = await apiKeyProvider.GetApiKeyAsync(ct);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiDiagnosisResult(
                null, AiSkipReason.ApiKeyMissing, "AIのAPIキーが設定されていません。");
        }

        var limitCheck = await CheckLimitsAsync(limit, ct);
        if (limitCheck is not null)
        {
            return new AiDiagnosisResult(null, AiSkipReason.LimitReached, limitCheck);
        }

        var allowedActionIds = await GetAllowedActionIdsAsync(incident.TargetId, ct);

        // 匿名化・重複行除去・文字数上限を適用してから送信する
        var preparedLog = AiInputSanitizer.Prepare(logExcerpt, limit.MaxInputCharacters);

        var input = new AiDiagnosisInput
        {
            Classification = incident.Classification,
            Service = incident.Service,
            LogExcerpt = preparedLog,
            AllowedActionIds = allowedActionIds,
        };

        var response = await provider.DiagnoseAsync(input, limit, apiKey, ct);
        await RecordUsageAsync(incident, limit, preparedLog.Length, response, ct);

        if (response.Outcome != AiCallOutcome.Success || response.Output is null)
        {
            var reason = response.Outcome switch
            {
                AiCallOutcome.RateLimited => AiSkipReason.LimitReached,
                AiCallOutcome.InvalidResponse => AiSkipReason.InvalidResponse,
                _ => AiSkipReason.ProviderFailure,
            };

            logger.LogInformation(
                "AI diagnosis did not produce a result for incident {IncidentId}: {Outcome}",
                incident.Id, response.Outcome);

            // 縮退: 診断を作らず、復旧も開始しない
            return new AiDiagnosisResult(null, reason, response.ErrorSummary);
        }

        var output = response.Output;
        var diagnosis = await SaveDiagnosisAsync(new Diagnosis
        {
            IncidentId = incident.Id,
            TargetId = incident.TargetId,
            Source = DiagnosisSource.Ai,
            Classification = output.Classification,
            Severity = output.Severity,
            Rationale = output.Confidence is { } confidence
                ? $"{output.Rationale}(確信度: {confidence:0.00})"
                : output.Rationale,
            RecommendedActionId = output.RecommendedActionId,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        }, incident.TargetId, ct);

        return new AiDiagnosisResult(diagnosis, AiSkipReason.None);
    }

    /// <summary>
    /// 推奨アクションを現在の対象能力・許可リストで再検証してから保存する。
    /// AIが返した値をそのまま信用しない。
    /// </summary>
    private async Task<Diagnosis> SaveDiagnosisAsync(
        Diagnosis diagnosis, long targetId, CancellationToken ct)
    {
        var allowed = await GetAllowedActionIdsAsync(targetId, ct);
        diagnosis.RecommendedActionAllowed =
            diagnosis.RecommendedActionId is { } actionId
            && allowed.Contains(actionId, StringComparer.Ordinal);

        // 許可されないアクションは推奨として保持しない
        if (!diagnosis.RecommendedActionAllowed)
        {
            diagnosis.RecommendedActionId = null;
        }

        await diagnoses.AddAsync(diagnosis, ct);
        await diagnoses.SaveChangesAsync(ct);
        return diagnosis;
    }

    /// <summary>
    /// 対象で実際に実行できるアクションID。テンプレートの許可操作かつ
    /// カタログに存在し、High危険度でないものに限る。
    /// </summary>
    private async Task<List<string>> GetAllowedActionIdsAsync(long targetId, CancellationToken ct)
    {
        var target = await targets.FindByIdAsync(targetId, ct);
        var template = target is null ? null : templates.Find(target.TemplateId);
        if (template is null)
        {
            return [];
        }

        return template.AllowedOperations
            .Where(id => actionCatalog.Find(id) is { RiskLevel: not ActionRiskLevel.High })
            .ToList();
    }

    /// <summary>月・日・時の上限を判定する。超過している場合は理由を返す。</summary>
    private async Task<string?> CheckLimitsAsync(AiUsageLimit limit, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var hourly = await usageRecords.CountSinceAsync(now.AddHours(-1), ct);
        if (hourly >= limit.HourlyLimit)
        {
            return $"1時間あたりの上限({limit.HourlyLimit}回)に達しています。";
        }

        var daily = await usageRecords.CountSinceAsync(now.AddDays(-1), ct);
        if (daily >= limit.DailyLimit)
        {
            return $"1日あたりの上限({limit.DailyLimit}回)に達しています。";
        }

        var monthly = await usageRecords.CountSinceAsync(now.AddDays(-30), ct);
        if (monthly >= limit.MonthlyLimit)
        {
            return $"月あたりの上限({limit.MonthlyLimit}回)に達しています。";
        }

        return null;
    }

    /// <summary>呼び出し記録。プロンプト・応答の本文は保存しない。</summary>
    private async Task RecordUsageAsync(
        Incident incident, AiUsageLimit limit, int inputCharacters,
        AiCallResponse response, CancellationToken ct)
    {
        await usageRecords.AddAsync(new AiUsageRecord
        {
            CalledAt = timeProvider.GetUtcNow().UtcDateTime,
            Provider = provider.ProviderName,
            Model = limit.Model,
            IncidentId = incident.Id,
            Result = response.Outcome switch
            {
                AiCallOutcome.Success => AiCallResult.Success,
                AiCallOutcome.InvalidResponse => AiCallResult.InvalidResponse,
                AiCallOutcome.RateLimited => AiCallResult.RateLimited,
                AiCallOutcome.Timeout => AiCallResult.Timeout,
                _ => AiCallResult.Error,
            },
            InputCharacters = inputCharacters,
            OutputTokens = response.OutputTokens,
            LatencyMs = response.LatencyMs,
            ErrorSummary = response.ErrorSummary,
        }, ct);
        await usageRecords.SaveChangesAsync(ct);
    }
}

/// <summary>AIのAPIキー取得。呼び出し時に復号し、保持・ログ出力しない。</summary>
public interface IAiApiKeyProvider
{
    Task<string?> GetApiKeyAsync(CancellationToken ct = default);
}
