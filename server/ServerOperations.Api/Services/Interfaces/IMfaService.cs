using ServerOperations.Api.DTOs.Auth;

namespace ServerOperations.Api.Services.Interfaces;

public interface IMfaService
{
    /// <summary>TOTPシークレットを生成・保存し、認証アプリ登録情報を返す(未検証状態)。</summary>
    Task<MfaSetupResponse> SetupAsync(long userId, CancellationToken ct = default);

    /// <summary>TOTPコードを検証する。初回成功でMFAを有効化し、以後は直近認証時刻を更新する。</summary>
    Task<MfaVerifyResponse> VerifyAsync(long userId, string totpCode, CancellationToken ct = default);

    /// <summary>ログイン時のTOTP検証。成功時は直近認証時刻を更新する。</summary>
    Task<bool> ValidateForLoginAsync(long userId, string totpCode, CancellationToken ct = default);
}
