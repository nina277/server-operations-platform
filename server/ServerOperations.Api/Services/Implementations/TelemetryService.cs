using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

public class TelemetryService(
    IMonitoringTargetRepository targets,
    IMetricSnapshotRepository snapshots,
    IIncidentLogRepository logs) : ITelemetryService
{
    public async Task<List<MetricSnapshotDto>> GetMetricsAsync(
        long targetId, int limit, CancellationToken ct = default)
    {
        await EnsureTargetExistsAsync(targetId, ct);
        var items = await snapshots.GetRecentAsync(targetId, Math.Clamp(limit, 1, 500), ct);
        return items.Select(m => new MetricSnapshotDto
        {
            Id = m.Id,
            CollectedAt = m.CollectedAt,
            Kind = m.Kind,
            Status = m.Status.ToString(),
            PayloadJson = m.PayloadJson,
            ErrorMessage = m.ErrorMessage,
        }).ToList();
    }

    public async Task<List<IncidentLogDto>> GetLogsAsync(
        long targetId, int limit, CancellationToken ct = default)
    {
        await EnsureTargetExistsAsync(targetId, ct);
        var items = await logs.GetRecentAsync(targetId, Math.Clamp(limit, 1, 200), ct);
        return items.Select(l => new IncidentLogDto
        {
            Id = l.Id,
            CollectedAt = l.CollectedAt,
            Source = l.Source,
            MaskedContent = l.MaskedContent,
            IncidentId = l.IncidentId,
        }).ToList();
    }

    private async Task EnsureTargetExistsAsync(long targetId, CancellationToken ct)
    {
        _ = await targets.FindByIdAsync(targetId, ct)
            ?? throw AppException.NotFound("target_not_found", "監視対象が見つかりません。");
    }
}
