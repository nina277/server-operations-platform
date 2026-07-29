using Microsoft.AspNetCore.DataProtection;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services.Ai;

namespace ServerOperations.Api.Services.Implementations;

/// <summary>
/// AIのAPIキー取得。呼び出し時に復号し、保持・ログ出力しない。
/// </summary>
public class AiApiKeyProvider(
    IEncryptedSecretRepository secrets,
    IDataProtectionProvider dataProtectionProvider) : IAiApiKeyProvider
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("EncryptedSecret");

    public async Task<string?> GetApiKeyAsync(CancellationToken ct = default)
    {
        var secret = await secrets.FindByKindAsync("gemini-api-key", ct);
        return secret is null ? null : _protector.Unprotect(secret.ValueProtected);
    }
}
