using ServerOperations.Core.Models.Auth;

namespace ServerOperations.Api.Services.Interfaces;

/// <summary>
/// Service層から監査ログを記録するヘルパー。IP・User-Agent・traceIdは現在のHTTPコンテキストから自動付与する。
/// </summary>
public interface IAuditService
{
    Task RecordAsync(
        string action,
        string targetType,
        string? targetId,
        AuditResult result,
        long? actorUserId = null,
        string? actorName = null,
        string? details = null,
        CancellationToken ct = default);
}
