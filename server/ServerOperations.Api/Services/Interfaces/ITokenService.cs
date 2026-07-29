using ServerOperations.Core.Models.Auth;

namespace ServerOperations.Api.Services.Interfaces;

public record AccessTokenResult(string Token, DateTime ExpiresAt);

public interface ITokenService
{
    AccessTokenResult CreateAccessToken(User user);

    /// <summary>暗号学的に安全なランダム値のリフレッシュトークンを生成する。</summary>
    string GenerateRefreshTokenValue();

    /// <summary>リフレッシュトークン値のSHA-256ハッシュ(Base64)。DBにはこの値だけを保存する。</summary>
    string HashRefreshToken(string tokenValue);
}
