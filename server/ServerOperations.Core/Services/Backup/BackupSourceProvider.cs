using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Data;

namespace ServerOperations.Core.Services.Backup;

public interface IBackupSourceProvider
{
    /// <summary>バックアップ対象のスナップショットを作る(暗号化前の平文)。</summary>
    Task<byte[]> CreateSnapshotAsync(CancellationToken ct = default);
}

/// <summary>
/// バックアップ対象を収集する。
/// 復元に必要な設定・対象定義・ルールを含め、暗号化済み秘密値と収集データは含めない
/// (秘密値はData Protection鍵に依存するため、鍵を別途保全する運用とする)。
/// </summary>
public class DatabaseBackupSourceProvider(AppDbContext db) : IBackupSourceProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public async Task<byte[]> CreateSnapshotAsync(CancellationToken ct = default)
    {
        var snapshot = new
        {
            version = 1,
            createdAt = DateTime.UtcNow,
            users = await db.Users
                .Select(u => new { u.Id, u.Username, u.Role, u.IsActive, u.CreatedAt })
                .ToListAsync(ct),
            settings = await db.SystemSettings
                .Select(s => new { s.Category, s.Value, s.UpdatedAt })
                .ToListAsync(ct),
            trustedNetworkCidrs = await db.TrustedNetworkCidrs
                .Select(c => new { c.Cidr, c.Description, c.CreatedAt })
                .ToListAsync(ct),
            monitoringTargets = await db.MonitoringTargets
                .Select(t => new { t.Id, t.Name, t.TemplateId, t.Description, t.IsEnabled, t.CreatedAt })
                .ToListAsync(ct),
            targetProfiles = await db.TargetProfiles
                .Select(p => new { p.TargetId, p.SettingsJson, p.UpdatedAt })
                .ToListAsync(ct),
            diagnosticRules = await db.DiagnosticRules
                .Select(r => new
                {
                    r.Name, r.Classification, r.RuleType, r.ConditionJson, r.Severity,
                    r.RecommendedActionId, r.Priority, r.RationaleTemplate, r.IsEnabled,
                })
                .ToListAsync(ct),
        };

        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, JsonOptions));
    }
}
