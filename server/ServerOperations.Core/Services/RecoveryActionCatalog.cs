using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services;

/// <summary>
/// 復旧アクションの定義。指示書6.2の許可リストに対応する。
/// High操作はこのカタログに存在せず、実行APIも提供しない。
/// </summary>
public record RecoveryActionDefinition(
    string ActionId,
    string Name,
    ActionRiskLevel RiskLevel,
    bool RequiresApproval,
    bool RequiresIdempotencyKey,
    bool RequiresTargetResource,
    string Description);

public interface IRecoveryActionCatalog
{
    IReadOnlyList<RecoveryActionDefinition> GetAll();

    RecoveryActionDefinition? Find(string actionId);
}

public class RecoveryActionCatalog : IRecoveryActionCatalog
{
    public const string RecheckHttpHealth = "RECHECK_HTTP_HEALTH";
    public const string RestartAllowedContainer = "RESTART_ALLOWED_CONTAINER";
    public const string StartAllowedContainer = "START_ALLOWED_CONTAINER";
    public const string StopAllowedContainer = "STOP_ALLOWED_CONTAINER";

    private static readonly RecoveryActionDefinition[] Definitions =
    [
        new(
            RecheckHttpHealth,
            "HTTPヘルスチェック再実行",
            ActionRiskLevel.Low,
            RequiresApproval: false,
            RequiresIdempotencyKey: false,
            RequiresTargetResource: false,
            "副作用なし。対象のHTTPヘルスチェックを再実行する。"),
        new(
            RestartAllowedContainer,
            "許可済みコンテナの再起動",
            ActionRiskLevel.Low,
            RequiresApproval: false,
            RequiresIdempotencyKey: true,
            RequiresTargetResource: true,
            "対象別許可・ルール一致・クールダウン・回数上限・ヘルスチェックを条件に再起動する。"),
        new(
            StartAllowedContainer,
            "許可済みコンテナの開始",
            ActionRiskLevel.Medium,
            RequiresApproval: true,
            RequiresIdempotencyKey: true,
            RequiresTargetResource: true,
            "管理者承認・MFA再認証・Idempotency-Keyが必須。"),
        new(
            StopAllowedContainer,
            "許可済みコンテナの停止",
            ActionRiskLevel.Medium,
            RequiresApproval: true,
            RequiresIdempotencyKey: true,
            RequiresTargetResource: true,
            "管理者承認・MFA再認証・Idempotency-Keyが必須。"),
    ];

    public IReadOnlyList<RecoveryActionDefinition> GetAll() => Definitions;

    public RecoveryActionDefinition? Find(string actionId) =>
        Definitions.FirstOrDefault(d => d.ActionId == actionId);
}
