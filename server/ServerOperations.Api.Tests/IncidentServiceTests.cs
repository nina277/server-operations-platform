using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Services.Implementations;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Api.Tests.Fakes;

namespace ServerOperations.Api.Tests;

public class IncidentServiceTests
{
    private readonly FakeIncidentRepository _incidents = new();
    private readonly FakeAuditService _audit = new();
    private readonly FakeCurrentUserAccessor _currentUser = new();
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));

    private IncidentService CreateSut() => new(_incidents, _audit, _currentUser, _time);

    private Incident AddIncident(IncidentStatus status = IncidentStatus.Open)
    {
        var incident = new Incident
        {
            Id = 1,
            TargetId = 1,
            SignatureSha256 = "sig",
            Title = "コンテナ web が停止しています",
            Classification = "ContainerStopped",
            Status = status,
            Severity = IncidentSeverity.High,
            FirstOccurredAt = _time.Now.UtcDateTime,
            LastOccurredAt = _time.Now.UtcDateTime,
        };
        _incidents.Incidents.Add(incident);
        return incident;
    }

    [Theory]
    [InlineData(IncidentStatus.Open, "Acknowledged")]
    [InlineData(IncidentStatus.Acknowledged, "Resolved")]
    [InlineData(IncidentStatus.Resolved, "Open")]
    [InlineData(IncidentStatus.Resolved, "Closed")]
    public async Task UpdateStatus_AllowedTransition_Succeeds(IncidentStatus from, string to)
    {
        AddIncident(from);

        var dto = await CreateSut().UpdateStatusAsync(1, to);

        Assert.Equal(to, dto.Status);
        Assert.Contains(_audit.Entries, e => e.Action == "incident.status_change");
    }

    [Theory]
    [InlineData(IncidentStatus.Closed, "Open")]
    [InlineData(IncidentStatus.Closed, "Resolved")]
    [InlineData(IncidentStatus.Recovering, "Acknowledged")]
    public async Task UpdateStatus_DisallowedTransition_Rejects(IncidentStatus from, string to)
    {
        AddIncident(from);

        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().UpdateStatusAsync(1, to));

        Assert.Equal("invalid_status_transition", ex.Code);
    }

    [Fact]
    public async Task UpdateStatus_UnknownStatus_Rejects()
    {
        AddIncident();

        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().UpdateStatusAsync(1, "Weird"));

        Assert.Equal("invalid_status", ex.Code);
    }

    [Fact]
    public async Task UpdateStatus_ToResolved_SetsResolvedAt()
    {
        AddIncident();

        var dto = await CreateSut().UpdateStatusAsync(1, "Resolved");

        Assert.NotNull(dto.ResolvedAt);
    }

    [Fact]
    public async Task Search_FiltersByStatus()
    {
        AddIncident();
        _incidents.Incidents.Add(new Incident
        {
            Id = 2,
            TargetId = 1,
            SignatureSha256 = "sig2",
            Title = "HTTPヘルスチェックが失敗しています",
            Classification = "HttpUnavailable",
            Status = IncidentStatus.Closed,
            FirstOccurredAt = _time.Now.UtcDateTime,
            LastOccurredAt = _time.Now.UtcDateTime,
        });

        var result = await CreateSut().SearchAsync(new IncidentListQuery { Status = "Open" });

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Open", Assert.Single(result.Items).Status);
    }
}
