namespace ServerOperations.Core.Models.Operations;

/// <summary>監視対象の非秘密設定値(テンプレート入力)。JSONで保持する。秘密値はTargetCredentialへ。</summary>
public class TargetProfile
{
    public long Id { get; set; }

    public long TargetId { get; set; }

    public MonitoringTarget? Target { get; set; }

    /// <summary>設定値(JSONオブジェクト: key → value)。</summary>
    public required string SettingsJson { get; set; }

    public DateTime UpdatedAt { get; set; }
}
