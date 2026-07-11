using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

public class DashboardService(
    IIncidentRepository incidents,
    IMonitoringTargetRepository targets) : IDashboardService
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

        return new DashboardSummaryDto
        {
            TargetCount = allTargets.Count,
            EnabledTargetCount = allTargets.Count(t => t.IsEnabled),
            ActiveIncidentsBySeverity = bySeverity.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            IncidentsByStatus = byStatus.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            RecentIncidents = recent.Select(IncidentDto.From).ToList(),
        };
    }
}
