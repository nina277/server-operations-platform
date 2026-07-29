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
}
