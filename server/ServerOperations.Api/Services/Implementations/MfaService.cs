using Microsoft.AspNetCore.DataProtection;
using OtpNet;
using ServerOperations.Api.DTOs.Auth;
using ServerOperations.Api.Models.Auth;
using ServerOperations.Api.Repositories.Interfaces;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

public class MfaService(
    IMfaCredentialRepository credentials,
    IUserRepository users,
    IDataProtectionProvider dataProtectionProvider,
    IAuditService audit,
    TimeProvider timeProvider) : IMfaService
{
    private const string Issuer = "ServerOperationsPlatform";

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("MfaSecret");

    public async Task<MfaSetupResponse> SetupAsync(long userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId, ct)
            ?? throw AppException.NotFound("user_not_found", "ユーザーが見つかりません。");

        var existing = await credentials.FindByUserIdAsync(userId, ct);
        if (existing is { IsEnabled: true })
        {
            throw AppException.Conflict("mfa_already_enabled", "MFAは既に有効です。");
        }

        // 未検証の既存レコードは作り直しを許可する
        if (existing is not null)
        {
            await credentials.RemoveAsync(existing, ct);
        }

        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        var secretBase32 = Base32Encoding.ToString(secretBytes);

        await credentials.AddAsync(new MfaCredential
        {
            UserId = userId,
            SecretProtected = _protector.Protect(secretBase32),
            IsEnabled = false,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        }, ct);
        await credentials.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "auth.mfa.setup", "User", userId.ToString(), AuditResult.Success,
            actorUserId: userId, actorName: user.Username, ct: ct);

        var otpAuthUri = new OtpUri(OtpType.Totp, secretBase32, user.Username, Issuer).ToString();

        return new MfaSetupResponse { Secret = secretBase32, OtpAuthUri = otpAuthUri };
    }

    public async Task<MfaVerifyResponse> VerifyAsync(long userId, string totpCode, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId, ct)
            ?? throw AppException.NotFound("user_not_found", "ユーザーが見つかりません。");

        var credential = await credentials.FindByUserIdAsync(userId, ct)
            ?? throw AppException.BadRequest("mfa_not_configured", "MFAが設定されていません。先にセットアップしてください。");

        if (!VerifyCode(credential, totpCode))
        {
            await audit.RecordAsync(
                "auth.mfa.verify", "User", userId.ToString(), AuditResult.Failure,
                actorUserId: userId, actorName: user.Username, details: "invalid TOTP code", ct: ct);
            throw AppException.Unauthorized("mfa_invalid_code", "認証コードが正しくありません。");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        credential.IsEnabled = true;
        credential.LastVerifiedAt = now;
        await credentials.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "auth.mfa.verify", "User", userId.ToString(), AuditResult.Success,
            actorUserId: userId, actorName: user.Username, ct: ct);

        return new MfaVerifyResponse { MfaEnabled = true, VerifiedAt = now };
    }

    public async Task<bool> ValidateForLoginAsync(long userId, string totpCode, CancellationToken ct = default)
    {
        var credential = await credentials.FindByUserIdAsync(userId, ct);
        if (credential is null || !credential.IsEnabled)
        {
            return false;
        }

        if (!VerifyCode(credential, totpCode))
        {
            return false;
        }

        credential.LastVerifiedAt = timeProvider.GetUtcNow().UtcDateTime;
        await credentials.SaveChangesAsync(ct);
        return true;
    }

    private bool VerifyCode(MfaCredential credential, string totpCode)
    {
        var secret = _protector.Unprotect(credential.SecretProtected);
        var totp = new Totp(Base32Encoding.ToBytes(secret));
        return totp.VerifyTotp(
            timeProvider.GetUtcNow().UtcDateTime,
            totpCode.Trim(),
            out _,
            VerificationWindow.RfcSpecifiedNetworkDelay);
    }
}
