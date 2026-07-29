using Microsoft.Extensions.Logging.Abstractions;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Adapters.Implementations;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services;
using ServerOperations.Core.Services.Ai;

namespace ServerOperations.Api.Tests;

public class AiDiagnosisGatewayTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeAiUsageLimitRepository _limits = new();
    private readonly FakeAiUsageRecordRepository _usage = new();
    private readonly FakeAiApiKeyProvider _apiKey = new();
    private readonly StubAiProvider _provider = new();
    private readonly FakeIncidentRepository _incidents = new();
    private readonly FakeDiagnosisRepository _diagnoses;
    private readonly FakeMonitoringTargetRepository _targets = new();
    private readonly TestTimeProvider _time = new(BaseTime);

    public AiDiagnosisGatewayTests()
    {
        _diagnoses = new FakeDiagnosisRepository(_incidents);
        _targets.Targets.Add(new MonitoringTarget
        {
            Id = 1,
            Name = "docker1",
            TemplateId = "docker-host",
            IsEnabled = true,
        });
    }

    private AiDiagnosisGateway CreateSut() => new(
        _limits, _usage, _apiKey, _provider, _diagnoses, _targets,
        new AdapterTemplateCatalog(), new RecoveryActionCatalog(), _time,
        NullLogger<AiDiagnosisGateway>.Instance);

    private Incident AddIncident(long id = 1, string signature = "sig-a")
    {
        var incident = new Incident
        {
            Id = id,
            TargetId = 1,
            SignatureSha256 = signature,
            Title = "未知の障害",
            Classification = "UnknownLog",
            Service = "web",
            Severity = IncidentSeverity.Medium,
            FirstOccurredAt = BaseTime.UtcDateTime,
            LastOccurredAt = BaseTime.UtcDateTime,
        };
        _incidents.Incidents.Add(incident);
        return incident;
    }

    private void EnableAi() => _limits.Limit = new AiUsageLimit
    {
        Id = 1,
        IsEnabled = true,
        Model = "gemini-2.0-flash",
        MonthlyLimit = 80,
        DailyLimit = 10,
        HourlyLimit = 3,
        MaxInputCharacters = 6000,
        MaxOutputTokens = 500,
    };

    [Fact]
    public async Task Diagnose_WhenDisabled_DoesNotCallProvider()
    {
        _limits.Limit = new AiUsageLimit { Id = 1, IsEnabled = false };
        var incident = AddIncident();

        var result = await CreateSut().DiagnoseAsync(incident, "log");

        Assert.Null(result.Diagnosis);
        Assert.Equal(AiSkipReason.Disabled, result.SkipReason);
        Assert.Empty(_provider.Calls);
    }

    [Fact]
    public async Task Diagnose_WithNoSettings_IsDisabled()
    {
        var incident = AddIncident();

        var result = await CreateSut().DiagnoseAsync(incident, "log");

        Assert.Equal(AiSkipReason.Disabled, result.SkipReason);
        Assert.Empty(_provider.Calls);
    }

    [Fact]
    public async Task Diagnose_WithoutApiKey_DoesNotCallProvider()
    {
        EnableAi();
        _apiKey.ApiKey = null;
        var incident = AddIncident();

        var result = await CreateSut().DiagnoseAsync(incident, "log");

        Assert.Equal(AiSkipReason.ApiKeyMissing, result.SkipReason);
        Assert.Empty(_provider.Calls);
    }

    [Fact]
    public async Task Diagnose_SameSignatureHistory_ReusesWithoutCallingAi()
    {
        EnableAi();
        var past = AddIncident(id: 1, signature: "shared");
        _diagnoses.Diagnoses.Add(new Diagnosis
        {
            Id = 1,
            IncidentId = past.Id,
            TargetId = 1,
            Source = DiagnosisSource.Ai,
            Classification = "PreviousFinding",
            Severity = IncidentSeverity.High,
            Rationale = "過去の判断",
            CreatedAt = BaseTime.UtcDateTime,
        });
        var incident = AddIncident(id: 2, signature: "shared");

        var result = await CreateSut().DiagnoseAsync(incident, "log");

        Assert.NotNull(result.Diagnosis);
        Assert.Equal(AiSkipReason.HistoryReused, result.SkipReason);
        Assert.Equal(DiagnosisSource.History, result.Diagnosis.Source);
        // 同一署名ではAIを再呼出しない
        Assert.Empty(_provider.Calls);
    }

    [Fact]
    public async Task Diagnose_HourlyLimitReached_DoesNotCallProvider()
    {
        EnableAi();
        // 直近1時間に3件(上限3)
        SeedUsage(minutesAgo: [10, 20, 30]);
        var incident = AddIncident();

        var result = await CreateSut().DiagnoseAsync(incident, "log");

        Assert.Null(result.Diagnosis);
        Assert.Equal(AiSkipReason.LimitReached, result.SkipReason);
        Assert.Contains("1時間", result.Message);
        Assert.Empty(_provider.Calls);
    }

    [Fact]
    public async Task Diagnose_DailyLimitReached_DoesNotCallProvider()
    {
        EnableAi();
        // 直近1時間は2件(上限3未満)だが、24時間では10件(上限10)
        SeedUsage(minutesAgo: [10, 20, 120, 180, 240, 300, 360, 420, 480, 540]);
        var incident = AddIncident();

        var result = await CreateSut().DiagnoseAsync(incident, "log");

        Assert.Equal(AiSkipReason.LimitReached, result.SkipReason);
        Assert.Contains("1日", result.Message);
        Assert.Empty(_provider.Calls);
    }

    [Fact]
    public async Task Diagnose_MonthlyLimitReached_DoesNotCallProvider()
    {
        EnableAi();
        // 直近1時間・1日は上限未満だが、30日では80件(上限80)
        var minutes = Enumerable.Range(0, 80).Select(i => 1500 + i * 300).ToArray();
        SeedUsage(minutes);
        var incident = AddIncident();

        var result = await CreateSut().DiagnoseAsync(incident, "log");

        Assert.Equal(AiSkipReason.LimitReached, result.SkipReason);
        Assert.Contains("月", result.Message);
        Assert.Empty(_provider.Calls);
    }

    [Fact]
    public async Task Diagnose_UnderAllLimits_CallsProvider()
    {
        EnableAi();
        SeedUsage(minutesAgo: [10, 200]);
        var incident = AddIncident();

        await CreateSut().DiagnoseAsync(incident, "log");

        Assert.Single(_provider.Calls);
    }

    /// <summary>指定した「何分前」に呼び出し履歴を作る。</summary>
    private void SeedUsage(params int[] minutesAgo)
    {
        foreach (var minutes in minutesAgo)
        {
            _usage.Records.Add(new AiUsageRecord
            {
                Id = _usage.Records.Count + 1,
                CalledAt = BaseTime.UtcDateTime.AddMinutes(-minutes),
                Provider = "stub",
                Model = "test",
                Result = AiCallResult.Success,
                InputCharacters = 100,
            });
        }
    }

    [Fact]
    public async Task Diagnose_Success_SavesAiSourcedDiagnosis()
    {
        EnableAi();
        _provider.Response = new AiCallResponse(AiCallOutcome.Success, new AiDiagnosisOutput
        {
            Classification = "ContainerStopped",
            Severity = IncidentSeverity.High,
            Rationale = "コンテナが停止しています。",
            RecommendedActionId = RecoveryActionCatalog.RestartAllowedContainer,
            Confidence = 0.9,
        }, OutputTokens: 120, LatencyMs: 800);
        var incident = AddIncident();

        var result = await CreateSut().DiagnoseAsync(incident, "log");

        Assert.NotNull(result.Diagnosis);
        Assert.Equal(DiagnosisSource.Ai, result.Diagnosis.Source);
        Assert.Equal(RecoveryActionCatalog.RestartAllowedContainer, result.Diagnosis.RecommendedActionId);
        Assert.True(result.Diagnosis.RecommendedActionAllowed);
        Assert.Contains("確信度", result.Diagnosis.Rationale);
    }

    [Fact]
    public async Task Diagnose_ActionNotAllowedForTarget_IsStrippedFromDiagnosis()
    {
        // web-siteテンプレートは再起動を許可していない
        _targets.Targets[0].TemplateId = "web-site";
        EnableAi();
        _provider.Response = new AiCallResponse(AiCallOutcome.Success, new AiDiagnosisOutput
        {
            Classification = "ContainerStopped",
            Severity = IncidentSeverity.High,
            Rationale = "コンテナが停止しています。",
            RecommendedActionId = RecoveryActionCatalog.RestartAllowedContainer,
        });
        var incident = AddIncident();

        var result = await CreateSut().DiagnoseAsync(incident, "log");

        Assert.NotNull(result.Diagnosis);
        // AIの推奨をそのまま信用せず、対象能力で再検証して落とす
        Assert.False(result.Diagnosis.RecommendedActionAllowed);
        Assert.Null(result.Diagnosis.RecommendedActionId);
    }

    [Theory]
    [InlineData(AiCallOutcome.RateLimited, AiSkipReason.LimitReached)]
    [InlineData(AiCallOutcome.Timeout, AiSkipReason.ProviderFailure)]
    [InlineData(AiCallOutcome.Error, AiSkipReason.ProviderFailure)]
    [InlineData(AiCallOutcome.InvalidResponse, AiSkipReason.InvalidResponse)]
    public async Task Diagnose_ProviderFailure_DegradesWithoutDiagnosis(
        AiCallOutcome outcome, AiSkipReason expectedReason)
    {
        EnableAi();
        _provider.Response = new AiCallResponse(outcome, ErrorSummary: "失敗");
        var incident = AddIncident();

        var result = await CreateSut().DiagnoseAsync(incident, "log");

        // 縮退: 診断を作らない = 復旧も開始されない
        Assert.Null(result.Diagnosis);
        Assert.Equal(expectedReason, result.SkipReason);
        Assert.Empty(_diagnoses.Diagnoses);
    }

    [Fact]
    public async Task Diagnose_RecordsUsageWithoutStoringPromptOrResponse()
    {
        EnableAi();
        _provider.Response = new AiCallResponse(AiCallOutcome.Success, new AiDiagnosisOutput
        {
            Classification = "X",
            Severity = IncidentSeverity.Low,
            Rationale = "理由",
        }, OutputTokens: 50, LatencyMs: 300);
        var incident = AddIncident();

        await CreateSut().DiagnoseAsync(incident, "password=hunter2 at 192.168.1.10");

        var record = Assert.Single(_usage.Records);
        Assert.Equal(AiCallResult.Success, record.Result);
        Assert.Equal(50, record.OutputTokens);
        // 記録に本文は残さない(文字数のみ)
        Assert.True(record.InputCharacters > 0);
        Assert.DoesNotContain("hunter2", System.Text.Json.JsonSerializer.Serialize(record));
        Assert.DoesNotContain("192.168.1.10", System.Text.Json.JsonSerializer.Serialize(record));
    }

    [Fact]
    public async Task Diagnose_SendsAnonymizedInputToProvider()
    {
        EnableAi();
        var incident = AddIncident();

        await CreateSut().DiagnoseAsync(incident, "error at 192.168.1.10 password=hunter2");

        var call = Assert.Single(_provider.Calls);
        Assert.DoesNotContain("192.168.1.10", call.LogExcerpt);
        Assert.DoesNotContain("hunter2", call.LogExcerpt);
        // 許可アクションのみを候補として渡す
        Assert.Contains(RecoveryActionCatalog.RestartAllowedContainer, call.AllowedActionIds);
    }

    private sealed class StubAiProvider : IAiDiagnosisProvider
    {
        public string ProviderName => "stub";

        public List<AiDiagnosisInput> Calls { get; } = [];

        public AiCallResponse Response { get; set; } =
            new(AiCallOutcome.Success, new AiDiagnosisOutput
            {
                Classification = "X",
                Severity = IncidentSeverity.Low,
                Rationale = "理由",
            });

        public Task<AiCallResponse> DiagnoseAsync(
            AiDiagnosisInput input, AiUsageLimit limits, string apiKey, CancellationToken ct = default)
        {
            Calls.Add(input);
            return Task.FromResult(Response);
        }
    }
}

public class FakeAiUsageLimitRepository : IAiUsageLimitRepository
{
    public AiUsageLimit? Limit { get; set; }

    public Task<AiUsageLimit?> GetAsync(CancellationToken ct = default) => Task.FromResult(Limit);

    public Task AddAsync(AiUsageLimit limit, CancellationToken ct = default)
    {
        Limit = limit;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeAiUsageRecordRepository : IAiUsageRecordRepository
{
    public List<AiUsageRecord> Records { get; } = [];

    public Task AddAsync(AiUsageRecord record, CancellationToken ct = default)
    {
        record.Id = Records.Count + 1;
        Records.Add(record);
        return Task.CompletedTask;
    }

    public Task<int> CountSinceAsync(DateTime sinceUtc, CancellationToken ct = default) =>
        Task.FromResult(Records.Count(r => r.CalledAt >= sinceUtc));

    public Task<List<AiUsageRecord>> GetRecentAsync(int limit, CancellationToken ct = default) =>
        Task.FromResult(Records.OrderByDescending(r => r.CalledAt).Take(limit).ToList());

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeAiApiKeyProvider : IAiApiKeyProvider
{
    public string? ApiKey { get; set; } = "test-api-key";

    public Task<string?> GetApiKeyAsync(CancellationToken ct = default) => Task.FromResult(ApiKey);
}
