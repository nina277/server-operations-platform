using System.ComponentModel.DataAnnotations;

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
