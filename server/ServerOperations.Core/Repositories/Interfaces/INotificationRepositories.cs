using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Repositories.Interfaces;

public interface INotificationRepository
{
    /// <summary>
    /// 同一集約キーで、指定時刻以降に通知済みの通知を検索する(再通知の抑止用)。
    /// </summary>
    Task<Notification?> FindRecentByAggregationKeyAsync(
        string aggregationKey, DateTime notifiedAfterUtc, CancellationToken ct = default);

    Task<Notification?> FindByIdAsync(long id, CancellationToken ct = default);

    Task<(List<Notification> Items, long TotalCount)> SearchAsync(
        bool? isRead, int page, int pageSize, CancellationToken ct = default);

    Task<int> CountUnreadAsync(CancellationToken ct = default);

    Task AddAsync(Notification notification, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IDeviceTokenRepository
{
    Task<List<DeviceToken>> GetActiveAsync(CancellationToken ct = default);

    Task<List<DeviceToken>> GetByUserAsync(long userId, CancellationToken ct = default);

    Task<DeviceToken?> FindByTokenAsync(string token, CancellationToken ct = default);

    Task<DeviceToken?> FindByIdAsync(long id, CancellationToken ct = default);

    Task AddAsync(DeviceToken token, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
