using System.ComponentModel.DataAnnotations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.DTOs.Operations;

public record AdapterTemplateInputDto(
    string Key,
    string Label,
    string Type,
    bool Required,
    bool Secret,
    string Description,
    string? DefaultValue);

public record AdapterTemplateDto(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<AdapterTemplateInputDto> Inputs,
    IReadOnlyList<string> RecommendedMonitors,
    IReadOnlyList<string> InitialRules,
    IReadOnlyList<string> AllowedOperations,
    IReadOnlyList<string> Capabilities);

public record TargetDto
{
    public required long Id { get; init; }

    public required string Name { get; init; }

    public required string TemplateId { get; init; }

    public string? Description { get; init; }

    public required bool IsEnabled { get; init; }

    /// <summary>自動復旧の有効/無効(初期値OFF)。</summary>
    public required bool AutoRecoveryEnabled { get; init; }

    /// <summary>操作を許可するコンテナ名。空の場合はどのコンテナも操作できない。</summary>
    public required IReadOnlyList<string> AllowedContainers { get; init; }

    /// <summary>
    /// この対象の収集間隔(秒)。nullなら全体の既定値で動く。
    /// </summary>
    public int? CollectionIntervalSeconds { get; init; }

    /// <summary>非秘密の設定値。秘密値(資格情報)は種別名のみconfiguredCredentialsで返す。</summary>
    public required IReadOnlyDictionary<string, string> Settings { get; init; }

    public required IReadOnlyList<string> ConfiguredCredentials { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime UpdatedAt { get; init; }
}

public record CreateTargetRequest
{
    [Required]
    [MaxLength(100)]
    public required string Name { get; init; }

    [Required]
    [MaxLength(64)]
    public required string TemplateId { get; init; }

    [MaxLength(500)]
    public string? Description { get; init; }

    /// <summary>テンプレート入力(非秘密)。key → value。</summary>
    public Dictionary<string, string> Settings { get; init; } = [];

    /// <summary>テンプレートのsecret入力。key → value。保存時に暗号化される。</summary>
    public Dictionary<string, string> Credentials { get; init; } = [];
}

public record UpdateTargetRequest
{
    [Required]
    [MaxLength(100)]
    public required string Name { get; init; }

    [MaxLength(500)]
    public string? Description { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// 自動復旧の有効/無効。有効にしてもLow操作(コンテナ再起動)のみが自動実行の対象となる。
    /// </summary>
    public bool AutoRecoveryEnabled { get; init; }

    /// <summary>
    /// 操作を許可するコンテナ名。空にするとどのコンテナも操作できなくなる。
    /// </summary>
    public List<string> AllowedContainers { get; init; } = [];

    /// <summary>
    /// この対象の収集間隔(秒)。nullなら全体の既定値を使う。
    /// 実際に使える値へ丸められるため、指定した値がそのまま返るとは限らない。
    /// </summary>
    [Range(CollectionInterval.MinSeconds, CollectionInterval.MaxSeconds,
        ErrorMessage = "収集間隔は60〜3600秒の範囲で指定してください。")]
    public int? CollectionIntervalSeconds { get; init; }

    public Dictionary<string, string> Settings { get; init; } = [];

    /// <summary>更新する資格情報のみ指定する(省略したものは維持)。</summary>
    public Dictionary<string, string> Credentials { get; init; } = [];
}

public record ConnectionTestResultDto
{
    public required bool Success { get; init; }

    public required string Message { get; init; }

    public long? LatencyMs { get; init; }

    public string? Detail { get; init; }
}

public record TargetCapabilitiesDto
{
    public required long TargetId { get; init; }

    public required string TemplateId { get; init; }

    public required IReadOnlyList<string> Capabilities { get; init; }

    public required IReadOnlyList<string> AllowedOperations { get; init; }

    public required IReadOnlyList<string> RecommendedMonitors { get; init; }

    public required IReadOnlyList<string> InitialRules { get; init; }
}

/// <summary>
/// 監視対象を削除したときに一緒に消えるものの件数。
/// 削除は元に戻せないため、何が消えるかを先に示す。
/// </summary>
public record TargetDeletePreviewDto
{
    public required long TargetId { get; init; }

    public required string TargetName { get; init; }

    public required int MetricSnapshots { get; init; }

    public required int Incidents { get; init; }

    public required int IncidentLogs { get; init; }

    public required int Diagnoses { get; init; }

    public required int RecoveryActions { get; init; }

    public required int HealthChecks { get; init; }

    public required int Notifications { get; init; }

    public required int MaintenanceWindows { get; init; }

    public required int Total { get; init; }
}
