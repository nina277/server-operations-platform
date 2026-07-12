using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

public class IncidentService(
    IIncidentRepository incidents,
    IAuditService audit,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : IIncidentService
{
    /// <summary>許可される状態遷移。Closedは終端。</summary>
    private static readonly Dictionary<IncidentStatus, IncidentStatus[]> AllowedTransitions = new()
    {
        [IncidentStatus.Open] = [IncidentStatus.Acknowledged, IncidentStatus.Recovering, IncidentStatus.Resolved, IncidentStatus.Closed],
        [IncidentStatus.Acknowledged] = [IncidentStatus.Recovering, IncidentStatus.Resolved, IncidentStatus.Closed],
        [IncidentStatus.Recovering] = [IncidentStatus.Resolved, IncidentStatus.Closed],
        [IncidentStatus.Resolved] = [IncidentStatus.Open, IncidentStatus.Closed],
        [IncidentStatus.Closed] = [],
    };

    public async Task<PagedResult<IncidentDto>> SearchAsync(
        IncidentListQuery query, CancellationToken ct = default)
    {
        var criteria = new IncidentSearchCriteria
        {
            Status = query.Status,
            Severity = query.Severity,
            TargetId = query.TargetId,
            Search = query.Search,
            Sort = query.Sort,
            Page = query.Page,
            PageSize = query.PageSize,
        };

        var (items, total) = await incidents.SearchAsync(criteria, ct);
        return new PagedResult<IncidentDto>(
            items.Select(IncidentDto.From).ToList(),
            Math.Max(query.Page, 1),
            Math.Clamp(query.PageSize, 1, 100),
            total);
    }

    public async Task<IncidentDto> GetAsync(long id, CancellationToken ct = default) =>
        IncidentDto.From(await FindOrThrowAsync(id, ct));

    public async Task<IncidentDto> UpdateStatusAsync(long id, string newStatus, CancellationToken ct = default)
    {
        if (!Enum.TryParse<IncidentStatus>(newStatus, ignoreCase: true, out var parsed))
        {
            throw AppException.BadRequest("invalid_status", "不明なインシデント状態です。");
        }

        var incident = await FindOrThrowAsync(id, ct);

        if (incident.Status == parsed)
        {
            return IncidentDto.From(incident);
        }

        if (!AllowedTransitions[incident.Status].Contains(parsed))
        {
            throw AppException.BadRequest(
                "invalid_status_transition",
                $"{incident.Status} から {parsed} への遷移は許可されていません。");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var before = incident.Status;
        incident.Status = parsed;
        incident.UpdatedAt = now;
        incident.ResolvedAt = parsed switch
        {
            IncidentStatus.Resolved or IncidentStatus.Closed => incident.ResolvedAt ?? now,
            _ => null,
        };

        await incidents.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "incident.status_change", "Incident", incident.Id.ToString(), AuditResult.Success,
            actorUserId: currentUser.UserId, actorName: currentUser.Username,
            details: $"before={before} after={parsed}", ct: ct);

        return IncidentDto.From(incident);
    }

    private async Task<Incident> FindOrThrowAsync(long id, CancellationToken ct) =>
        await incidents.FindByIdAsync(id, ct)
            ?? throw AppException.NotFound("incident_not_found", "インシデントが見つかりません。");
}
