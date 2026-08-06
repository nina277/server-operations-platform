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
    private readonly FakeHostMetricsAdapter _hostMetrics = new();
    private readonly TestTimeProvider _time = new(BaseTime);

    private readonly Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider _dataProtection = new();
    private readonly FakeDiagnosisService _diagnosis = new();
    private readonly FakeNotificationService _notifications = new();
    private readonly FakeAutoRecoveryService _autoRecovery = new();

    // しきい値の判定は本物を使う。差し替えると「ルールに当たったか」を確かめられない
    private readonly FakeDiagnosticRuleRepository _rules = new();

    private TargetCollectionService CreateSut() => new(
        _targets, _snapshots, _incidents, _logs, _docker, _http, _hostMetrics, new AdapterTemplateCatalog(),
        _dataProtection, _diagnosis, new ResourceThresholdDetector(_rules, new RuleEngine()),
        _notifications, _autoRecovery, _time,
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

        var snapshot = Assert.Single(_snapshots.Snapshots, s => s.Kind == "docker");
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
            _targets, _snapshots, _incidents, _logs, _docker, throwingHttp, _hostMetrics, new AdapterTemplateCatalog(),
            _dataProtection, _diagnosis, new ResourceThresholdDetector(_rules, new RuleEngine()),
            _notifications, _autoRecovery, _time,
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
            _targets, _snapshots, _incidents, _logs, _docker, new ThrowingHttpAdapter(), _hostMetrics,
            new AdapterTemplateCatalog(), _dataProtection, _diagnosis,
            new ResourceThresholdDetector(_rules, new RuleEngine()), _notifications, _autoRecovery,
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

        // 状態・ログ・使用率のすべてを行う
        Assert.Single(_snapshots.Snapshots, s => s.Kind == "docker");
        Assert.Single(_snapshots.Snapshots, s => s.Kind == "resource");
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

        Assert.Single(_snapshots.Snapshots, s => s.Kind == "docker");
    }

    // --- リソース使用率(B-10) ---

    private void AddMemoryPressureRule(double threshold = 90)
    {
        _rules.Rules.Add(new DiagnosticRule
        {
            Id = 1,
            Name = "メモリ逼迫(使用率)",
            Classification = "MemoryPressure",
            RuleType = DiagnosticRuleType.Threshold,
            ConditionJson =
                $$"""{"field":"memoryUsagePercent","operator":">=","value":{{threshold}}}""",
            Severity = IncidentSeverity.Medium,
            Priority = 20,
            RationaleTemplate = "メモリ使用率が {value}% に達しています。",
        });
    }

    [Fact]
    public async Task 稼働中コンテナの使用率を収集して残す()
    {
        AddDockerTarget();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "running", "Up", 0)];
        _docker.Stats["c1"] = new ContainerStats(12.5, 40.0, 400, 1000);

        await CreateSut().CollectAsync(1);

        var snapshot = Assert.Single(_snapshots.Snapshots, s => s.Kind == "resource");
        Assert.Equal(CollectionStatus.Ok, snapshot.Status);
        Assert.Contains("12.5", snapshot.PayloadJson);
        Assert.Contains("40", snapshot.PayloadJson);
    }

    [Fact]
    public async Task 停止中のコンテナは測らない()
    {
        // 停止中に使用率は無い。問い合わせても意味の無い値しか返らない
        AddDockerTarget();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (137)", 1)];

        await CreateSut().CollectAsync(1);

        Assert.Empty(_docker.StatsRequests);
    }

    [Fact]
    public async Task リソース使用率を外すと取りに行かない()
    {
        // 外しても呼び続けるなら、設定が効いていないのと同じ
        AddDockerTarget();
        SetEnabledMonitors(1, $"[\"{MonitorKinds.ContainerState}\"]");
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "running", "Up", 0)];

        await CreateSut().CollectAsync(1);

        Assert.Empty(_docker.StatsRequests);
        Assert.DoesNotContain(_snapshots.Snapshots, s => s.Kind == "resource");
    }

    [Fact]
    public async Task 状態もリソースも外せばコンテナ一覧すら取りに行かない()
    {
        AddDockerTarget();
        SetEnabledMonitors(1, $"[\"{MonitorKinds.LogExcerpt}\"]");
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "running", "Up", 0)];

        await CreateSut().CollectAsync(1);

        Assert.Empty(_docker.CalledEndpoints);
    }

    [Fact]
    public async Task 状態を外してもリソース使用率だけは収集できる()
    {
        // 使用率はコンテナごとの値なので、一覧の取得自体は避けられない。
        // ただし状態のスナップショットとインシデント化は行わない。
        AddDockerTarget();
        SetEnabledMonitors(1, $"[\"{MonitorKinds.ResourceUsage}\"]");
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (137)", 1)];

        await CreateSut().CollectAsync(1);

        Assert.DoesNotContain(_snapshots.Snapshots, s => s.Kind == "docker");
        Assert.Single(_snapshots.Snapshots, s => s.Kind == "resource");
        Assert.Empty(_incidents.Incidents);
    }

    [Fact]
    public async Task しきい値を超えたらインシデントにする()
    {
        // これまでしきい値ルールは説明にしか使われず、自分では何も起こせなかった
        AddDockerTarget();
        AddMemoryPressureRule();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "running", "Up", 0)];
        _docker.Stats["c1"] = new ContainerStats(5.0, 95.0, 950, 1000);

        await CreateSut().CollectAsync(1);

        var incident = Assert.Single(_incidents.Incidents);
        Assert.Equal("MemoryPressure", incident.Classification);
        Assert.Equal("web", incident.Service);
        Assert.Equal(IncidentSeverity.Medium, incident.Severity);
    }

    [Fact]
    public async Task しきい値の範囲内ならインシデントにしない()
    {
        AddDockerTarget();
        AddMemoryPressureRule();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "running", "Up", 0)];
        _docker.Stats["c1"] = new ContainerStats(5.0, 40.0, 400, 1000);

        await CreateSut().CollectAsync(1);

        Assert.Empty(_incidents.Incidents);
    }

    [Fact]
    public async Task 使用率が取れなければインシデントにしない()
    {
        // 取れないことを正常とも異常とも決めつけない
        AddDockerTarget();
        AddMemoryPressureRule();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "running", "Up", 0)];

        await CreateSut().CollectAsync(1);

        Assert.Empty(_incidents.Incidents);
    }

    [Fact]
    public async Task 使用率が1件も取れなければ収集失敗として残す()
    {
        // 空の結果を正常な収集として記録すると、取れていないことが見えなくなる
        AddDockerTarget();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "running", "Up", 0)];

        await CreateSut().CollectAsync(1);

        var snapshot = Assert.Single(_snapshots.Snapshots, s => s.Kind == "resource");
        Assert.Equal(CollectionStatus.Failed, snapshot.Status);
        Assert.NotNull(snapshot.ErrorMessage);
    }

    [Fact]
    public async Task 稼働中コンテナが無ければ失敗にしない()
    {
        AddDockerTarget();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "exited", "Exited (0)", 0)];

        await CreateSut().CollectAsync(1);

        var snapshot = Assert.Single(_snapshots.Snapshots, s => s.Kind == "resource");
        Assert.Equal(CollectionStatus.Ok, snapshot.Status);
    }

    [Fact]
    public async Task 測るコンテナ数に上限を設ける()
    {
        // 1件あたり約1秒かかるため、際限なく増やすと収集間隔を超える
        AddDockerTarget();
        _docker.Containers = Enumerable.Range(1, TargetCollectionService.MaxStatsContainers + 5)
            .Select(i => new ContainerInfo($"c{i:00}", $"svc{i:00}", "nginx:1.27", "running", "Up", 0))
            .ToList();
        foreach (var container in _docker.Containers)
        {
            _docker.Stats[container.Id] = new ContainerStats(1.0, 1.0, 10, 1000);
        }

        await CreateSut().CollectAsync(1);

        Assert.Equal(TargetCollectionService.MaxStatsContainers, _docker.StatsRequests.Count);

        // 測らなかった件数を残す。黙って省くと「全部見ている」と誤解される
        var snapshot = Assert.Single(_snapshots.Snapshots, s => s.Kind == "resource");
        Assert.Contains("\"skipped\":5", snapshot.PayloadJson);
    }

    [Fact]
    public async Task 同じコンテナの逼迫は1件のインシデントにまとめる()
    {
        AddDockerTarget();
        AddMemoryPressureRule();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "running", "Up", 0)];
        _docker.Stats["c1"] = new ContainerStats(5.0, 95.0, 950, 1000);
        var sut = CreateSut();

        await sut.CollectAsync(1);
        _time.Now = BaseTime.AddMinutes(5);
        await sut.CollectAsync(1);

        var incident = Assert.Single(_incidents.Incidents);
        Assert.Equal(2, incident.OccurrenceCount);
    }

    [Fact]
    public async Task 使用率の逼迫では自動復旧を試みない()
    {
        // 使用率が高いだけでは「何を再起動すれば直るか」が定まらない
        AddDockerTarget();
        AddMemoryPressureRule();
        _docker.Containers = [new ContainerInfo("c1", "web", "nginx:1.27", "running", "Up", 0)];
        _docker.Stats["c1"] = new ContainerStats(5.0, 95.0, 950, 1000);

        await CreateSut().CollectAsync(1);

        Assert.Empty(_autoRecovery.Calls);
    }

    // --- ホストのディスク使用率(B-11) ---

    private void SetMetricsEndpoint(long id, string url)
    {
        var target = _targets.Targets.Single(t => t.Id == id);
        var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(
            target.Profile!.SettingsJson)!;
        settings["metricsEndpoint"] = url;
        target.Profile.SettingsJson = JsonSerializer.Serialize(settings);
    }

    private void AddDiskPressureRule(double threshold = 85)
    {
        _rules.Rules.Add(new DiagnosticRule
        {
            Id = _rules.Rules.Count + 1,
            Name = "ディスク逼迫(使用率)",
            Classification = "DiskPressure",
            RuleType = DiagnosticRuleType.Threshold,
            ConditionJson =
                $$"""{"field":"diskUsagePercent","operator":">=","value":{{threshold}}}""",
            Severity = IncidentSeverity.Medium,
            Priority = 20,
            RationaleTemplate = "ディスク使用率が {value}% に達しています。",
        });
    }

    [Fact]
    public async Task ホストのディスク使用率を収集して残す()
    {
        AddDockerTarget();
        SetMetricsEndpoint(1, "http://192.168.1.20:9100/metrics");
        _hostMetrics.Filesystems = [new FilesystemUsage("/", 1000, 100, 88.89)];

        await CreateSut().CollectAsync(1);

        var snapshot = Assert.Single(_snapshots.Snapshots, s => s.Kind == "disk");
        Assert.Equal(CollectionStatus.Ok, snapshot.Status);
        Assert.Contains("88.89", snapshot.PayloadJson);
        Assert.Contains("/", snapshot.PayloadJson);
    }

    [Fact]
    public async Task 接続先が未設定なら取りに行かない()
    {
        // 設定していない対象で毎回失敗を積み上げない
        AddDockerTarget();

        await CreateSut().CollectAsync(1);

        Assert.Empty(_hostMetrics.CalledUrls);
        Assert.DoesNotContain(_snapshots.Snapshots, s => s.Kind == "disk");
    }

    [Fact]
    public async Task ディスク使用率を外すと取りに行かない()
    {
        AddDockerTarget();
        SetMetricsEndpoint(1, "http://192.168.1.20:9100/metrics");
        SetEnabledMonitors(1, $"[\"{MonitorKinds.ContainerState}\"]");

        await CreateSut().CollectAsync(1);

        Assert.Empty(_hostMetrics.CalledUrls);
    }

    [Fact]
    public async Task ディスクのしきい値を超えたらインシデントにする()
    {
        AddDockerTarget();
        SetMetricsEndpoint(1, "http://192.168.1.20:9100/metrics");
        AddDiskPressureRule();
        _hostMetrics.Filesystems = [new FilesystemUsage("/", 1000, 50, 92.0)];

        await CreateSut().CollectAsync(1);

        var incident = Assert.Single(_incidents.Incidents);
        Assert.Equal("DiskPressure", incident.Classification);

        // どのファイルシステムが逼迫しているかが分からないと手当てのしようがない
        Assert.Equal("/", incident.Service);
    }

    [Fact]
    public async Task ファイルシステムごとに別のインシデントにする()
    {
        // / と /mnt/data では消すべきものが違う。1件にまとめると手当てを誤る
        AddDockerTarget();
        SetMetricsEndpoint(1, "http://192.168.1.20:9100/metrics");
        AddDiskPressureRule();
        _hostMetrics.Filesystems =
        [
            new FilesystemUsage("/", 1000, 50, 92.0),
            new FilesystemUsage("/mnt/data", 1000, 20, 96.0),
            new FilesystemUsage("/boot", 1000, 800, 20.0),
        ];

        await CreateSut().CollectAsync(1);

        Assert.Equal(2, _incidents.Incidents.Count);
        Assert.Equal(["/", "/mnt/data"], _incidents.Incidents.Select(i => i.Service));
    }

    [Fact]
    public async Task ディスクの逼迫では自動復旧を試みない()
    {
        // コンテナを再起動しても容量は戻らない。消してよいものを決められるのは人だけ
        AddDockerTarget();
        SetMetricsEndpoint(1, "http://192.168.1.20:9100/metrics");
        AddDiskPressureRule();
        _hostMetrics.Filesystems = [new FilesystemUsage("/", 1000, 50, 92.0)];

        await CreateSut().CollectAsync(1);

        Assert.Empty(_autoRecovery.Calls);
    }

    [Fact]
    public async Task ディスク使用率が読めなければ収集失敗として残す()
    {
        // 空の結果を正常な収集として記録すると、取れていないことが見えなくなる
        AddDockerTarget();
        SetMetricsEndpoint(1, "http://192.168.1.20:9100/metrics");
        AddDiskPressureRule();

        await CreateSut().CollectAsync(1);

        var snapshot = Assert.Single(_snapshots.Snapshots, s => s.Kind == "disk");
        Assert.Equal(CollectionStatus.Failed, snapshot.Status);
        Assert.NotNull(snapshot.ErrorMessage);

        // 読めなかったことを「しきい値を下回っている」と読み替えない
        Assert.Empty(_incidents.Incidents);
    }

    [Fact]
    public async Task Composeアプリの対象ではディスク使用率を扱わない()
    {
        // ホストのディスクはComposeプロジェクト単位の値ではない。
        // 同じホストの対象ごとに同じ数値のインシデントが並ぶのを避ける
        var template = new AdapterTemplateCatalog().Find("docker-compose-app")!;

        Assert.DoesNotContain(MonitorKinds.DiskUsage, template.CollectableMonitors);
    }
}
