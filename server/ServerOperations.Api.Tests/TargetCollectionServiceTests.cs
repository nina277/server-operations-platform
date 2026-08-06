using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using ServerOperations.Core.Adapters.Implementations;
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
    private readonly FakeDiagnosisService _diagnosis = new();
    private readonly FakeNotificationService _notifications = new();
    private readonly FakeAutoRecoveryService _autoRecovery = new();

    private TargetCollectionService CreateSut() => new(
        _targets, _snapshots, _incidents, _logs, _docker, _http, new AdapterTemplateCatalog(),
        _dataProtection, _diagnosis, _notifications, _autoRecovery, _time,
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
            _targets, _snapshots, _incidents, _logs, _docker, throwingHttp, new AdapterTemplateCatalog(),
            _dataProtection, _diagnosis, _notifications, _autoRecovery, _time,
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
    public async Task Collect_NewIncident_TriggersDiagnosisWithMaskedContext()
    {
        AddDockerTarget();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (137)", 3)];
        _docker.ContainerLogs["c1"] = "OOM killed: password=hunter2";

        await CreateSut().CollectAsync(1);

        var call = Assert.Single(_diagnosis.Calls);
        Assert.Equal("exited", call.Context.ContainerState);
        Assert.Equal("web", call.Context.ContainerName);
        Assert.Equal(3, call.Context.RestartCount);
        // 診断へ渡すログもマスク済み
        Assert.DoesNotContain("hunter2", call.Context.LogExcerpt);
    }

    [Fact]
    public async Task Collect_ContinuingIncident_DoesNotRediagnose()
    {
        AddDockerTarget();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (137)", 3)];
        var sut = CreateSut();

        await sut.CollectAsync(1);
        _time.Now = BaseTime.AddMinutes(5);
        await sut.CollectAsync(1);

        // 継続中の同一障害では診断を重ねない
        Assert.Single(_diagnosis.Calls);
    }

    [Fact]
    public async Task Collect_RecurrenceAfterResolved_TriggersRediagnosis()
    {
        AddDockerTarget();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (137)", 3)];
        var sut = CreateSut();

        await sut.CollectAsync(1);
        var incident = Assert.Single(_incidents.Incidents);
        incident.Status = IncidentStatus.Resolved;

        _time.Now = BaseTime.AddMinutes(10);
        await sut.CollectAsync(1);

        Assert.Equal(2, _diagnosis.Calls.Count);
    }

    [Fact]
    public async Task Collect_NewIncident_SendsNotificationWithoutLogContents()
    {
        AddDockerTarget();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (137)", 3)];
        _docker.ContainerLogs["c1"] = "fatal: password=hunter2 stack trace line1 line2 line3";

        await CreateSut().CollectAsync(1);

        var request = Assert.Single(_notifications.Requests);
        Assert.Equal(NotificationSeverity.High, request.Severity);
        Assert.Equal(_incidents.Incidents[0].SignatureSha256, request.AggregationKey);
        // 通知本文にログ全文・秘密情報を含めない
        Assert.DoesNotContain("hunter2", request.Body);
        Assert.DoesNotContain("stack trace", request.Body);
    }

    [Fact]
    public async Task Collect_ContinuingIncident_DoesNotResendNotification()
    {
        AddDockerTarget();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (137)", 3)];
        var sut = CreateSut();

        await sut.CollectAsync(1);
        _time.Now = BaseTime.AddMinutes(5);
        await sut.CollectAsync(1);

        // 既存インシデントへの集約時は通知を作り直さない
        Assert.Single(_notifications.Requests);
    }

    [Fact]
    public async Task Collect_WithDiagnosis_InvokesAutoRecovery()
    {
        AddDockerTarget();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (137)", 3)];
        _diagnosis.Result = new Diagnosis
        {
            Id = 1,
            IncidentId = 1,
            TargetId = 1,
            Source = DiagnosisSource.Rule,
            Classification = "ContainerStopped",
            Severity = IncidentSeverity.High,
            Rationale = "コンテナ状態が exited です。",
            RecommendedActionId = "RESTART_ALLOWED_CONTAINER",
            RecommendedActionAllowed = true,
        };

        await CreateSut().CollectAsync(1);

        var call = Assert.Single(_autoRecovery.Calls);
        Assert.Equal(1, call.Target.Id);
        Assert.Equal("ContainerStopped", call.Diagnosis.Classification);
    }

    [Fact]
    public async Task Collect_WithoutDiagnosis_DoesNotInvokeAutoRecovery()
    {
        AddDockerTarget();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (137)", 3)];
        // 診断できなかった場合(ルール未一致・履歴なし)は自動復旧しない
        _diagnosis.Result = null;

        await CreateSut().CollectAsync(1);

        Assert.Empty(_autoRecovery.Calls);
    }

    [Fact]
    public async Task Collect_CollectionFailure_DoesNotInvokeAutoRecovery()
    {
        // 対象へ到達できていない状態で復旧操作を試みない
        AddHttpTarget();
        var sut = new TargetCollectionService(
            _targets, _snapshots, _incidents, _logs, _docker, new ThrowingHttpAdapter(),
            new AdapterTemplateCatalog(), _dataProtection, _diagnosis, _notifications, _autoRecovery,
            _time,
            NullLogger<TargetCollectionService>.Instance);

        await sut.CollectAsync(1);

        Assert.Empty(_autoRecovery.Calls);
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

    // --- 対象ごとの監視項目(B-06) ---

    private void SetEnabledMonitors(long id, string? json) =>
        _targets.Targets.Single(t => t.Id == id).EnabledMonitorsJson = json;

    [Fact]
    public async Task 未設定ならテンプレートの既定どおり収集する()
    {
        AddDockerTarget();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (137)", 3)];
        _docker.ContainerLogs["c1"] = "fatal error";

        await CreateSut().CollectAsync(1);

        Assert.Single(_snapshots.Snapshots);
        Assert.Single(_docker.LogRequests);
    }

    [Fact]
    public async Task ログ抜粋を外すとログを取りに行かない()
    {
        // 外しても呼び続けるなら、設定が効いていないのと同じ
        AddDockerTarget();
        SetEnabledMonitors(1, $"[\"{MonitorKinds.ContainerState}\"]");
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (137)", 3)];
        _docker.ContainerLogs["c1"] = "fatal error";

        await CreateSut().CollectAsync(1);

        Assert.Empty(_docker.LogRequests);
    }

    [Fact]
    public async Task ログ抜粋を外してもインシデントは作る()
    {
        // 止めたのはログの取得だけ。停止の検知まで止めては監視にならない。
        AddDockerTarget();
        SetEnabledMonitors(1, $"[\"{MonitorKinds.ContainerState}\"]");
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (137)", 3)];

        await CreateSut().CollectAsync(1);

        Assert.Single(_incidents.Incidents);
    }

    [Fact]
    public async Task コンテナ状態を外すと収集そのものを行わない()
    {
        AddDockerTarget();
        SetEnabledMonitors(1, $"[\"{MonitorKinds.LogExcerpt}\"]");
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (137)", 3)];

        await CreateSut().CollectAsync(1);

        Assert.Empty(_snapshots.Snapshots);
        Assert.Empty(_incidents.Incidents);
    }

    [Fact]
    public async Task 壊れた設定でも収集は止めない()
    {
        // 設定が壊れただけで監視が黙って止まるのは避ける
        AddDockerTarget();
        SetEnabledMonitors(1, "これはJSONではない");
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "running", "Up", 0)];

        await CreateSut().CollectAsync(1);

        Assert.Single(_snapshots.Snapshots);
    }
}
