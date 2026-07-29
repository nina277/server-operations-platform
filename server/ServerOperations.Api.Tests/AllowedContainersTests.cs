using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

public class AllowedContainersTests
{
    private static MonitoringTarget Target(string json) => new()
    {
        Id = 1,
        Name = "t1",
        TemplateId = "docker-host",
        AllowedContainersJson = json,
    };

    [Fact]
    public void Parse_EmptyOrMissing_ReturnsNothingAllowed()
    {
        Assert.Empty(AllowedContainers.Parse(Target("[]")));
        Assert.Empty(AllowedContainers.Parse(Target(string.Empty)));
    }

    [Fact]
    public void Parse_MalformedJson_TreatedAsNothingAllowed()
    {
        // 壊れた設定で操作が通ってしまわないこと
        Assert.Empty(AllowedContainers.Parse(Target("{ not json")));
    }

    [Fact]
    public void Serialize_TrimsDeduplicatesAndSorts()
    {
        var json = AllowedContainers.Serialize([" web ", "api", "web", string.Empty, "  "]);

        var parsed = AllowedContainers.Parse(Target(json));
        Assert.Equal(["api", "web"], parsed);
    }

    [Fact]
    public void IsAllowed_OnlyListedContainers()
    {
        var target = Target(AllowedContainers.Serialize(["web", "api"]));

        Assert.True(AllowedContainers.IsAllowed(target, "web"));
        Assert.True(AllowedContainers.IsAllowed(target, "api"));
        Assert.False(AllowedContainers.IsAllowed(target, "mysql"));
    }

    [Fact]
    public void IsAllowed_IsCaseSensitive()
    {
        // コンテナ名は大文字小文字を区別する(Dockerの挙動に合わせ、曖昧一致で広げない)
        var target = Target(AllowedContainers.Serialize(["web"]));

        Assert.False(AllowedContainers.IsAllowed(target, "WEB"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAllowed_EmptyName_IsRejected(string? name)
    {
        var target = Target(AllowedContainers.Serialize(["web"]));

        Assert.False(AllowedContainers.IsAllowed(target, name));
    }
}
