using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Settings;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

public class AuditLogQueryService(
    IAuditLogRepository auditLogs,
    IAuditService audit) : IAuditLogQueryService
{
    /// <summary>1回のCSV出力で持ち出せる上限。無制限にすると全件を一度に抜ける。</summary>
    public const int MaxExportRows = 10000;

    public async Task<string> ExportCsvAsync(AuditLogFilter filter, CancellationToken ct = default)
    {
        var (items, total) = await auditLogs.SearchAsync(filter, skip: 0, take: MaxExportRows, ct);

        // 監査ログの持ち出しそのものを監査に残す。誰がいつ何件抜いたかを追えるようにする。
        await audit.RecordAsync(
            "audit.export", "AuditLog", null, AuditResult.Success,
            details: $"exported={items.Count} matched={total} limit={MaxExportRows}", ct: ct);

        return AuditLogCsvWriter.Write(items.Select(AuditLogDto.From));
    }

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
