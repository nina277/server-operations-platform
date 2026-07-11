namespace ServerOperations.Core.Models.Operations;

/// <summary>監視対象の資格情報。値はData Protectionで暗号化して保存する。</summary>
public class TargetCredential
{
    public long Id { get; set; }

    public long TargetId { get; set; }

    public MonitoringTarget? Target { get; set; }

    /// <summary>資格情報の種別(テンプレートのsecret入力キー。例: basic-auth-password)。</summary>
    public required string Kind { get; set; }

    /// <summary>Data Protectionで暗号化済みの値。</summary>
    public required string ValueProtected { get; set; }

    public DateTime UpdatedAt { get; set; }
}
