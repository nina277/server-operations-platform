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
    /// <summary>
    /// この種類の対象で見ると良い項目。画面での案内に使う説明であり、
    /// 選択の対象ではない。実際に選べるものは CollectableMonitors。
    /// </summary>
    IReadOnlyList<string> RecommendedMonitors,
    IReadOnlyList<string> InitialRules,
    IReadOnlyList<string> AllowedOperations,
    IReadOnlyList<string> Capabilities,
    /// <summary>
    /// 対象ごとに入切できる収集の単位。
    ///
    /// 「収集として独立して行う仕事」だけを並べる。
    /// 例えば再起動回数はコンテナ一覧に付いてくる値で、単独では止められない。
    /// 止められないものを選択肢に出すと、外しても何も変わらない設定になる。
    /// </summary>
    IReadOnlyList<string> CollectableMonitors);

public interface IAdapterTemplateCatalog
{
    IReadOnlyList<AdapterTemplate> GetAll();

    AdapterTemplate? Find(string templateId);
}
