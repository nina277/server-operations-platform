namespace ServerOperations.Api.Models.Operations;

/// <summary>監視対象。テンプレート(docker-host / docker-compose-app / web-site)に基づき登録される。</summary>
public class MonitoringTarget
{
    public long Id { get; set; }

    public required string Name { get; set; }

    /// <summary>アダプターテンプレートID(コード内カタログで定義)。</summary>
    public required string TemplateId { get; set; }

    public string? Description { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public long? CreatedByUserId { get; set; }

    public TargetProfile? Profile { get; set; }

    public ICollection<TargetCredential> Credentials { get; set; } = [];
}
