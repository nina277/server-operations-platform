namespace ServerOperations.Core.Models.Settings;

/// <summary>
/// 管理APIへのアクセスを許可するネットワーク範囲(CIDR)。
/// 1件も登録がない場合は制限しない(初期セットアップ用)。登録がある場合は範囲外を拒否する。
/// </summary>
public class TrustedNetworkCidr
{
    public long Id { get; set; }

    /// <summary>CIDR表記(例: 192.168.1.0/24)。</summary>
    public required string Cidr { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedByUserId { get; set; }
}
