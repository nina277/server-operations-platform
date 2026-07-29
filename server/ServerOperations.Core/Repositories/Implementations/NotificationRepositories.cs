using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Repositories.Implementations;

public class NotificationRepository(AppDbContext db) : INotificationRepository
{
    public Task<Notification?> FindRecentByAggregationKeyAsync(
        string aggregationKey, DateTime notifiedAfterUtc, CancellationToken ct = default) =>
        db.Notifications
            .Where(n => n.AggregationKey == aggregationKey && n.LastNotifiedAt >= notifiedAfterUtc)
            .OrderByDescending(n => n.LastNotifiedAt)
            .FirstOrDefaultAsync(ct);

    public Task<Notification?> FindByIdAsync(long id, CancellationToken ct = default) =>
        db.Notifications.Include(n => n.Deliveries).FirstOrDefaultAsync(n => n.Id == id, ct);

    public async Task<(List<Notification> Items, long TotalCount)> SearchAsync(
        bool? isRead, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Notifications.AsQueryable();
        if (isRead is { } read)
        {
            query = query.Where(n => n.IsRead == read);
        }

        var total = await query.LongCountAsync(ct);
        var normalizedPage = Math.Max(page, 1);
        var normalizedSize = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderByDescending(n => n.LastNotifiedAt)
            .Skip((normalizedPage - 1) * normalizedSize)
            .Take(normalizedSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<int> CountUnreadAsync(CancellationToken ct = default) =>
        db.Notifications.CountAsync(n => !n.IsRead, ct);

    public async Task AddAsync(Notification notification, CancellationToken ct = default) =>
        await db.Notifications.AddAsync(notification, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public class DeviceTokenRepository(AppDbContext db) : IDeviceTokenRepository
{
    public Task<List<DeviceToken>> GetActiveAsync(CancellationToken ct = default) =>
        db.DeviceTokens.Where(t => t.IsActive && t.RevokedAt == null).ToListAsync(ct);

    public Task<List<DeviceToken>> GetByUserAsync(long userId, CancellationToken ct = default) =>
        db.DeviceTokens.Where(t => t.UserId == userId).OrderByDescending(t => t.CreatedAt).ToListAsync(ct);

    public Task<DeviceToken?> FindByTokenAsync(string token, CancellationToken ct = default) =>
        db.DeviceTokens.FirstOrDefaultAsync(t => t.Token == token, ct);

    public Task<DeviceToken?> FindByIdAsync(long id, CancellationToken ct = default) =>
        db.DeviceTokens.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task AddAsync(DeviceToken token, CancellationToken ct = default) =>
        await db.DeviceTokens.AddAsync(token, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
