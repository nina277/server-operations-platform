namespace ServerOperations.Core.Models.Operations;

/// <summary>監視対象。テンプレート(docker-host / docker-compose-app / web-site)に基づき登録される。</summary>
public class MonitoringTarget
{
    public long Id { get; set; }

    public required string Name { get; set; }

    /// <summary>アダプターテンプレートID(コード内カタログで定義)。</summary>
    public required string TemplateId { get; set; }

    public string? Description { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 自動復旧の有効/無効。初期値はOFF(指示書6.2)。
    /// 有効時もLow操作(RESTART_ALLOWED_CONTAINER)のみが自動実行の対象となる。
    /// </summary>
    public bool AutoRecoveryEnabled { get; set; }

    /// <summary>
    /// 操作を許可するコンテナ名(JSON配列)。空 = どのコンテナも操作不可。
    /// 復旧の受付時と実行直前の両方で検証する。
    /// </summary>
    public string AllowedContainersJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public long? CreatedByUserId { get; set; }

    public TargetProfile? Profile { get; set; }

    public ICollection<TargetCredential> Credentials { get; set; } = [];
}
