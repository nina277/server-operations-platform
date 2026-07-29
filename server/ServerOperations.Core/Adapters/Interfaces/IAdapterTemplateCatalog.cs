namespace ServerOperations.Core.Adapters.Interfaces;

/// <summary>テンプレート入力の型。</summary>
public enum TemplateInputType
{
    String,
    Url,
    Int,
    Bool,
}

public record TemplateInput(
    string Key,
    string Label,
    TemplateInputType Type,
    bool Required,
    bool Secret,
    string Description,
    string? DefaultValue = null);

/// <summary>
/// アダプターテンプレート。必須入力・入力型・説明・推奨監視・初期ルール・初期許可操作を持つ。
/// テンプレートはコードで版管理する(DBでは編集させない)。
/// </summary>
public record AdapterTemplate(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<TemplateInput> Inputs,
    IReadOnlyList<string> RecommendedMonitors,
    IReadOnlyList<string> InitialRules,
    IReadOnlyList<string> AllowedOperations,
    IReadOnlyList<string> Capabilities);

public interface IAdapterTemplateCatalog
{
    IReadOnlyList<AdapterTemplate> GetAll();

    AdapterTemplate? Find(string templateId);
}
