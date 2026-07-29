using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Settings;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

public class AuditLogQueryService(IAuditLogRepository auditLogs) : IAuditLogQueryService
{
    public async Task<PagedResult<AuditLogDto>> SearchAsync(
        AuditLogFilter filter, PagingQuery paging, CancellationToken ct = default)
    {
        var (items, total) = await auditLogs.SearchAsync(
            filter, paging.Skip, paging.NormalizedPageSize, ct);

        return new PagedResult<AuditLogDto>(
            items.Select(AuditLogDto.From).ToList(),
            paging.NormalizedPage,
            paging.NormalizedPageSize,
            total);
    }

    public async Task<AuditLogFilterOptionsDto> GetFilterOptionsAsync(CancellationToken ct = default)
    {
        var (targetTypes, actions) = await auditLogs.GetFilterOptionsAsync(ct);

        return new AuditLogFilterOptionsDto
        {
            TargetTypes = targetTypes,
            Actions = actions,
            Results = Enum.GetNames<AuditResult>(),
        };
    }
}
