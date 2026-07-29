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

    public Task<bool> AnyAsync(CancellationToken ct = default) => Task.FromResult(Rules.Count > 0);

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

    public Task<Diagnosis?> DiagnoseAsync(
        Incident incident, DiagnosticContext context, CancellationToken ct = default)
    {
        Calls.Add((incident, context));
        return Task.FromResult<Diagnosis?>(null);
    }
}
