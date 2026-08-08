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

    /// <summary>
    /// この対象の収集間隔(秒)。nullなら全体の既定値を使う。
    /// 短くしすぎると対象とDockerのAPIに負荷をかけるため、
    /// 保存時に <see cref="CollectionInterval"/> の上下限で丸める。
    /// </summary>
    public int? CollectionIntervalSeconds { get; set; }

    /// <summary>
    /// この対象で行う収集の種類(JSON配列)。nullならテンプレートで行えるものすべて。
    ///
    /// 「どれができるか」はテンプレートが決め、「今回どれを使うか」はここが決める。
    /// 能力と運用上の選択を同じ場所に置くと、対象を1つ変えるだけで
    /// 同じテンプレートの他の対象まで影響することになる。
    /// </summary>
    public string? EnabledMonitorsJson { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public long? CreatedByUserId { get; set; }

    public TargetProfile? Profile { get; set; }

    public ICollection<TargetCredential> Credentials { get; set; } = [];
}
