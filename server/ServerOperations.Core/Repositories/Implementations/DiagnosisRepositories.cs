using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Repositories.Implementations;

public class DiagnosticRuleRepository(AppDbContext db) : IDiagnosticRuleRepository
{
    public Task<List<DiagnosticRule>> GetEnabledAsync(CancellationToken ct = default) =>
        db.DiagnosticRules.Where(r => r.IsEnabled).OrderBy(r => r.Priority).ThenBy(r => r.Id).ToListAsync(ct);

    public Task<List<DiagnosticRule>> GetAllAsync(CancellationToken ct = default) =>
        db.DiagnosticRules.OrderBy(r => r.Priority).ThenBy(r => r.Id).ToListAsync(ct);

    public Task<bool> AnyAsync(CancellationToken ct = default) => db.DiagnosticRules.AnyAsync(ct);

    public async Task AddRangeAsync(IEnumerable<DiagnosticRule> rules, CancellationToken ct = default) =>
        await db.DiagnosticRules.AddRangeAsync(rules, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public class DiagnosisRepository(AppDbContext db) : IDiagnosisRepository
{
    public async Task AddAsync(Diagnosis diagnosis, CancellationToken ct = default) =>
        await db.Diagnoses.AddAsync(diagnosis, ct);

    public Task<List<Diagnosis>> GetByIncidentAsync(long incidentId, CancellationToken ct = default) =>
        db.Diagnoses.Where(d => d.IncidentId == incidentId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

    public Task<Diagnosis?> FindLatestBySignatureAsync(
        string signature, long excludeIncidentId, CancellationToken ct = default) =>
        db.Diagnoses
            .Join(db.Incidents, d => d.IncidentId, i => i.Id, (d, i) => new { d, i })
            .Where(x => x.i.SignatureSha256 == signature && x.d.IncidentId != excludeIncidentId)
            .OrderByDescending(x => x.d.CreatedAt)
            .Select(x => x.d)
            .FirstOrDefaultAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
