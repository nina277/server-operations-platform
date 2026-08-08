using System.ComponentModel.DataAnnotations;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services.Deployment;

namespace ServerOperations.Api.DTOs.Operations;

public record ServiceTemplateInputDto
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public required string Type { get; init; }

    public string? DefaultValue { get; init; }

    public bool Required { get; init; }

    public string? Description { get; init; }

    public int? ContainerPort { get; init; }

    public string? ContainerPath { get; init; }
}

public record ServiceTemplateDto
{
    public required long Id { get; init; }

    public required string Key { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required string Image { get; init; }

    public required int MemoryLimitMb { get; init; }

    public required bool IsBuiltIn { get; init; }

    public required List<ServiceTemplateInputDto> Inputs { get; init; }

    public static ServiceTemplateDto From(ServiceTemplate t) => new()
    {
        Id = t.Id,
        Key = t.Key,
        Name = t.Name,
        Description = t.Description,
        Image = t.Image,
        MemoryLimitMb = t.MemoryLimitMb,
        IsBuiltIn = t.IsBuiltIn,
        Inputs = t.Inputs.OrderBy(i => i.SortOrder).Select(i => new ServiceTemplateInputDto
        {
            Key = i.Key,
            Label = i.Label,
            Type = i.Type.ToString(),
            // 秘密値の既定値は返さない
            DefaultValue = i.Type == ServiceInputType.Secret ? null : i.DefaultValue,
            Required = i.Required,
            Description = i.Description,
            ContainerPort = i.ContainerPort,
            ContainerPath = i.ContainerPath,
        }).ToList(),
    };
}

public record DeployServiceRequest
{
    public required long TargetId { get; init; }

    public required long TemplateId { get; init; }

    [Required]
    [MaxLength(63)]
    public required string Name { get; init; }

    /// <summary>入力欄の値。秘密値はここで受け取るが、保存も監査もしない。</summary>
    public Dictionary<string, string> Inputs { get; init; } = [];

    /// <summary>取り違え防止の確認。展開時は Name と同じ値を求める。</summary>
    [MaxLength(63)]
    public string? Confirm { get; init; }
}

public record DeploymentPlanDto
{
    public required string ServiceName { get; init; }

    public required string ContainerName { get; init; }

    public required string Image { get; init; }

    public required Dictionary<int, int> Ports { get; init; }

    public required List<string> Volumes { get; init; }

    /// <summary>設定される環境変数の項目名だけ。**値は含めない。**</summary>
    public required List<string> EnvironmentKeys { get; init; }

    public required List<string> Warnings { get; init; }

    public required bool Applied { get; init; }

    public static DeploymentPlanDto From(DeploymentPlan p) => new()
    {
        ServiceName = p.ServiceName,
        ContainerName = p.ContainerName,
        Image = p.Image,
        Ports = p.Ports,
        Volumes = p.Volumes,
        EnvironmentKeys = p.EnvironmentKeys,
        Warnings = p.Warnings,
        Applied = p.Applied,
    };
}

public record DeployedServiceDto
{
    public required long Id { get; init; }

    public required string Name { get; init; }

    public required long TargetId { get; init; }

    public required string ContainerName { get; init; }

    public required string Image { get; init; }

    public required string Status { get; init; }

    public string? Message { get; init; }

    public required DateTime DeployedAt { get; init; }

    public static DeployedServiceDto From(DeployedService s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        TargetId = s.TargetId,
        ContainerName = s.ContainerName,
        Image = s.Image,
        Status = s.Status.ToString(),
        Message = s.Message,
        DeployedAt = s.DeployedAt,
    };
}
