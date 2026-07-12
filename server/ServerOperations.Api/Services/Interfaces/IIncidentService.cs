using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;

namespace ServerOperations.Api.Services.Interfaces;

public interface IIncidentService
{
    Task<PagedResult<IncidentDto>> SearchAsync(IncidentListQuery query, CancellationToken ct = default);

    Task<IncidentDto> GetAsync(long id, CancellationToken ct = default);

    Task<IncidentDto> UpdateStatusAsync(long id, string newStatus, CancellationToken ct = default);
}

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default);
}

public interface ITelemetryService
{
    Task<List<MetricSnapshotDto>> GetMetricsAsync(long targetId, int limit, CancellationToken ct = default);

    Task<List<IncidentLogDto>> GetLogsAsync(long targetId, int limit, CancellationToken ct = default);
}
