using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Services;

/// <summary>抑止の判定結果。抑止した場合は理由を残し、監査と画面で説明できるようにする。</summary>
public record MaintenanceState
{
    public bool SuppressNotifications { get; init; }

    public bool SuppressAutoRecovery { get; init; }

    /// <summary>効いている期間の理由(最初の1件)。抑止していないならnull。</summary>
    public string? Reason { get; init; }

    public static readonly MaintenanceState None = new();
}

public interface IMaintenanceService
{
    /// <summary>指定対象がいまメンテナンス期間中かを判定する。</summary>
    Task<MaintenanceState> GetStateAsync(long targetId, CancellationToken ct = default);
}

/// <summary>
/// メンテナンス期間の判定。計画停止中に通知と自動復旧を止める。
///
/// 検知そのものは止めない。期間中に起きたことはインシデントとして記録し、
/// 「知らせない」「勝手に直さない」だけを制御する。
/// 記録まで止めると、計画停止をまたいで続いた障害を後から追えなくなるため。
/// </summary>
public class MaintenanceService(
    IMaintenanceWindowRepository windows,
    TimeProvider timeProvider) : IMaintenanceService
{
    public async Task<MaintenanceState> GetStateAsync(long targetId, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var active = await windows.GetActiveForTargetAsync(targetId, now, ct);

        if (active.Count == 0)
        {
            return MaintenanceState.None;
        }

        // 期間が重なっている場合は、どれか1つでも止めていれば止める
        var suppressNotifications = active.Any(w => w.SuppressNotifications);
        var suppressAutoRecovery = active.Any(w => w.SuppressAutoRecovery);

        if (!suppressNotifications && !suppressAutoRecovery)
        {
            return MaintenanceState.None;
        }

        return new MaintenanceState
        {
            SuppressNotifications = suppressNotifications,
            SuppressAutoRecovery = suppressAutoRecovery,
            Reason = FirstReason(active, suppressNotifications, suppressAutoRecovery),
        };
    }

    private static string? FirstReason(
        List<MaintenanceWindow> active, bool suppressNotifications, bool suppressAutoRecovery) =>
        active.FirstOrDefault(w =>
                (suppressNotifications && w.SuppressNotifications)
                || (suppressAutoRecovery && w.SuppressAutoRecovery))
            ?.Reason;
}
