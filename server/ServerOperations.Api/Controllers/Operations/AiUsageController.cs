using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Controllers.Operations;

[ApiController]
[Route("api/v1/ai-usage")]
[Authorize]
public class AiUsageController(
    IAiUsageLimitRepository limits,
    IAiUsageRecordRepository usageRecords,
    IAuditService audit,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<AiUsageSummaryDto>>> GetSummary(CancellationToken ct)
    {
        var limit = await GetOrCreateLimitAsync(ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var summary = new AiUsageSummaryDto
        {
            IsEnabled = limit.IsEnabled,
            Provider = limit.Provider,
            Model = limit.Model,
            HourlyUsed = await usageRecords.CountSinceAsync(now.AddHours(-1), ct),
            HourlyLimit = limit.HourlyLimit,
            DailyUsed = await usageRecords.CountSinceAsync(now.AddDays(-1), ct),
            DailyLimit = limit.DailyLimit,
            MonthlyUsed = await usageRecords.CountSinceAsync(now.AddDays(-30), ct),
            MonthlyLimit = limit.MonthlyLimit,
            MaxInputCharacters = limit.MaxInputCharacters,
            MaxOutputTokens = limit.MaxOutputTokens,
            RecentCalls = (await usageRecords.GetRecentAsync(20, ct))
                .Select(AiUsageRecordDto.From).ToList(),
        };

        return Ok(ApiResponse<AiUsageSummaryDto>.Ok(summary, TraceId()));
    }

    [HttpPut("limits")]
    [Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
    public async Task<ActionResult<ApiResponse<AiUsageSummaryDto>>> UpdateLimits(
        [FromBody] UpdateAiLimitsRequest request, CancellationToken ct)
    {
        var limit = await GetOrCreateLimitAsync(ct);
        var before = $"monthly={limit.MonthlyLimit} daily={limit.DailyLimit} hourly={limit.HourlyLimit} "
            + $"maxInput={limit.MaxInputCharacters} maxOutput={limit.MaxOutputTokens} model={limit.Model}";

        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            limit.Model = request.Model.Trim();
        }

        limit.MonthlyLimit = request.MonthlyLimit;
        limit.DailyLimit = request.DailyLimit;
        limit.HourlyLimit = request.HourlyLimit;
        limit.MaxInputCharacters = request.MaxInputCharacters;
        limit.MaxOutputTokens = request.MaxOutputTokens;
        limit.TimeoutSeconds = request.TimeoutSeconds;
        limit.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        limit.UpdatedByUserId = currentUser.UserId;
        await limits.SaveChangesAsync(ct);

        var after = $"monthly={limit.MonthlyLimit} daily={limit.DailyLimit} hourly={limit.HourlyLimit} "
            + $"maxInput={limit.MaxInputCharacters} maxOutput={limit.MaxOutputTokens} model={limit.Model}";

        await audit.RecordAsync(
            "ai.limits.update", "AiUsageLimit", limit.Id.ToString(), AuditResult.Success,
            actorUserId: currentUser.UserId, actorName: currentUser.Username,
            details: $"before=[{before}] after=[{after}]", ct: ct);

        return await GetSummary(ct);
    }

    /// <summary>AI診断の有効/無効を切り替える。初期状態は無効。</summary>
    [HttpPatch("enabled")]
    [Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
    public async Task<ActionResult<ApiResponse<AiUsageSummaryDto>>> UpdateEnabled(
        [FromBody] UpdateAiEnabledRequest request, CancellationToken ct)
    {
        var limit = await GetOrCreateLimitAsync(ct);
        var before = limit.IsEnabled;

        limit.IsEnabled = request.IsEnabled;
        limit.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        limit.UpdatedByUserId = currentUser.UserId;
        await limits.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "ai.enabled.update", "AiUsageLimit", limit.Id.ToString(), AuditResult.Success,
            actorUserId: currentUser.UserId, actorName: currentUser.Username,
            details: $"enabled={before}->{limit.IsEnabled}", ct: ct);

        return await GetSummary(ct);
    }

    /// <summary>設定が未作成なら既定値で作る(初期状態は無効)。</summary>
    private async Task<AiUsageLimit> GetOrCreateLimitAsync(CancellationToken ct)
    {
        var limit = await limits.GetAsync(ct);
        if (limit is not null)
        {
            return limit;
        }

        limit = new AiUsageLimit
        {
            IsEnabled = false,
            UpdatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        await limits.AddAsync(limit, ct);
        await limits.SaveChangesAsync(ct);
        return limit;
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
