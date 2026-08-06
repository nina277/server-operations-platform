namespace ServerOperations.Core.Services;

/// <summary>
/// 対象ごとに入切できる収集の単位。
///
/// 収集として独立して行う仕事だけを並べる。
/// 例えば再起動回数はコンテナ一覧に付いてくる値であり、単独では止められない。
/// 止められないものを選択肢に出すと、外しても何も変わらない設定になってしまう。
/// </summary>
public static class MonitorKinds
{
    /// <summary>コンテナ一覧の取得と、停止コンテナのインシデント化。</summary>
    public const string ContainerState = "container-state";

    /// <summary>
    /// 停止コンテナのログ末尾の取得。
    /// コンテナ一覧とは別のAPI呼び出しで、外せば取得しなくなる。
    /// ログに出したくない情報がある対象では外せるようにしておく。
    /// </summary>
    public const string LogExcerpt = "log-excerpt";

    /// <summary>HTTPヘルスチェック(死活と応答時間)。</summary>
    public const string HttpCheck = "http-check";

    public static readonly IReadOnlyList<string> All = [ContainerState, LogExcerpt, HttpCheck];
}
