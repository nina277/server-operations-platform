using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Settings;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Services.Interfaces;

/// <summary>監査ログの参照。記録側(IAuditService)とは分けている。</summary>
public interface IAuditLogQueryService
{
    Task<PagedResult<AuditLogDto>> SearchAsync(
        AuditLogFilter filter, PagingQuery paging, CancellationToken ct = default);

    Task<AuditLogFilterOptionsDto> GetFilterOptionsAsync(CancellationToken ct = default);

    /// <summary>
    /// 検索条件に沿った監査ログをCSVで返す。件数には上限があり、
    /// 超える場合は上限までを返す(全件を1回で吐き出させない)。
    /// </summary>
    Task<string> ExportCsvAsync(AuditLogFilter filter, CancellationToken ct = default);
}
