namespace ServerOperations.Core.Services;

/// <summary>
/// 収集間隔の正規化。対象ごとに間隔を変えられるようにするが、
/// 短すぎる値は対象のサーバーとDockerのAPIに負荷をかけるため上下限で丸める。
/// </summary>
public static class CollectionInterval
{
    /// <summary>これより短くはできない。1分未満は対象への負荷が見合わない。</summary>
    public const int MinSeconds = 60;

    /// <summary>これより長くはできない。1時間を超えると障害の検知が遅れすぎる。</summary>
    public const int MaxSeconds = 3600;

    /// <summary>
    /// cronで等間隔に表せる分数だけを使う。60の約数に限るのは、
    /// 例えば7分間隔にすると毎時0,7,…,56分の次が0分になり、
    /// その1回だけ4分間隔になってしまうため。
    /// 「n分ごと」と説明した通りに動くことを優先する。
    /// </summary>
    public static readonly IReadOnlyList<int> AllowedMinutes =
        [1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30, 60];

    /// <summary>秒の指定を、実際に使える間隔(秒)へ丸める。</summary>
    public static int Normalize(int seconds)
    {
        var clamped = Math.Clamp(seconds, MinSeconds, MaxSeconds);
        var minutes = (double)clamped / 60;

        // 丸めた結果が要求より長くなると検知が遅れるため、近い方ではなく
        // 「要求以下で最大の値」を選ぶ。ただし下限は必ず確保する。
        var chosen = AllowedMinutes.Where(m => m <= minutes).DefaultIfEmpty(AllowedMinutes[0]).Max();
        return chosen * 60;
    }

    /// <summary>間隔(秒)をHangfireのcron式へ変換する。</summary>
    public static string ToCron(int seconds)
    {
        var minutes = Normalize(seconds) / 60;
        return minutes switch
        {
            1 => "* * * * *",
            60 => "0 * * * *",
            _ => $"*/{minutes} * * * *",
        };
    }
}
