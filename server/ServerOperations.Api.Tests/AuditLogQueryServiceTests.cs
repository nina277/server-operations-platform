using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.Services.Implementations;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Tests;

public class AuditLogQueryServiceTests
{
    private static readonly DateTime BaseTime = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeAuditLogRepository _auditLogs = new();
    private readonly FakeAuditService _audit = new();

    private AuditLogQueryService CreateService() => new(_auditLogs, _audit);

    private void Seed(
        string actorName,
        string action,
        string targetType,
        AuditResult result,
        int minutesAgo)
    {
        _auditLogs.AddAsync(new AuditLog
        {
            OccurredAt = BaseTime.AddMinutes(-minutesAgo),
            ActorUserId = 1,
            ActorName = actorName,
            IpAddress = "192.0.2.10",
            UserAgent = "Mozilla/5.0",
            TargetType = targetType,
            TargetId = "1",
            Action = action,
            Result = result,
            Details = "詳細",
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task SearchAsync_新しい順に返す()
    {
        Seed("admin", "auth.login", "User", AuditResult.Success, minutesAgo: 30);
        Seed("admin", "recovery.execute", "RecoveryAction", AuditResult.Success, minutesAgo: 10);

        var result = await CreateService().SearchAsync(new AuditLogFilter(), new PagingQuery());

        Assert.Equal(2, result.TotalCount);
        Assert.Equal("recovery.execute", result.Items[0].Action);
        Assert.Equal("auth.login", result.Items[1].Action);
    }

    [Fact]
    public async Task SearchAsync_操作者名は部分一致で絞り込める()
    {
        Seed("operator-admin", "auth.login", "User", AuditResult.Success, minutesAgo: 10);
        Seed("viewer", "auth.login", "User", AuditResult.Success, minutesAgo: 5);

        var result = await CreateService()
            .SearchAsync(new AuditLogFilter { ActorName = "admin" }, new PagingQuery());

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("operator-admin", result.Items[0].ActorName);
    }

    [Fact]
    public async Task SearchAsync_操作と結果で絞り込める()
    {
        Seed("admin", "auth.login", "User", AuditResult.Success, minutesAgo: 30);
        Seed("admin", "auth.login", "User", AuditResult.Failure, minutesAgo: 20);
        Seed("admin", "recovery.execute", "RecoveryAction", AuditResult.Failure, minutesAgo: 10);

        var service = CreateService();

        var byAction = await service.SearchAsync(
            new AuditLogFilter { Action = "auth.login" }, new PagingQuery());
        Assert.Equal(2, byAction.TotalCount);

        var byResult = await service.SearchAsync(
            new AuditLogFilter { Result = AuditResult.Failure }, new PagingQuery());
        Assert.Equal(2, byResult.TotalCount);

        var both = await service.SearchAsync(
            new AuditLogFilter { Action = "auth.login", Result = AuditResult.Failure },
            new PagingQuery());
        Assert.Equal(1, both.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_期間で絞り込める()
    {
        Seed("admin", "auth.login", "User", AuditResult.Success, minutesAgo: 120);
        Seed("admin", "auth.login", "User", AuditResult.Success, minutesAgo: 30);
        Seed("admin", "auth.login", "User", AuditResult.Success, minutesAgo: 5);

        var result = await CreateService().SearchAsync(
            new AuditLogFilter
            {
                OccurredFromUtc = BaseTime.AddMinutes(-60),
                OccurredToUtc = BaseTime.AddMinutes(-10),
            },
            new PagingQuery());

        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_ページングする()
    {
        for (var i = 0; i < 25; i++)
        {
            Seed("admin", "auth.login", "User", AuditResult.Success, minutesAgo: i);
        }

        var page2 = await CreateService()
            .SearchAsync(new AuditLogFilter(), new PagingQuery { Page = 2, PageSize = 10 });

        Assert.Equal(25, page2.TotalCount);
        Assert.Equal(10, page2.Items.Count);
        Assert.Equal(2, page2.Page);
        Assert.Equal(3, page2.TotalPages);
    }

    [Fact]
    public async Task SearchAsync_ページ数の指定が範囲外でも既定値へ寄せる()
    {
        Seed("admin", "auth.login", "User", AuditResult.Success, minutesAgo: 1);

        var result = await CreateService()
            .SearchAsync(new AuditLogFilter(), new PagingQuery { Page = 0, PageSize = 1000 });

        Assert.Equal(1, result.Page);
        Assert.Equal(PagingQuery.MaxPageSize, result.PageSize);
    }

    [Fact]
    public async Task SearchAsync_操作者IPとUserAgentを必ず返す()
    {
        Seed("admin", "auth.login", "User", AuditResult.Success, minutesAgo: 1);

        var result = await CreateService().SearchAsync(new AuditLogFilter(), new PagingQuery());

        var item = result.Items[0];
        Assert.Equal("admin", item.ActorName);
        Assert.Equal("192.0.2.10", item.IpAddress);
        Assert.Equal("Mozilla/5.0", item.UserAgent);
        Assert.Equal("User", item.TargetType);
        Assert.Equal("Success", item.Result);
    }

    [Fact]
    public async Task GetFilterOptionsAsync_記録済みの種別と操作を返す()
    {
        Seed("admin", "recovery.execute", "RecoveryAction", AuditResult.Success, minutesAgo: 10);
        Seed("admin", "auth.login", "User", AuditResult.Success, minutesAgo: 5);
        Seed("admin", "auth.login", "User", AuditResult.Failure, minutesAgo: 1);

        var options = await CreateService().GetFilterOptionsAsync();

        Assert.Equal(["RecoveryAction", "User"], options.TargetTypes);
        Assert.Equal(["auth.login", "recovery.execute"], options.Actions);
        Assert.Equal(["Success", "Failure", "Denied"], options.Results);
    }
}
