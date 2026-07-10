namespace ServerOperations.Api.Extensions;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "server-operations-platform";

    public string Audience { get; set; } = "server-operations-platform";

    /// <summary>HMAC-SHA256署名鍵。32文字以上。環境変数 Jwt__SigningKey で必ず上書きする。</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 60;

    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>管理操作で要求するMFA直近認証の有効時間(分)。</summary>
    public int MfaFreshnessMinutes { get; set; } = 15;
}
