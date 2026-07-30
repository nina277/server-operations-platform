using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services;
using ServerOperations.Core.Services.Notifications;

namespace ServerOperations.Api.Tests.Fakes;

/// <summary>
/// メンテナンス期間の判定を差し替える。既定では何も抑止しない
/// (抑止を確かめたいテストだけが明示的に立てる)。
/// </summary>
public class FakeMaintenanceService : IMaintenanceService
{
    public MaintenanceState State { get; set; } = MaintenanceState.None;

    /// <summary>判定を求められた対象。抑止の判定が呼ばれたことを確かめるのに使う。</summary>
    public List<long> QueriedTargetIds { get; } = [];

    public Task<MaintenanceState> GetStateAsync(long targetId, CancellationToken ct = default)
    {
        QueriedTargetIds.Add(targetId);
        return Task.FromResult(State);
    }
}

public class FakeNotificationTestService : INotificationTestService
{
    public List<ChannelTestResult> Results { get; set; } = [];

    public int CallCount { get; private set; }

    public Task<List<ChannelTestResult>> SendTestAsync(CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(Results);
    }
}

public class FakeIncidentNoteRepository : IIncidentNoteRepository
{
    private long _nextId = 1;

    public List<IncidentNote> Notes { get; } = [];

    public Task<List<IncidentNote>> GetForIncidentAsync(long incidentId, CancellationToken ct = default) =>
        Task.FromResult(Notes
            .Where(n => n.IncidentId == incidentId)
            .OrderByDescending(n => n.CreatedAt)
            .ToList());

    public Task AddAsync(IncidentNote note, CancellationToken ct = default)
    {
        note.Id = _nextId++;
        Notes.Add(note);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeMaintenanceWindowRepository : IMaintenanceWindowRepository
{
    private long _nextId = 1;

    public List<MaintenanceWindow> Windows { get; } = [];

    public Task<MaintenanceWindow?> FindByIdAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Windows.FirstOrDefault(w => w.Id == id));

    public Task<List<MaintenanceWindow>> GetUpcomingAsync(DateTime asOf, CancellationToken ct = default) =>
        Task.FromResult(Windows
            .Where(w => w.CancelledAt == null && w.EndsAt >= asOf)
            .OrderBy(w => w.StartsAt)
            .ToList());

    public Task<List<MaintenanceWindow>> GetActiveForTargetAsync(
        long targetId, DateTime asOf, CancellationToken ct = default) =>
        Task.FromResult(Windows
            .Where(w => w.CancelledAt == null
                && w.StartsAt <= asOf
                && w.EndsAt > asOf
                && (w.TargetId == null || w.TargetId == targetId))
            .ToList());

    public Task AddAsync(MaintenanceWindow window, CancellationToken ct = default)
    {
        window.Id = _nextId++;
        Windows.Add(window);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
