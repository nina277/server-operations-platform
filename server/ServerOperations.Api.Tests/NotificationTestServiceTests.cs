using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services.Notifications;

namespace ServerOperations.Api.Tests;

/// <summary>
/// 通知のテスト送信。設定が正しいかを本物の障害を待たずに確かめるためのもの。
/// 宛先を指定できないこと、通知として保存されないことが要点。
/// </summary>
public class NotificationTestServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly TestTimeProvider _time = new(BaseTime);

    private NotificationTestService CreateSut(params INotificationChannelSender[] channels) =>
        new(channels, _time);

    [Fact]
    public async Task チャネルごとの結果を返す()
    {
        var email = new StubChannel { Channel = NotificationChannel.Email };
        var push = new StubChannel { Channel = NotificationChannel.Push };

        var results = await CreateSut(email, push).SendTestAsync();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task 未設定のチャネルは失敗ではなく見送りとして返す()
    {
        // 「設定していないから送らなかった」を失敗として出すと、
        // 直すべき問題があるように見えてしまう
        var channel = new StubChannel
        {
            Result = ChannelSendResult.Skip("メール通知は無効です。"),
        };

        var results = await CreateSut(channel).SendTestAsync();

        var result = Assert.Single(results);
        Assert.True(result.Skipped);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task 一つのチャネルが失敗しても他のチャネルは試す()
    {
        // どれが駄目なのかを知るのがテスト送信の目的
        var failing = new StubChannel
        {
            Channel = NotificationChannel.Email,
            Result = ChannelSendResult.Fail("接続できません。"),
        };
        var working = new StubChannel { Channel = NotificationChannel.Push };

        var results = await CreateSut(failing, working).SendTestAsync();

        Assert.False(results[0].Success);
        Assert.True(results[1].Success);
        Assert.Single(working.Sent);
    }

    [Fact]
    public async Task 例外が出ても他のチャネルは試す()
    {
        var throwing = new ThrowingStubChannel();
        var working = new StubChannel { Channel = NotificationChannel.Push };

        var results = await CreateSut(throwing, working).SendTestAsync();

        Assert.False(results[0].Success);
        Assert.True(results[1].Success);
    }

    [Fact]
    public async Task 例外の詳細はそのまま返さない()
    {
        // 例外の本文には接続先やホスト名が入りうる
        var throwing = new ThrowingStubChannel
        {
            Error = new InvalidOperationException("smtp.internal.example.com:587 へ接続できません"),
        };

        var results = await CreateSut(throwing).SendTestAsync();

        Assert.DoesNotContain("smtp.internal", Assert.Single(results).Message ?? string.Empty);
    }

    [Fact]
    public async Task テスト通知は対象を持たない()
    {
        // 対象を持つとメンテナンス期間の抑止に引っかかりうる。
        // 設定そのものを確かめるのが目的のため、抑止の判定を通さない。
        var channel = new StubChannel();

        await CreateSut(channel).SendTestAsync();

        var sent = Assert.Single(channel.Sent);
        Assert.Null(sent.TargetId);
        Assert.Null(sent.IncidentId);
    }

    [Fact]
    public async Task テスト通知は集約キーを持たない()
    {
        // 集約キーを持つと、以後の本物の通知が「集約済み」として抑止される
        var channel = new StubChannel();

        await CreateSut(channel).SendTestAsync();

        Assert.Null(Assert.Single(channel.Sent).AggregationKey);
    }

    [Fact]
    public async Task テスト通知だと分かる本文にする()
    {
        var channel = new StubChannel();

        await CreateSut(channel).SendTestAsync();

        var sent = Assert.Single(channel.Sent);
        Assert.Contains("テスト", sent.Title);
        Assert.Contains("対応は不要", sent.Body);
    }

    private sealed class StubChannel : INotificationChannelSender
    {
        public NotificationChannel Channel { get; set; } = NotificationChannel.Email;

        public ChannelSendResult Result { get; set; } = ChannelSendResult.Ok();

        public List<Notification> Sent { get; } = [];

        public Task<ChannelSendResult> SendAsync(Notification notification, CancellationToken ct = default)
        {
            Sent.Add(notification);
            return Task.FromResult(Result);
        }
    }

    private sealed class ThrowingStubChannel : INotificationChannelSender
    {
        public NotificationChannel Channel => NotificationChannel.Email;

        public Exception Error { get; set; } = new InvalidOperationException("送信に失敗しました");

        public Task<ChannelSendResult> SendAsync(
            Notification notification, CancellationToken ct = default) => throw Error;
    }
}
