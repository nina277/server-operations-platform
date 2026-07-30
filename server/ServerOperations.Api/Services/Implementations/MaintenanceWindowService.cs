using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

/// <summary>
/// メンテナンス期間。計画停止中の通知と自動復旧を止める。
///
/// 期間の登録と取り消しは、自動復旧の挙動を変える操作にあたるため必ず監査に残す。
/// 「なぜ自動復旧が動かなかったのか」を後から説明できる必要がある。
/// </summary>
public class MaintenanceWindowService(
    IMaintenanceWindowRepository windows,
    IMonitoringTargetRepository targets,
    IAuditService audit,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : IMaintenanceWindowService
{
    /// <summary>1回の期間の上限。長すぎる抑止は事故のもとになる。</summary>
    public const int MaxDurationDays = 30;

    public async Task<List<MaintenanceWindowDto>> GetUpcomingAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var items = await windows.GetUpcomingAsync(now, ct);

        // 対象名を引くため、必要なものだけまとめて取る
        var names = await TargetNamesAsync(items.Select(w => w.TargetId), ct);

        return items.Select(w => ToDto(w, names.GetValueOrDefault(w.TargetId ?? 0), now)).ToList();
    }

    public async Task<MaintenanceWindowDto> CreateAsync(
        CreateMaintenanceWindowRequest request, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (request.EndsAt <= request.StartsAt)
        {
            throw AppException.BadRequest(
                "invalid_maintenance_range", "終了日時は開始日時より後にしてください。");
        }

        if ((request.EndsAt - request.StartsAt).TotalDays > MaxDurationDays)
        {
            throw AppException.BadRequest(
                "maintenance_too_long",
                $"1件のメンテナンス期間は最長{MaxDurationDays}日です。");
        }

        if (request.EndsAt <= now)
        {
            throw AppException.BadRequest(
                "maintenance_in_past", "すでに終了した期間は登録できません。");
        }

        // どちらも止めない期間は、登録しても何も起きない。
        // 設定したのに効かない状態を作らないため、保存前に弾く。
        if (!request.SuppressNotifications && !request.SuppressAutoRecovery)
        {
            throw AppException.BadRequest(
                "maintenance_no_effect",
                "通知と自動復旧の少なくとも一方を止める設定にしてください。");
        }

        string? targetName = null;
        if (request.TargetId is { } targetId)
        {
            var target = await targets.FindByIdAsync(targetId, ct);
            if (target is null)
            {
                throw AppException.NotFound("target_not_found", "監視対象が見つかりません。");
            }

            targetName = target.Name;
        }

        var reason = request.Reason.Trim();
        if (reason.Length == 0)
        {
            throw AppException.BadRequest(
                "maintenance_reason_required", "メンテナンスの理由を入力してください。");
        }

        var window = new MaintenanceWindow
        {
            TargetId = request.TargetId,
            Reason = reason,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            SuppressNotifications = request.SuppressNotifications,
            SuppressAutoRecovery = request.SuppressAutoRecovery,
            CreatedByUserId = currentUser.UserId,
            CreatedAt = now,
        };

        await windows.AddAsync(window, ct);
        await windows.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "maintenance.create", "MaintenanceWindow", window.Id.ToString(), AuditResult.Success,
            details: Describe(window), ct: ct);

        return ToDto(window, targetName, now);
    }

    public async Task<MaintenanceWindowDto> CancelAsync(long id, CancellationToken ct = default)
    {
        var window = await windows.FindByIdAsync(id, ct);
        if (window is null)
        {
            throw AppException.NotFound("maintenance_not_found", "メンテナンス期間が見つかりません。");
        }

        if (window.CancelledAt is not null)
        {
            throw AppException.Conflict(
                "maintenance_already_cancelled", "すでに取り消されています。");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        window.CancelledAt = now;
        await windows.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "maintenance.cancel", "MaintenanceWindow", window.Id.ToString(), AuditResult.Success,
            details: Describe(window), ct: ct);

        var names = await TargetNamesAsync([window.TargetId], ct);
        return ToDto(window, names.GetValueOrDefault(window.TargetId ?? 0), now);
    }

    private async Task<Dictionary<long, string>> TargetNamesAsync(
        IEnumerable<long?> targetIds, CancellationToken ct)
    {
        var ids = targetIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToHashSet();
        if (ids.Count == 0)
        {
            return [];
        }

        var all = await targets.GetAllAsync(ct);
        return all.Where(t => ids.Contains(t.Id)).ToDictionary(t => t.Id, t => t.Name);
    }

    private static string Describe(MaintenanceWindow w) =>
        $"target={w.TargetId?.ToString() ?? "all"} from={w.StartsAt:O} to={w.EndsAt:O} "
        + $"notifications={w.SuppressNotifications} autoRecovery={w.SuppressAutoRecovery} "
        + $"cancelled={w.CancelledAt?.ToString("O") ?? "-"}";

    private static MaintenanceWindowDto ToDto(
        MaintenanceWindow w, string? targetName, DateTime now) => new()
    {
        Id = w.Id,
        TargetId = w.TargetId,
        TargetName = targetName,
        Reason = w.Reason,
        StartsAt = w.StartsAt,
        EndsAt = w.EndsAt,
        SuppressNotifications = w.SuppressNotifications,
        SuppressAutoRecovery = w.SuppressAutoRecovery,
        CancelledAt = w.CancelledAt,
        IsActive = w.IsActiveAt(now),
        CreatedAt = w.CreatedAt,
    };
}
