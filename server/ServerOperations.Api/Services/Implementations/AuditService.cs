using System.Diagnostics;
using ServerOperations.Api.Models.Auth;
using ServerOperations.Api.Repositories.Interfaces;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

public class AuditService(
    IAuditLogRepository repository,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider,
    ILogger<AuditService> logger) : IAuditService
{
    public async Task RecordAsync(
        string action,
        string targetType,
        string? targetId,
        AuditResult result,
        long? actorUserId = null,
        string? actorName = null,
        string? details = null,
        CancellationToken ct = default)
    {
        var context = httpContextAccessor.HttpContext;

        var entry = new AuditLog
        {
            OccurredAt = timeProvider.GetUtcNow().UtcDateTime,
            ActorUserId = actorUserId,
            ActorName = actorName,
            IpAddress = context?.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            UserAgent = Truncate(context?.Request.Headers.UserAgent.ToString(), 512) ?? "unknown",
            TargetType = targetType,
            TargetId = targetId,
            Action = action,
            Result = result,
            Details = Truncate(details, 2000),
            TraceId = Activity.Current?.Id ?? context?.TraceIdentifier,
        };

        try
        {
            await repository.AddAsync(entry, ct);
            await repository.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // 監査の書き込み失敗で業務処理を止めないが、必ずアプリログには残す
            logger.LogError(ex,
                "Failed to persist audit log. action={Action} target={TargetType}/{TargetId} result={Result}",
                action, targetType, targetId, result);
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= maxLength ? value : value[..maxLength];
}
