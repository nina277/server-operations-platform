using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services.Notifications;

public record ChannelTestResult
{
    public required string Channel { get; init; }

    public required bool Success { get; init; }

    /// <summary>設定が無いなどで送らなかった場合はtrue。失敗とは区別する。</summary>
    public required bool Skipped { get; init; }

    public string? Message { get; init; }
}

public interface INotificationTestService
{
    /// <summary>
    /// 保存済みの設定と宛先へテスト通知を送り、チャネルごとの結果を返す。
    /// </summary>
    Task<List<ChannelTestResult>> SendTestAsync(CancellationToken ct = default);
}

/// <summary>
/// 通知のテスト送信。設定が正しいかを、本物の障害を待たずに確かめられるようにする。
///
/// 宛先は保存済みの設定からのみ取る。呼び出し側から宛先を渡せるようにすると
/// 任意の相手へメールを送れる踏み台になるため、そのような口は用意しない。
///
/// 通知としては保存しない。テストは診断であって運用上の出来事ではなく、
/// 通知一覧に混ざると未読件数や集約の判断を狂わせるため。
/// </summary>
public class NotificationTestService(
    IEnumerable<INotificationChannelSender> channels,
    TimeProvider timeProvider) : INotificationTestService
{
    public async Task<List<ChannelTestResult>> SendTestAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // 保存しない一時的な通知。重大度の下限やメンテナンス期間の抑止は
        // 通さない(設定そのものを確かめるのが目的のため)。
        var probe = new Notification
        {
            Severity = NotificationSeverity.Low,
            Title = "テスト通知",
            Body = "通知設定の確認のために送信しました。この通知に対応は不要です。",
            OccurrenceCount = 1,
            FirstNotifiedAt = now,
            LastNotifiedAt = now,
        };

        var results = new List<ChannelTestResult>();

        // 1つが失敗しても他のチャネルは試す(どれが駄目なのかを知りたいため)
        foreach (var channel in channels)
        {
            try
            {
                var result = await channel.SendAsync(probe, ct);
                results.Add(new ChannelTestResult
                {
                    Channel = channel.Channel.ToString(),
                    Success = result.Success,
                    Skipped = result.Skipped,
                    Message = Truncate(result.ErrorMessage, 500),
                });
            }
            catch (Exception)
            {
                // 例外の詳細には接続先やホスト名が入りうるため、そのままは返さない
                results.Add(new ChannelTestResult
                {
                    Channel = channel.Channel.ToString(),
                    Success = false,
                    Skipped = false,
                    Message = "送信中にエラーが発生しました。設定を確認してください。",
                });
            }
        }

        return results;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
