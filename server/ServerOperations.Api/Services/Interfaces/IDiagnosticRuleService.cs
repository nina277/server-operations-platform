using ServerOperations.Api.DTOs.Operations;

namespace ServerOperations.Api.Services.Interfaces;

/// <summary>診断ルールの参照と編集。ルールは自動復旧の入口のため、保存前に条件を検証する。</summary>
public interface IDiagnosticRuleService
{
    Task<List<DiagnosticRuleDto>> GetAllAsync(CancellationToken ct = default);

    Task<DiagnosticRuleDto> GetAsync(long id, CancellationToken ct = default);

    Task<DiagnosticRuleDto> CreateAsync(SaveDiagnosticRuleRequest request, CancellationToken ct = default);

    Task<DiagnosticRuleDto> UpdateAsync(
        long id, SaveDiagnosticRuleRequest request, CancellationToken ct = default);

    Task<DiagnosticRuleDto> SetEnabledAsync(long id, bool isEnabled, CancellationToken ct = default);

    /// <summary>
    /// 判定を試す。保存も実行もしない。
    /// 編集中のルールを渡すと、それも含めて評価する。
    /// </summary>
    Task<RuleTestResponse> TestAsync(RuleTestRequest request, CancellationToken ct = default);

    RuleEditorOptionsDto GetEditorOptions();
}
