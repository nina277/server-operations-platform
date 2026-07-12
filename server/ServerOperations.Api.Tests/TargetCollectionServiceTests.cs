using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;
using ServerOperations.Api.Tests.Fakes;

namespace ServerOperations.Api.Tests;

public class TargetCollectionServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeMonitoringTargetRepository _targets = new();
    private readonly FakeMetricSnapshotRepository _snapshots = new();
    private readonly FakeIncidentRepository _incidents = new();
    private readonly FakeIncidentLogRepository _logs = new();
    private readonly FakeDockerAdapter _docker = new();
    private readonly FakeHttpAdapter _http = new();
    private readonly TestTimeProvider _time = new(BaseTime);

    private readonly Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider _dataProtection = new();

    private TargetCollectionService CreateSut() => new(
        _targets, _snapshots, _incidents, _logs, _docker, _http, _dataProtection, _time,
        NullLogger<TargetCollectionService>.Instance);

    private void AddDockerTarget(long id = 1)
    {
        _targets.Targets.Add(new Core.Models.Operations.MonitoringTarget
        {
            Id = id,
            Name = $"docker{id}",
            TemplateId = "docker-host",
            IsEnabled = true,
            Profile = new TargetProfile
            {
                TargetId = id,
                SettingsJson = JsonSerializer.Serialize(
                    new Dictionary<string, string> { ["endpoint"] = "http://192.168.1.20:2375" }),
            },
        });
    }

    private void AddHttpTarget(long id = 1)
    {
        _targets.Targets.Add(new Core.Models.Operations.MonitoringTarget
        {
            Id = id,
            Name = $"web{id}",
            TemplateId = "web-site",
            IsEnabled = true,
            Profile = new TargetProfile
            {
                TargetId = id,
                SettingsJson = JsonSerializer.Serialize(
                    new Dictionary<string, string> { ["url"] = "http://192.168.1.10/health" }),
            },
        });
    }

    [Fact]
    public async Task Collect_Docker_AllRunning_SavesSnapshotWithoutIncident()
    {
        AddDockerTarget();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "running", "Up 2 hours", 0)];

        await CreateSut().CollectAsync(1);

        var snapshot = Assert.Single(_snapshots.Snapshots);
        Assert.Equal(CollectionStatus.Ok, snapshot.Status);
        Assert.Contains("running", snapshot.PayloadJson);
        Assert.Empty(_incidents.Incidents);
    }

    [Fact]
    public async Task Collect_Docker_StoppedContainer_CreatesIncidentWithMaskedLog()
    {
        AddDockerTarget();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (137)", 3)];
        _docker.ContainerLogs["c1"] = "fatal error: db password=hunter2 connection refused";

        await CreateSut().CollectAsync(1);

        var incident = Assert.Single(_incidents.Incidents);
        Assert.Equal("ContainerStopped", incident.Classification);
        Assert.Equal("web", incident.Service);
        Assert.Equal(IncidentStatus.Open, incident.Status);

        var log = Assert.Single(_logs.Logs);
        Assert.DoesNotContain("hunter2", log.MaskedContent);
        Assert.Contains("***", log.MaskedContent);
        Assert.Equal(incident.Id, log.IncidentId);
    }

    [Fact]
    public async Task Collect_SameFailureTwice_AggregatesIntoOneIncident()
    {
        AddDockerTarget();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (137)", 3)];
        _docker.ContainerLogs["c1"] = "fatal error code 137";
        var sut = CreateSut();

        await sut.CollectAsync(1);
        _time.Now = BaseTime.AddMinutes(5);
        await sut.CollectAsync(1);

        var incident = Assert.Single(_incidents.Incidents);
        Assert.Equal(2, incident.OccurrenceCount);
        Assert.Equal(BaseTime.AddMinutes(5).UtcDateTime, incident.LastOccurredAt);
    }

    [Fact]
    public async Task Collect_RecurrenceAfterResolved_ReopensIncident()
    {
        AddDockerTarget();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (137)", 3)];
        var sut = CreateSut();

        await sut.CollectAsync(1);
        var incident = Assert.Single(_incidents.Incidents);
        incident.Status = IncidentStatus.Resolved;
        incident.ResolvedAt = _time.Now.UtcDateTime;

        _time.Now = BaseTime.AddMinutes(10);
        await sut.CollectAsync(1);

        Assert.Single(_incidents.Incidents);
        Assert.Equal(IncidentStatus.Open, incident.Status);
        Assert.Null(incident.ResolvedAt);
    }

    [Fact]
    public async Task Collect_Http_Failure_RecordsFailedSnapshotAndIncident()
    {
        AddHttpTarget();
        _http.Result = new AdapterConnectionResult(false, "HTTP 503 を受信しました(期待: 200)。", 120);

        await CreateSut().CollectAsync(1);

        var snapshot = Assert.Single(_snapshots.Snapshots);
        Assert.Equal(CollectionStatus.Failed, snapshot.Status);
        Assert.NotNull(snapshot.ErrorMessage);

        var incident = Assert.Single(_incidents.Incidents);
        Assert.Equal("HttpUnavailable", incident.Classification);
    }

    [Fact]
    public async Task Collect_AdapterThrows_RecordsCollectionFailure_NotStaleValues()
    {
        AddHttpTarget();
        var throwingHttp = new ThrowingHttpAdapter();
        var sut = new TargetCollectionService(
            _targets, _snapshots, _incidents, _logs, _docker, throwingHttp, _dataProtection, _time,
            NullLogger<TargetCollectionService>.Instance);

        await sut.CollectAsync(1);

        var snapshot = Assert.Single(_snapshots.Snapshots);
        Assert.Equal(CollectionStatus.Failed, snapshot.Status);
        Assert.Null(snapshot.PayloadJson);

        var incident = Assert.Single(_incidents.Incidents);
        Assert.Equal("CollectionFailed", incident.Classification);
    }

    [Fact]
    public async Task Collect_Http_SendsConfiguredBasicAuth()
    {
        AddHttpTarget();
        var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(
            _targets.Targets[0].Profile!.SettingsJson)!;
        settings["basicAuthUser"] = "monitor";
        _targets.Targets[0].Profile!.SettingsJson = JsonSerializer.Serialize(settings);
        _targets.Targets[0].Credentials.Add(new Core.Models.Operations.TargetCredential
        {
            TargetId = 1,
            Kind = "basicAuthPassword",
            ValueProtected = _dataProtection.CreateProtector("TargetCredential").Protect("collect-pass"),
        });

        await CreateSut().CollectAsync(1);

        var options = Assert.Single(_http.CalledOptions);
        Assert.Equal("monitor", options.BasicAuthUser);
        Assert.Equal("collect-pass", options.BasicAuthPassword);
    }

    [Fact]
    public async Task Collect_DisabledTarget_DoesNothing()
    {
        AddHttpTarget();
        _targets.Targets[0].IsEnabled = false;

        await CreateSut().CollectAsync(1);

        Assert.Empty(_snapshots.Snapshots);
        Assert.Empty(_incidents.Incidents);
    }

    private sealed class ThrowingHttpAdapter : IHttpAdapter
    {
        public Task<AdapterConnectionResult> TestConnectionAsync(
            HttpCheckOptions options, CancellationToken ct = default) =>
            throw new HttpRequestException("boom");
    }
}
