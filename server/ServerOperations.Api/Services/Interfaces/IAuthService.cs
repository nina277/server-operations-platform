using ServerOperations.Api.DTOs.Auth;

namespace ServerOperations.Api.Services.Interfaces;

public interface IAuthService
{
    Task<TokenPairResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<TokenPairResponse> RefreshAsync(string refreshToken, CancellationToken ct = default);

    Task LogoutAsync(string refreshToken, CancellationToken ct = default);

    Task<MeResponse> GetMeAsync(long userId, CancellationToken ct = default);

    /// <summary>
    /// 自分のパスワードを変更する。本人確認のため現在のパスワードを要求し、
    /// 変更後は他の端末のセッションを失効させる。
    /// </summary>
    Task<ChangePasswordResponse> ChangePasswordAsync(
        long userId, ChangePasswordRequest request, CancellationToken ct = default);
}
