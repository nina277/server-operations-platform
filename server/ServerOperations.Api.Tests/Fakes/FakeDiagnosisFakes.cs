using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests.Fakes;

public class FakeDiagnosticRuleRepository : IDiagnosticRuleRepository
{
    public List<DiagnosticRule> Rules { get; } = [];

    public Task<List<DiagnosticRule>> GetEnabledAsync(CancellationToken ct = default) =>
        Task.FromResult(Rules.Where(r => r.IsEnabled).OrderBy(r => r.Priority).ThenBy(r => r.Id).ToList());

    public Task<List<DiagnosticRule>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult(Rules.OrderBy(r => r.Priority).ThenBy(r => r.Id).ToList());

    public Task<DiagnosticRule?> FindByIdAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Rules.FirstOrDefault(r => r.Id == id));

    public Task<bool> ExistsByNameAsync(
        string name, long? excludeId = null, CancellationToken ct = default) =>
        Task.FromResult(Rules.Any(r => r.Name == name && (excludeId is null || r.Id != excludeId)));

    public Task<bool> AnyAsync(CancellationToken ct = default) => Task.FromResult(Rules.Count > 0);

    public Task AddAsync(DiagnosticRule rule, CancellationToken ct = default)
    {
        rule.Id = Rules.Count + 1;
        Rules.Add(rule);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IEnumerable<DiagnosticRule> rules, CancellationToken ct = default)
    {
        foreach (var rule in rules)
        {
            rule.Id = Rules.Count + 1;
            Rules.Add(rule);
        }

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeDiagnosisRepository(FakeIncidentRepository? incidents = null) : IDiagnosisRepository
{
    public List<Diagnosis> Diagnoses { get; } = [];

    public Task AddAsync(Diagnosis diagnosis, CancellationToken ct = default)
    {
        diagnosis.Id = Diagnoses.Count + 1;
        Diagnoses.Add(diagnosis);
        return Task.CompletedTask;
    }

    public Task<List<Diagnosis>> GetByIncidentAsync(long incidentId, CancellationToken ct = default) =>
        Task.FromResult(Diagnoses
            .Where(d => d.IncidentId == incidentId)
            .OrderByDescending(d => d.CreatedAt)
            .ToList());

    public Task<Diagnosis?> FindLatestBySignatureAsync(
        string signature, long excludeIncidentId, CancellationToken ct = default)
    {
        // EF実装と同じく、インシデントの署名で結合して検索する
        var matchingIncidentIds = (incidents?.Incidents ?? [])
            .Where(i => i.SignatureSha256 == signature)
            .Select(i => i.Id)
            .ToHashSet();

        var found = Diagnoses
            .Where(d => matchingIncidentIds.Contains(d.IncidentId) && d.IncidentId != excludeIncidentId)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefault();

        return Task.FromResult(found);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeDiagnosisService : IDiagnosisService
{
    public List<(Incident Incident, DiagnosticContext Context)> Calls { get; } = [];

    /// <summary>診断結果。nullは「ルール未一致かつ履歴なし」を表す。</summary>
    public Diagnosis? Result { get; set; }

    public Task<Diagnosis?> DiagnoseAsync(
        Incident incident, DiagnosticContext context, CancellationToken ct = default)
    {
        Calls.Add((incident, context));
        return Task.FromResult(Result);
    }
}

public class FakeNotificationService : ServerOperations.Core.Services.Notifications.INotificationService
{
    public List<ServerOperations.Core.Services.Notifications.NotificationRequest> Requests { get; } = [];

    public Task<Notification?> NotifyAsync(
        ServerOperations.Core.Services.Notifications.NotificationRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        return Task.FromResult<Notification?>(null);
    }
}

public class FakeAutoRecoveryService : ServerOperations.Core.Services.IAutoRecoveryService
{
    public List<(MonitoringTarget Target, Incident Incident, Diagnosis Diagnosis)> Calls { get; } = [];

    public Task<RecoveryAction?> TryRecoverAsync(
        MonitoringTarget target, Incident incident, Diagnosis diagnosis, CancellationToken ct = default)
    {
        Calls.Add((target, incident, diagnosis));
        return Task.FromResult<RecoveryAction?>(null);
    }
}
