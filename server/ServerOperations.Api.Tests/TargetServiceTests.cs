using Microsoft.AspNetCore.DataProtection;
using ServerOperations.Core.Adapters.Implementations;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Services.Implementations;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Tests;

public class TargetServiceTests
{
    private readonly FakeMonitoringTargetRepository _repo = new();
    private readonly FakeDockerAdapter _docker = new();
    private readonly FakeHttpAdapter _http = new();
    private readonly FakeAuditService _audit = new();
    private readonly FakeCurrentUserAccessor _currentUser = new();
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));

    private TargetService CreateSut() => new(
        _repo, new AdapterTemplateCatalog(), _docker, _http,
        new EphemeralDataProtectionProvider(), _audit, _currentUser, _time);

    [Fact]
    public async Task Create_WithUnknownTemplate_Rejects()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.CreateAsync(new CreateTargetRequest
        {
            Name = "t1",
            TemplateId = "no-such-template",
        }));

        Assert.Equal("unknown_template", ex.Code);
    }

    [Fact]
    public async Task Create_WithMissingRequiredInput_Rejects()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.CreateAsync(new CreateTargetRequest
        {
            Name = "web1",
            TemplateId = "web-site",
            Settings = [],
        }));

        Assert.Equal("missing_required_input", ex.Code);
    }

    [Fact]
    public async Task Create_WithUnknownInputKey_Rejects()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.CreateAsync(new CreateTargetRequest
        {
            Name = "web1",
            TemplateId = "web-site",
            Settings = new() { ["url"] = "http://192.168.1.10/health", ["hack"] = "x" },
        }));

        Assert.Equal("unknown_input", ex.Code);
    }

    [Fact]
    public async Task Create_WithLocalhostUrl_Rejects()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.CreateAsync(new CreateTargetRequest
        {
            Name = "web1",
            TemplateId = "web-site",
            Settings = new() { ["url"] = "http://127.0.0.1/health" },
        }));

        Assert.Equal("url_not_allowed", ex.Code);
    }

    [Fact]
    public async Task Create_WebSite_AppliesDefaultsAndEncryptsCredentials()
    {
        var sut = CreateSut();

        var dto = await sut.CreateAsync(new CreateTargetRequest
        {
            Name = "web1",
            TemplateId = "web-site",
            Settings = new() { ["url"] = "http://192.168.1.10/health" },
            Credentials = new() { ["basicAuthPassword"] = "secret-pass" },
        });

        Assert.Equal("200", dto.Settings["expectedStatus"]);
        Assert.Equal("10", dto.Settings["timeoutSeconds"]);
        Assert.Contains("basicAuthPassword", dto.ConfiguredCredentials);

        var stored = Assert.Single(_repo.Targets);
        var credential = Assert.Single(stored.Credentials);
        Assert.DoesNotContain("secret-pass", credential.ValueProtected);
    }

    [Fact]
    public async Task Create_SecretPassedInSettings_Rejects()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.CreateAsync(new CreateTargetRequest
        {
            Name = "web1",
            TemplateId = "web-site",
            Settings = new()
            {
                ["url"] = "http://192.168.1.10/health",
                ["basicAuthPassword"] = "should-not-be-here",
            },
        }));

        Assert.Equal("secret_in_settings", ex.Code);
    }

    [Fact]
    public async Task Create_DockerHost_WithUnixSocket_Rejects()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.CreateAsync(new CreateTargetRequest
        {
            Name = "docker1",
            TemplateId = "docker-host",
            Settings = new() { ["endpoint"] = "unix:///var/run/docker.sock" },
        }));

        Assert.Equal("invalid_url_scheme", ex.Code);
    }

    [Fact]
    public async Task TestConnection_DockerHost_UsesRegisteredEndpointOnly()
    {
        var sut = CreateSut();
        await sut.CreateAsync(new CreateTargetRequest
        {
            Name = "docker1",
            TemplateId = "docker-host",
            Settings = new() { ["endpoint"] = "http://192.168.1.20:2375" },
        });

        var result = await sut.TestConnectionAsync(1);

        Assert.True(result.Success);
        Assert.Equal("http://192.168.1.20:2375", Assert.Single(_docker.CalledEndpoints));
        Assert.Contains(_audit.Entries, e => e.Action == "target.test_connection");
    }

    [Fact]
    public async Task TestConnection_WebSite_PassesCredentialsButNeverExposesThem()
    {
        var sut = CreateSut();
        await sut.CreateAsync(new CreateTargetRequest
        {
            Name = "web1",
            TemplateId = "web-site",
            Settings = new() { ["url"] = "http://192.168.1.10/health" },
            Credentials = new()
            {
                ["basicAuthPassword"] = "secret-pass",
            },
        });

        var result = await sut.TestConnectionAsync(1);

        // アダプターには復号済みの資格情報が渡る
        var options = Assert.Single(_http.CalledOptions);
        Assert.Equal("secret-pass", options.BasicAuthPassword);

        // 応答・監査には資格情報が含まれない
        Assert.DoesNotContain("secret-pass", System.Text.Json.JsonSerializer.Serialize(result));
        Assert.All(_audit.Entries, e => Assert.DoesNotContain("secret-pass", e.ToString()));
    }

    [Fact]
    public async Task TestConnection_UnknownTarget_Throws()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.TestConnectionAsync(999));

        Assert.Equal("target_not_found", ex.Code);
    }

    [Fact]
    public async Task GetCapabilities_ReturnsTemplateDrivenLists()
    {
        var sut = CreateSut();
        await sut.CreateAsync(new CreateTargetRequest
        {
            Name = "docker1",
            TemplateId = "docker-host",
            Settings = new() { ["endpoint"] = "http://192.168.1.20:2375" },
        });

        var caps = await sut.GetCapabilitiesAsync(1);

        Assert.Contains("docker.containers.list", caps.Capabilities);
        Assert.Contains("RESTART_ALLOWED_CONTAINER", caps.AllowedOperations);
        Assert.Contains("ContainerStopped", caps.InitialRules);
    }

    [Fact]
    public async Task Create_DuplicateName_Rejects()
    {
        var sut = CreateSut();
        await sut.CreateAsync(new CreateTargetRequest
        {
            Name = "web1",
            TemplateId = "web-site",
            Settings = new() { ["url"] = "http://192.168.1.10/health" },
        });

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.CreateAsync(new CreateTargetRequest
        {
            Name = "web1",
            TemplateId = "web-site",
            Settings = new() { ["url"] = "http://192.168.1.11/health" },
        }));

        Assert.Equal("duplicate_target_name", ex.Code);
    }

    // --- 削除 ---

    private MonitoringTarget AddTarget(bool isEnabled = false) =>
        _repo.Targets[_repo.Targets.Count - 1];

    [Fact]
    public async Task 削除で消えるものの件数を先に示す()
    {
        // 削除は元に戻せないため、何が消えるかを見てから決められるようにする
        var sut = CreateSut();
        var created = await sut.CreateAsync(new CreateTargetRequest
        {
            Name = "t1",
            TemplateId = "docker-host",
            Settings = new() { ["endpoint"] = "http://192.168.1.20:2375" },
        });

        _repo.Dependents = new TargetDependents
        {
            MetricSnapshots = 120,
            Incidents = 3,
            IncidentLogs = 40,
            Diagnoses = 3,
            RecoveryActions = 2,
            HealthChecks = 5,
            Notifications = 1,
            MaintenanceWindows = 0,
        };

        var preview = await sut.PreviewDeleteAsync(created.Id);

        Assert.Equal(120, preview.MetricSnapshots);
        Assert.Equal(3, preview.Incidents);
        Assert.Equal(174, preview.Total);
    }

    [Fact]
    public async Task 監視中の対象は削除できない()
    {
        // 動いているものを誤って消さない。止めてから消す手順にする。
        var sut = CreateSut();
        var created = await sut.CreateAsync(new CreateTargetRequest
        {
            Name = "t1",
            TemplateId = "docker-host",
            Settings = new() { ["endpoint"] = "http://192.168.1.20:2375" },
        });

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.DeleteAsync(created.Id));

        Assert.Equal("target_still_enabled", ex.Code);
    }

    [Fact]
    public async Task 監視を止めてあれば削除できる()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(new CreateTargetRequest
        {
            Name = "t1",
            TemplateId = "docker-host",
            Settings = new() { ["endpoint"] = "http://192.168.1.20:2375" },
        });
        AddTarget().IsEnabled = false;

        await sut.DeleteAsync(created.Id);

        Assert.DoesNotContain(_repo.Targets, t => t.Id == created.Id);
    }

    [Fact]
    public async Task 削除で消えた件数を監査に残す()
    {
        // 削除後は本体から辿れなくなるため、件数だけでも記録に残す
        var sut = CreateSut();
        var created = await sut.CreateAsync(new CreateTargetRequest
        {
            Name = "t1",
            TemplateId = "docker-host",
            Settings = new() { ["endpoint"] = "http://192.168.1.20:2375" },
        });
        AddTarget().IsEnabled = false;
        _repo.Dependents = _repo.Dependents with { Incidents = 7 };

        await sut.DeleteAsync(created.Id);

        var entry = Assert.Single(_audit.Entries, e => e.Action == "target.delete");
        Assert.Contains("incidents=7", entry.Details ?? string.Empty);
        Assert.Contains("name=t1", entry.Details ?? string.Empty);
    }

    [Fact]
    public async Task 存在しない対象は削除できない()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().DeleteAsync(999));

        Assert.Equal("target_not_found", ex.Code);
    }

    [Fact]
    public async Task 削除しても監査ログは消さない()
    {
        // 誰が何をしたかの記録は、対象が消えても残す必要がある
        var sut = CreateSut();
        var created = await sut.CreateAsync(new CreateTargetRequest
        {
            Name = "t1",
            TemplateId = "docker-host",
            Settings = new() { ["endpoint"] = "http://192.168.1.20:2375" },
        });
        AddTarget().IsEnabled = false;
        var beforeCount = _audit.Entries.Count;

        await sut.DeleteAsync(created.Id);

        Assert.True(_audit.Entries.Count > beforeCount);
    }
}
