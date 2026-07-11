using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ServerOperations.Core.Adapters.Implementations;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Repositories.Implementations;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Api.Services.Implementations;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);

        // Data Protection(MFAシークレット暗号化)。コンテナ再起動後も復号できるよう鍵をファイルへ永続化する。
        var keysPath = configuration["DataProtection:KeysPath"];
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("server-operations-platform");
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 0))));

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IMfaCredentialRepository, MfaCredentialRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<ISystemSettingRepository, SystemSettingRepository>();
        services.AddScoped<IEncryptedSecretRepository, EncryptedSecretRepository>();
        services.AddScoped<ITrustedNetworkCidrRepository, TrustedNetworkCidrRepository>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IMfaService, MfaService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<ISecretsService, SecretsService>();
        services.AddScoped<INetworkCidrService, NetworkCidrService>();

        services.AddScoped<IMonitoringTargetRepository, MonitoringTargetRepository>();
        services.AddSingleton<IAdapterTemplateCatalog, AdapterTemplateCatalog>();
        services.AddScoped<ITargetService, TargetService>();

        // アダプター用HTTPクライアント。リダイレクトは追跡せず、接続時にも遮断対象IPを検査する
        // (登録時の検証後にDNSの解決先が差し替えられるDNS rebindingへの対策)
        services.AddHttpClient(DockerAdapter.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(CreateGuardedHandler);
        services.AddHttpClient(HttpAdapter.HttpClientName, client =>
            {
                // 個別タイムアウトはHttpAdapter側のCancellationTokenで制御する
                client.Timeout = TimeSpan.FromSeconds(65);
            })
            .ConfigurePrimaryHttpMessageHandler(CreateGuardedHandler);
        services.AddScoped<IDockerAdapter, DockerAdapter>();
        services.AddScoped<IHttpAdapter, HttpAdapter>();

        return services;
    }

    /// <summary>
    /// 接続時に自前でDNS解決し、遮断対象(ループバック・リンクローカル等)を除いた
    /// 検証済みIPだけへ接続するハンドラーを作る。
    /// </summary>
    private static SocketsHttpHandler CreateGuardedHandler() => new()
    {
        AllowAutoRedirect = false,
        ConnectCallback = async (context, ct) =>
        {
            var allowed = await EndpointValidator
                .ResolveAllowedAddressesAsync(context.DnsEndPoint.Host, ct);

            var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    public static IServiceCollection AddAppAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured with at least 32 characters (e.g. env Jwt__SigningKey).");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddScoped<IAuthorizationHandler, RecentMfaHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicies.RecentMfa, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new RecentMfaRequirement()));

            options.AddPolicy(AuthorizationPolicies.AdminWithRecentMfa, policy =>
                policy.RequireAuthenticatedUser()
                    .RequireRole(nameof(UserRole.OperatorAdmin))
                    .AddRequirements(new RecentMfaRequirement()));
        });

        return services;
    }
}
