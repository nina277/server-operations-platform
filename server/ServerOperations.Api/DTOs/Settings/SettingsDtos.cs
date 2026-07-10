using System.ComponentModel.DataAnnotations;

namespace ServerOperations.Api.DTOs.Settings;

/// <summary>一般設定。</summary>
public record ProfileSettingsDto
{
    [Required]
    [MaxLength(100)]
    public required string SystemName { get; init; }

    /// <summary>UI既定言語 (ja / en)。</summary>
    [Required]
    [RegularExpression("^(ja|en)$", ErrorMessage = "languageは ja または en を指定してください。")]
    public required string Language { get; init; }
}

/// <summary>保持設定。プロファイルまたは個別日数で指定する。</summary>
public record RetentionSettingsDto
{
    /// <summary>compact / standard / long-term / custom。</summary>
    [Required]
    [RegularExpression("^(compact|standard|long-term|custom)$",
        ErrorMessage = "profileは compact / standard / long-term / custom のいずれかを指定してください。")]
    public required string Profile { get; init; }

    [Range(1, 3650)]
    public int MetricsDays { get; init; } = 30;

    [Range(1, 3650)]
    public int LogsDays { get; init; } = 30;

    [Range(1, 3650)]
    public int IncidentsDays { get; init; } = 365;

    [Range(1, 3650)]
    public int AuditDays { get; init; } = 365;
}

public record NetworkCidrDto
{
    public required long Id { get; init; }

    public required string Cidr { get; init; }

    public string? Description { get; init; }

    public required DateTime CreatedAt { get; init; }
}

public record CreateNetworkCidrRequest
{
    [Required]
    [MaxLength(64)]
    public required string Cidr { get; init; }

    [MaxLength(200)]
    public string? Description { get; init; }
}

/// <summary>秘密値の状態。値そのものは決して返さない。</summary>
public record SecretStatusDto
{
    public required string Kind { get; init; }

    public required bool IsConfigured { get; init; }

    public DateTime? UpdatedAt { get; init; }
}

public record UpdateSecretRequest
{
    [Required]
    [MaxLength(8000)]
    public required string Value { get; init; }
}
