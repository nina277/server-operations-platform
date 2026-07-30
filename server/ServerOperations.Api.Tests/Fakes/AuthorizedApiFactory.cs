using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using ServerOperations.Api.Extensions;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Tests.Fakes;

/// <summary>
/// 認可の検証用にHTTP経由でAPIを呼ぶための土台。
///
/// 役割ごとのトークンを発行して「誰が何を叩けるか」を確かめる。
/// MFAの直近認証はDBを見るため、そこだけ差し替える(認可の判定そのものは本物を通す)。
/// </summary>
public class AuthorizedApiFactory : WebApplicationFactory<Program>
{
    /// <summary>appsettings.Development.json と同じ鍵。テストはDevelopment環境で動く。</summary>
    private const string SigningKey = "dev-only-signing-key-do-not-use-in-production-0001";

    private const string Issuer = "server-operations-platform";

    private readonly FakeMfaCredentialRepository _mfaCredentials = new();

    /// <summary>MFAを直近で認証済みとして扱うか。falseなら「再認証が必要」な状態になる。</summary>
    public bool MfaRecentlyVerified
    {
        get => _mfaCredentials.Credentials.Count > 0;
        set
        {
            _mfaCredentials.Credentials.Clear();
            if (value)
            {
                _mfaCredentials.Credentials.Add(new MfaCredential
                {
                    Id = 1,
                    UserId = 1,
                    SecretProtected = "protected",
                    IsEnabled = true,
                    LastVerifiedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow.AddDays(-30),
                });
            }
        }
    }

    public AuthorizedApiFactory()
    {
        // 既定では管理者がMFAを直近で通した状態にしておく。
        // 認可テストの主題は役割の判定なので、MFAは前提を満たしておく。
        // falseを設定すると「再認証が必要」な状態を再現できる。
        MfaRecentlyVerified = true;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // MFAの直近認証だけDBを見るため差し替える。
            // ロール判定とポリシーの評価は本物をそのまま通す。
            services.RemoveAll<IMfaCredentialRepository>();
            services.AddSingleton<IMfaCredentialRepository>(_mfaCredentials);
        });
    }

    /// <summary>指定した役割のアクセストークンを発行する。</summary>
    public string CreateToken(UserRole role, long userId = 1, string username = "tester")
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, role.ToString()),
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Issuer,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>指定した役割で認証済みのクライアントを作る。</summary>
    public HttpClient CreateClientAs(UserRole role, long userId = 1)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CreateToken(role, userId));
        return client;
    }
}
