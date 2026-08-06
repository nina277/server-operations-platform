using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Tests.Fakes;

public class FakeMonitoringTargetRepository : IMonitoringTargetRepository
{
    public List<MonitoringTarget> Targets { get; } = [];

    public Task<MonitoringTarget?> FindByIdAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Targets.FirstOrDefault(t => t.Id == id));

    public Task<MonitoringTarget?> FindByNameAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(Targets.FirstOrDefault(t => t.Name == name));

    public Task<List<MonitoringTarget>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult(Targets.OrderBy(t => t.Id).ToList());

    public Task AddAsync(MonitoringTarget target, CancellationToken ct = default)
    {
        target.Id = Targets.Count + 1;
        Targets.Add(target);
        return Task.CompletedTask;
    }

    /// <summary>削除で消えるものの件数。既定では0(必要なテストが値を入れる)。</summary>
    public TargetDependents Dependents { get; set; } = new()
    {
        MetricSnapshots = 0,
        Incidents = 0,
        IncidentLogs = 0,
        Diagnoses = 0,
        RecoveryActions = 0,
        HealthChecks = 0,
        Notifications = 0,
        MaintenanceWindows = 0,
    };

    public List<MonitoringTarget> Deleted { get; } = [];

    public Task<TargetDependents> CountDependentsAsync(long targetId, CancellationToken ct = default) =>
        Task.FromResult(Dependents);

    public Task DeleteWithDependentsAsync(MonitoringTarget target, CancellationToken ct = default)
    {
        Targets.Remove(target);
        Deleted.Add(target);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeDockerAdapter : IDockerAdapter
{
    public AdapterConnectionResult Result { get; set; } =
        new(true, "Docker APIへ接続できました。", 12, "Engine 27.0 / API 1.47");

    public List<string> CalledEndpoints { get; } = [];

    public List<ContainerInfo> Containers { get; set; } = [];

    public Dictionary<string, string> ContainerLogs { get; } = [];

    public Task<AdapterConnectionResult> TestConnectionAsync(string endpoint, CancellationToken ct = default)
    {
        CalledEndpoints.Add(endpoint);
        return Task.FromResult(Result);
    }

    public Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(
        string endpoint, string? composeProject = null, CancellationToken ct = default)
    {
        CalledEndpoints.Add(endpoint);
        return Task.FromResult<IReadOnlyList<ContainerInfo>>(Containers);
    }

    /// <summary>コンテナIDごとに返す使用率。指定が無ければ未取得(null)として返す。</summary>
    public Dictionary<string, ContainerStats> Stats { get; } = [];

    /// <summary>使用率を取りに行った回数。外したときに呼ばれないことを確かめるのに使う。</summary>
    public List<string> StatsRequests { get; } = [];

    public Task<ContainerStats?> GetContainerStatsAsync(
        string endpoint, string containerId, CancellationToken ct = default)
    {
        StatsRequests.Add(containerId);
        return Task.FromResult(Stats.GetValueOrDefault(containerId));
    }

    /// <summary>ログを取りに行った回数。外したときに呼ばれないことを確かめるのに使う。</summary>
    public List<string> LogRequests { get; } = [];

    public Task<string> GetContainerLogsAsync(
        string endpoint, string containerId, int tailLines = 50, CancellationToken ct = default)
    {
        LogRequests.Add(containerId);
        return Task.FromResult(ContainerLogs.GetValueOrDefault(containerId, string.Empty));
    }

    public List<(string Endpoint, string Container, ContainerOperation Operation)> ControlCalls { get; } = [];

    public AdapterConnectionResult ControlResult { get; set; } = new(true, "操作に成功しました。");

    public Task<AdapterConnectionResult> ControlContainerAsync(
        string endpoint, string containerNameOrId, ContainerOperation operation, CancellationToken ct = default)
    {
        ControlCalls.Add((endpoint, containerNameOrId, operation));
        return Task.FromResult(ControlResult);
    }
}

public class FakeHttpAdapter : IHttpAdapter
{
    public AdapterConnectionResult Result { get; set; } = new(true, "HTTP 200 を受信しました(期待どおり)。", 34);

    public List<HttpCheckOptions> CalledOptions { get; } = [];

    public Task<AdapterConnectionResult> TestConnectionAsync(
        HttpCheckOptions options, CancellationToken ct = default)
    {
        CalledOptions.Add(options);
        return Task.FromResult(Result);
    }
}
