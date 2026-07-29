using Microsoft.AspNetCore.DataProtection;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services.Ai;

namespace ServerOperations.Worker;

/// <summary>Worker側のAI APIキー取得。APIと同じ鍵リング・目的文字列を使う。</summary>
public class WorkerAiApiKeyProvider(
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
