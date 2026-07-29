using Microsoft.Extensions.Logging.Abstractions;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Adapters.Implementations;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

public class DiagnosisServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeDiagnosticRuleRepository _rules = new();
    private readonly FakeIncidentRepository _incidents = new();
    private readonly FakeDiagnosisRepository _diagnoses;
    private readonly FakeMonitoringTargetRepository _targets = new();
    private readonly TestTimeProvider _time = new(BaseTime);

    public DiagnosisServiceTests()
    {
        _diagnoses = new FakeDiagnosisRepository(_incidents);
        _rules.Rules.AddRange(DefaultDiagnosticRules.Create(BaseTime.UtcDateTime));
    }

    private DiagnosisService CreateSut() => new(
        _rules, _diagnoses, _targets, new AdapterTemplateCatalog(), new RuleEngine(), _time,
        NullLogger<DiagnosisService>.Instance);

    private void AddTarget(long id = 1, string templateId = "docker-host")
    {
        _targets.Targets.Add(new MonitoringTarget
        {
            Id = id,
            Name = $"t{id}",
            TemplateId = templateId,
            IsEnabled = true,
        });
    }

    private Incident AddIncident(long id = 1, string signature = "sig-a", string classification = "ContainerStopped")
    {
        var incident = new Incident
        {
            Id = id,
            TargetId = 1,
            SignatureSha256 = signature,
            Title = "テスト障害",
            Classification = classification,
            Service = "web",
            Severity = IncidentSeverity.High,
            Status = IncidentStatus.Open,
            FirstOccurredAt = BaseTime.UtcDateTime,
            LastOccurredAt = BaseTime.UtcDateTime,
        };
        _incidents.Incidents.Add(incident);
        return incident;
    }

    [Fact]
    public async Task Diagnose_RuleMatch_SavesRuleSourcedDiagnosis()
    {
        AddTarget();
        var incident = AddIncident();

        var diagnosis = await CreateSut().DiagnoseAsync(incident, new DiagnosticContext
        {
            ContainerState = "exited",
            ContainerName = "web",
        });

        Assert.NotNull(diagnosis);
        Assert.Equal(DiagnosisSource.Rule, diagnosis.Source);
        Assert.Equal("ContainerStopped", diagnosis.Classification);
        Assert.NotNull(diagnosis.RuleId);
        Assert.Contains("exited", diagnosis.Rationale);
        // docker-hostテンプレートはRESTART_ALLOWED_CONTAINERを許可している
        Assert.True(diagnosis.RecommendedActionAllowed);
    }

    [Fact]
    public async Task Diagnose_RecommendedActionNotInTemplate_IsMarkedNotAllowed()
    {
        // web-siteテンプレートはRESTART_ALLOWED_CONTAINERを許可していない
        AddTarget(templateId: "web-site");
        var incident = AddIncident();

        var diagnosis = await CreateSut().DiagnoseAsync(incident, new DiagnosticContext
        {
            ContainerState = "exited",
        });

        Assert.NotNull(diagnosis);
        Assert.Equal("RESTART_ALLOWED_CONTAINER", diagnosis.RecommendedActionId);
        Assert.False(diagnosis.RecommendedActionAllowed);
    }

    [Fact]
    public async Task Diagnose_NoRuleMatch_ReusesHistoryWithSameSignature()
    {
        AddTarget();
        var pastIncident = AddIncident(id: 1, signature: "sig-shared");
        _diagnoses.Diagnoses.Add(new Diagnosis
        {
            Id = 1,
            IncidentId = pastIncident.Id,
            TargetId = 1,
            Source = DiagnosisSource.Rule,
            Classification = "UnknownLog",
            Severity = IncidentSeverity.Medium,
            Rationale = "過去の判断根拠",
            RecommendedActionId = "RESTART_ALLOWED_CONTAINER",
            RecommendedActionAllowed = true,
            CreatedAt = BaseTime.UtcDateTime,
        });

        var newIncident = AddIncident(id: 2, signature: "sig-shared");
        _time.Now = BaseTime.AddHours(1);

        // ルールに一致しないコンテキスト(全項目null)
        var diagnosis = await CreateSut().DiagnoseAsync(newIncident, new DiagnosticContext());

        Assert.NotNull(diagnosis);
        Assert.Equal(DiagnosisSource.History, diagnosis.Source);
        Assert.Equal(1, diagnosis.ReusedDiagnosisId);
        Assert.Equal("UnknownLog", diagnosis.Classification);
        Assert.Contains("再利用", diagnosis.Rationale);
    }

    [Fact]
    public async Task Diagnose_HistoryReuse_RevalidatesActionAgainstCurrentTarget()
    {
        // 過去はDocker Hostで再起動が許可されていたが、現在の対象はweb-site
        AddTarget(templateId: "web-site");
        var pastIncident = AddIncident(id: 1, signature: "sig-shared");
        _diagnoses.Diagnoses.Add(new Diagnosis
        {
            Id = 1,
            IncidentId = pastIncident.Id,
            TargetId = 1,
            Source = DiagnosisSource.Rule,
            Classification = "ContainerStopped",
            Severity = IncidentSeverity.High,
            Rationale = "過去の判断根拠",
            RecommendedActionId = "RESTART_ALLOWED_CONTAINER",
            RecommendedActionAllowed = true,
            CreatedAt = BaseTime.UtcDateTime,
        });

        var newIncident = AddIncident(id: 2, signature: "sig-shared");
        var diagnosis = await CreateSut().DiagnoseAsync(newIncident, new DiagnosticContext());

        Assert.NotNull(diagnosis);
        Assert.Equal(DiagnosisSource.History, diagnosis.Source);
        // 過去はtrueでも、現在の対象能力で再検証されfalseになる
        Assert.False(diagnosis.RecommendedActionAllowed);
    }

    [Fact]
    public async Task Diagnose_NoRuleAndNoHistory_ReturnsNull()
    {
        AddTarget();
        var incident = AddIncident(signature: "sig-unique");

        var diagnosis = await CreateSut().DiagnoseAsync(incident, new DiagnosticContext());

        Assert.Null(diagnosis);
        Assert.Empty(_diagnoses.Diagnoses);
    }

    [Fact]
    public async Task Diagnose_DoesNotReuseOwnDiagnosis()
    {
        AddTarget();
        var incident = AddIncident(signature: "sig-self");
        _diagnoses.Diagnoses.Add(new Diagnosis
        {
            Id = 1,
            IncidentId = incident.Id,
            TargetId = 1,
            Source = DiagnosisSource.Rule,
            Classification = "ContainerStopped",
            Severity = IncidentSeverity.High,
            Rationale = "自分自身の過去診断",
            CreatedAt = BaseTime.UtcDateTime,
        });

        var diagnosis = await CreateSut().DiagnoseAsync(incident, new DiagnosticContext());

        Assert.Null(diagnosis);
    }

    [Fact]
    public async Task Diagnose_PrefersRuleOverHistory()
    {
        AddTarget();
        var pastIncident = AddIncident(id: 1, signature: "sig-shared");
        _diagnoses.Diagnoses.Add(new Diagnosis
        {
            Id = 1,
            IncidentId = pastIncident.Id,
            TargetId = 1,
            Source = DiagnosisSource.Rule,
            Classification = "HistoryClassification",
            Severity = IncidentSeverity.Low,
            Rationale = "過去",
            CreatedAt = BaseTime.UtcDateTime,
        });
        var newIncident = AddIncident(id: 2, signature: "sig-shared");

        var diagnosis = await CreateSut().DiagnoseAsync(newIncident, new DiagnosticContext
        {
            ContainerState = "exited",
        });

        Assert.NotNull(diagnosis);
        Assert.Equal(DiagnosisSource.Rule, diagnosis.Source);
        Assert.Equal("ContainerStopped", diagnosis.Classification);
    }
}
