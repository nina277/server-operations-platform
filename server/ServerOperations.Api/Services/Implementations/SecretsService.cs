using Microsoft.AspNetCore.DataProtection;
using ServerOperations.Api.DTOs.Settings;
using ServerOperations.Api.Models.Auth;
using ServerOperations.Api.Models.Settings;
using ServerOperations.Api.Repositories.Interfaces;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

public class SecretsService(
    IEncryptedSecretRepository secrets,
    IDataProtectionProvider dataProtectionProvider,
    IAuditService audit,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : ISecretsService
{
    private static readonly string[] Kinds =
    [
        "smtp-password",
        "gemini-api-key",
        "fcm-service-account",
        "backup-access-key",
        "backup-secret-key",
    ];

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("EncryptedSecret");

    public IReadOnlyList<string> AllowedKinds => Kinds;

    public async Task<SecretStatusDto> GetStatusAsync(string kind, CancellationToken ct = default)
    {
        ValidateKind(kind);
        var stored = await secrets.FindByKindAsync(kind, ct);

        return new SecretStatusDto
        {
            Kind = kind,
            IsConfigured = stored is not null,
            UpdatedAt = stored?.UpdatedAt,
        };
    }

    public async Task<SecretStatusDto> UpdateAsync(string kind, string value, CancellationToken ct = default)
    {
        ValidateKind(kind);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw AppException.BadRequest("secret_value_required", "秘密値を入力してください。");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var stored = await secrets.FindByKindAsync(kind, ct);
        var isNew = stored is null;

        if (stored is null)
        {
            await secrets.AddAsync(new EncryptedSecret
            {
                Kind = kind,
                ValueProtected = _protector.Protect(value),
                UpdatedAt = now,
                UpdatedByUserId = currentUser.UserId,
            }, ct);
        }
        else
        {
            stored.ValueProtected = _protector.Protect(value);
            stored.UpdatedAt = now;
            stored.UpdatedByUserId = currentUser.UserId;
        }

        await secrets.SaveChangesAsync(ct);

        // 監査詳細に秘密値そのものは決して含めない
        await audit.RecordAsync(
            "settings.secret.update", "EncryptedSecret", kind, AuditResult.Success,
            actorUserId: currentUser.UserId, actorName: currentUser.Username,
            details: isNew ? "configured" : "rotated", ct: ct);

        return new SecretStatusDto { Kind = kind, IsConfigured = true, UpdatedAt = now };
    }

    private static void ValidateKind(string kind)
    {
        if (!Kinds.Contains(kind))
        {
            throw AppException.BadRequest("unknown_secret_kind", "不明な秘密値の種別です。");
        }
    }
}
