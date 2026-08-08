using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services;

/// <summary>
/// アクションが属する層。**危険度ではなく「誰が起動するか」で分ける。**
///
/// 危険度で分けると「Lowなら自動でよい」となり、操作を増やすほど
/// 無人実行の面積が広がる。無人で動くかどうかで分ければ、
/// 第2層に何を足しても無人実行の範囲は変わらない。
/// </summary>
public enum ActionTier
{
    /// <summary>
    /// 自動操作。システムが無人で起動しうる。**ここは4つに固定する。**
    /// 診断・AIの応答・ルールの推奨アクションが到達できるのはこの層だけ。
    /// </summary>
    Automatic = 0,

    /// <summary>
    /// 運用操作。**人が明示的に起動したときだけ動く。**
    /// 画面からの操作、または人が登録した予定による実行。
    /// 自動復旧の経路からは決して到達できない。
    /// </summary>
    Operator = 1,
}

/// <summary>
/// 復旧・運用アクションの定義。指示書6.2の許可リストに対応する。
/// </summary>
public record RecoveryActionDefinition(
    string ActionId,
    string Name,
    ActionRiskLevel RiskLevel,
    bool RequiresApproval,
    bool RequiresIdempotencyKey,
    bool RequiresTargetResource,
    string Description,
    ActionTier Tier = ActionTier.Automatic);

public interface IRecoveryActionCatalog
{
    IReadOnlyList<RecoveryActionDefinition> GetAll();

    RecoveryActionDefinition? Find(string actionId);

    /// <summary>
    /// 無人で実行しうるアクション(第1層)だけを返す。
    ///
    /// 診断の推奨アクション・AIの応答・ルールの設定値は、
    /// **すべてこの一覧で検証する。**
    /// </summary>
    IReadOnlyList<RecoveryActionDefinition> GetAutomatic();

    /// <summary>指定のアクションが無人で実行しうるか。</summary>
    bool IsAutomatic(string actionId);
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

    public IReadOnlyList<RecoveryActionDefinition> GetAutomatic() =>
        Definitions.Where(d => d.Tier == ActionTier.Automatic).ToList();

    public bool IsAutomatic(string actionId) =>
        Find(actionId) is { Tier: ActionTier.Automatic };

    public RecoveryActionDefinition? Find(string actionId) =>
        Definitions.FirstOrDefault(d => d.ActionId == actionId);
}
