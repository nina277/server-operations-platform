using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

public class DashboardService(
    IIncidentRepository incidents,
    IMonitoringTargetRepository targets,
    ServerOperations.Core.Services.IMonitoringHealthService monitoringHealth) : IDashboardService
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var allTargets = await targets.GetAllAsync(ct);
        var byStatus = await incidents.CountByStatusAsync(ct);
        var bySeverity = await incidents.CountActiveBySeverityAsync(ct);
        var (recent, _) = await incidents.SearchAsync(new IncidentSearchCriteria
        {
            Sort = "-lastOccurredAt",
            Page = 1,
            PageSize = 5,
        }, ct);

        // 収集が止まっている対象。件数の集計だけでは、監視が死んでいても
        // 「インシデント0件」として正常に見えてしまう。
        var health = await monitoringHealth.GetAsync(ct);

        return new DashboardSummaryDto
        {
            TargetCount = allTargets.Count,
            EnabledTargetCount = allTargets.Count(t => t.IsEnabled),
            ActiveIncidentsBySeverity = bySeverity.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            IncidentsByStatus = byStatus.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            RecentIncidents = recent.Select(IncidentDto.From).ToList(),
            UnreachedTargets = health
                .Where(h => !h.IsHealthy)
                .OrderByDescending(h => h.StaleForSeconds ?? long.MaxValue)
                .Select(h => new TargetMonitoringHealthDto
                {
                    TargetId = h.TargetId,
                    TargetName = h.TargetName,
                    Reach = h.Reach.ToString(),
                    LastCollectedAt = h.LastCollectedAt,
                    ExpectedIntervalSeconds = h.ExpectedIntervalSeconds,
                    StaleForSeconds = h.StaleForSeconds,
                })
                .ToList(),
        };
    }
}
