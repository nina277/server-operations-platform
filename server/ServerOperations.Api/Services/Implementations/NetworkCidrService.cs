using System.Net;
using ServerOperations.Api.DTOs.Settings;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Settings;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

public class NetworkCidrService(
    ITrustedNetworkCidrRepository cidrs,
    IAuditService audit,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : INetworkCidrService
{
    public async Task<List<NetworkCidrDto>> GetAllAsync(CancellationToken ct = default)
    {
        var all = await cidrs.GetAllAsync(ct);
        return all.Select(ToDto).ToList();
    }

    public async Task<NetworkCidrDto> AddAsync(CreateNetworkCidrRequest request, CancellationToken ct = default)
    {
        if (!CidrParser.TryParse(request.Cidr, out var network))
        {
            throw AppException.BadRequest("invalid_cidr", "CIDRの形式が正しくありません(例: 192.168.1.0/24)。");
        }

        var normalized = network.ToString();
        var existing = await cidrs.GetAllAsync(ct);

        if (existing.Any(c => c.Cidr == normalized))
        {
            throw AppException.Conflict("duplicate_cidr", "同じCIDRが既に登録されています。");
        }

        // 最初の1件を登録すると制限が有効になるため、現在の接続元が範囲内であることを要求する
        if (existing.Count == 0)
        {
            var remoteIp = currentUser.RemoteIp
                ?? throw AppException.BadRequest("unknown_remote_ip", "接続元IPアドレスを判定できません。");
            if (!CidrParser.Contains(network, remoteIp))
            {
                throw AppException.BadRequest(
                    "cidr_would_lock_out",
                    "現在の接続元がこの範囲に含まれていません。自分自身を締め出す変更は実行できません。");
            }
        }

        var entity = new TrustedNetworkCidr
        {
            Cidr = normalized,
            Description = request.Description,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            CreatedByUserId = currentUser.UserId,
        };
        await cidrs.AddAsync(entity, ct);
        await cidrs.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "settings.network_cidr.add", "TrustedNetworkCidr", normalized, AuditResult.Success,
            actorUserId: currentUser.UserId, actorName: currentUser.Username,
            details: $"added {normalized}", ct: ct);

        return ToDto(entity);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var target = await cidrs.FindByIdAsync(id, ct)
            ?? throw AppException.NotFound("cidr_not_found", "指定されたCIDRが見つかりません。");

        var all = await cidrs.GetAllAsync(ct);

        // 最後の1件の削除は拒否する(全開放への意図しない変更を防ぐ)
        if (all.Count <= 1)
        {
            throw AppException.BadRequest(
                "cannot_delete_last_cidr", "最後の許可範囲は削除できません。先に別の範囲を追加してください。");
        }

        // 削除後も現在の接続元が許可されることを要求する
        var remoteIp = currentUser.RemoteIp
            ?? throw AppException.BadRequest("unknown_remote_ip", "接続元IPアドレスを判定できません。");
        var remaining = all.Where(c => c.Id != id).ToList();
        var stillAllowed = remaining.Any(c =>
            CidrParser.TryParse(c.Cidr, out var network) && CidrParser.Contains(network, remoteIp));
        if (!stillAllowed)
        {
            throw AppException.BadRequest(
                "cidr_would_lock_out",
                "削除すると現在の接続元が許可範囲外になります。自分自身を締め出す変更は実行できません。");
        }

        await cidrs.RemoveAsync(target, ct);
        await cidrs.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "settings.network_cidr.delete", "TrustedNetworkCidr", target.Cidr, AuditResult.Success,
            actorUserId: currentUser.UserId, actorName: currentUser.Username,
            details: $"deleted {target.Cidr}", ct: ct);
    }

    public async Task<bool> IsAllowedAsync(IPAddress? remoteIp, CancellationToken ct = default)
    {
        var all = await cidrs.GetAllAsync(ct);

        // 未登録の場合は制限しない(初期セットアップ用)
        if (all.Count == 0)
        {
            return true;
        }

        if (remoteIp is null)
        {
            return false;
        }

        return all.Any(c => CidrParser.TryParse(c.Cidr, out var network) && CidrParser.Contains(network, remoteIp));
    }

    private static NetworkCidrDto ToDto(TrustedNetworkCidr entity) => new()
    {
        Id = entity.Id,
        Cidr = entity.Cidr,
        Description = entity.Description,
        CreatedAt = entity.CreatedAt,
    };
}
