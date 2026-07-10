using ServerOperations.Api.DTOs.Auth;

namespace ServerOperations.Api.Services.Interfaces;

public interface IAuthService
{
    Task<TokenPairResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<TokenPairResponse> RefreshAsync(string refreshToken, CancellationToken ct = default);

    Task LogoutAsync(string refreshToken, CancellationToken ct = default);

    Task<MeResponse> GetMeAsync(long userId, CancellationToken ct = default);
}
