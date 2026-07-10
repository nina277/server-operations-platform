using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ServerOperations.Api.Repositories.Interfaces;

namespace ServerOperations.Api.Extensions;

/// <summary>
/// 管理操作用のポリシー要件。MFAが有効で、かつ直近認証時刻が設定時間内であることを要求する。
/// </summary>
public class RecentMfaRequirement : IAuthorizationRequirement;

public class RecentMfaHandler(
    IMfaCredentialRepository credentials,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider) : AuthorizationHandler<RecentMfaRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, RecentMfaRequirement requirement)
    {
        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(userIdValue, out var userId))
        {
            return;
        }

        var credential = await credentials.FindByUserIdAsync(userId);
        if (credential is not { IsEnabled: true, LastVerifiedAt: not null })
        {
            return;
        }

        var freshness = TimeSpan.FromMinutes(jwtOptions.Value.MfaFreshnessMinutes);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (now - credential.LastVerifiedAt.Value <= freshness)
        {
            context.Succeed(requirement);
        }
    }
}

public static class AuthorizationPolicies
{
    /// <summary>OperatorAdminロール + MFA直近認証を要求する管理操作用ポリシー。</summary>
    public const string AdminWithRecentMfa = "AdminWithRecentMfa";

    /// <summary>MFA直近認証のみを要求するポリシー(ロールは各エンドポイントで指定)。</summary>
    public const string RecentMfa = "RecentMfa";
}
