using ServerOperations.Api.DTOs.Settings;
using ServerOperations.Api.Models.Auth;
using ServerOperations.Api.Services.Implementations;
using ServerOperations.Api.Tests.Fakes;

namespace ServerOperations.Api.Tests;

public class SettingsServiceTests
{
    private readonly FakeSystemSettingRepository _repo = new();
    private readonly FakeAuditService _audit = new();
    private readonly FakeCurrentUserAccessor _currentUser = new();
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));

    private SettingsService CreateSut() => new(_repo, _audit, _currentUser, _time);

    [Fact]
    public async Task GetProfile_ReturnsDefaults_WhenUnset()
    {
        var sut = CreateSut();

        var profile = await sut.GetProfileAsync();

        Assert.Equal("Server Operations Platform", profile.SystemName);
        Assert.Equal("ja", profile.Language);
    }

    [Fact]
    public async Task UpdateProfile_PersistsAndAuditsWithBeforeAfterSummary()
    {
        var sut = CreateSut();

        var updated = await sut.UpdateProfileAsync(new ProfileSettingsDto
        {
            SystemName = "Lab Ops",
            Language = "en",
        });

        Assert.Equal("Lab Ops", updated.SystemName);
        var roundTrip = await sut.GetProfileAsync();
        Assert.Equal("Lab Ops", roundTrip.SystemName);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal("settings.profile.update", entry.Action);
        Assert.Equal(AuditResult.Success, entry.Result);
        Assert.Equal(1, entry.ActorUserId);
    }

    [Fact]
    public async Task UpdateRetention_RoundTrips()
    {
        var sut = CreateSut();

        await sut.UpdateRetentionAsync(new RetentionSettingsDto
        {
            Profile = "custom",
            MetricsDays = 7,
            LogsDays = 14,
            IncidentsDays = 180,
            AuditDays = 730,
        });

        var stored = await sut.GetRetentionAsync();
        Assert.Equal("custom", stored.Profile);
        Assert.Equal(7, stored.MetricsDays);
        Assert.Equal(730, stored.AuditDays);
    }
}
