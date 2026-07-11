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

    public Task<string> GetContainerLogsAsync(
        string endpoint, string containerId, int tailLines = 50, CancellationToken ct = default) =>
        Task.FromResult(ContainerLogs.GetValueOrDefault(containerId, string.Empty));
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
