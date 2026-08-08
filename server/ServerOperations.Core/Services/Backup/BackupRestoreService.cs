using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Models.Settings;

namespace ServerOperations.Core.Services.Backup;

/// <summary>保存先にあるバックアップの1世代。</summary>
public record BackupGeneration(string ObjectKey, DateTime LastModified, long SizeBytes);

/// <summary>種別ごとの、復元で何が起きるか。</summary>
public record RestorePlanItem(
    string Category, int Added, int Updated, int Unchanged, int NotInBackup);

/// <summary>
/// 復元の下見または結果。
/// <paramref name="Applied"/> が false なら何も変更していない。
/// </summary>
public record BackupRestorePlan(
    string ObjectKey,
    DateTime SnapshotCreatedAt,
    int Version,
    bool Applied,
    List<RestorePlanItem> Items,
    List<string> Notes);

/// <summary>
/// 保存先(S3互換)への読み取り。
///
/// 復元の判断(誰を戻すか・何を消さないか)は保存先の実装と無関係に決まる。
/// **そこを試験するために切り離してある。**
/// </summary>
public interface IBackupObjectStore
{
    Task<List<BackupGeneration>> ListAsync(CancellationToken ct = default);

    Task<byte[]> GetAsync(string objectKey, CancellationToken ct = default);
}

public interface IBackupRestoreService
{
    /// <summary>保存先にある世代を新しい順に返す。</summary>
    Task<List<BackupGeneration>> ListGenerationsAsync(CancellationToken ct = default);

    /// <summary>復元の下見。**何も変更しない。**</summary>
    Task<BackupRestorePlan> PreviewAsync(string objectKey, CancellationToken ct = default);

    /// <summary>復元を適用する。</summary>
    Task<BackupRestorePlan> RestoreAsync(string objectKey, long? userId, CancellationToken ct = default);
}

/// <summary>
/// バックアップからの復元。
///
/// **復元は破壊的で、それ自体が乗っ取りの経路になりうる。**
/// 呼び出し側で管理者 + MFA再認証・実行前の確認・監査を要求すること。
///
/// 方針を2つ決めてある。
///
/// 1. **利用者は復元しない。**
///    バックアップにパスワードハッシュが入っていないため、復元しても誰もログインできない。
///    さらに、降格・無効化した利用者を古いバックアップで元へ戻せてしまうと、
///    **バックアップの復元が権限昇格の手段になる。**
///
/// 2. **バックアップに無いものは消さない。**
///    「置き換える」を素直に実装すると、バックアップ以後に作られた監視対象が消える。
///    復元は取り戻すための操作であり、いま動いているものを消す操作ではない。
///    現存するがバックアップに無いものは件数だけ報告する。
///
/// なお監視対象の自動復旧の有無と許可コンテナはバックアップに含まれない。
/// そのため**復元で自動復旧が有効になったり許可リストが広がることは起きない。**
/// 新しく作られる対象は既定(自動復旧OFF・許可リスト空)になる。
/// </summary>
public class BackupRestoreService(
    IBackupSettingsProvider settingsProvider,
    IBackupObjectStore objectStore,
    AppDbContext db,
    TimeProvider timeProvider,
    ILogger<BackupRestoreService> logger) : IBackupRestoreService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<List<BackupGeneration>> ListGenerationsAsync(CancellationToken ct = default) =>
        objectStore.ListAsync(ct);

    public Task<BackupRestorePlan> PreviewAsync(string objectKey, CancellationToken ct = default) =>
        ApplyAsync(objectKey, apply: false, userId: null, ct);

    public Task<BackupRestorePlan> RestoreAsync(
        string objectKey, long? userId, CancellationToken ct = default) =>
        ApplyAsync(objectKey, apply: true, userId, ct);

    private async Task<BackupRestorePlan> ApplyAsync(
        string objectKey, bool apply, long? userId, CancellationToken ct)
    {
        var snapshot = await FetchSnapshotAsync(objectKey, ct);
        var notes = new List<string>();
        var items = new List<RestorePlanItem>();

        items.Add(await RestoreSettingsAsync(snapshot, apply, userId, ct));
        items.Add(await RestoreCidrsAsync(snapshot, apply, userId, ct));
        items.Add(await RestoreTargetsAsync(snapshot, apply, userId, ct));
        items.Add(await RestoreRulesAsync(snapshot, apply, ct));

        var users = Elements(snapshot, "users").Count;
        if (users > 0)
        {
            notes.Add(
                $"利用者 {users} 件は復元しません。"
                + "バックアップにパスワードハッシュが無いため復元しても誰もログインできず、"
                + "役割や有効・無効を戻せると権限昇格の手段になるためです。");
        }

        notes.Add(
            "監視対象の資格情報・自動復旧の有無・許可コンテナはバックアップに含まれません。"
            + "新しく作られた対象は自動復旧OFF・許可リスト空になります。");
        notes.Add("収集値・インシデント・監査ログは対象外です(mysqldump からの復元を使ってください)。");

        if (apply)
        {
            await db.SaveChangesAsync(ct);
            logger.LogWarning(
                "Backup restored from {ObjectKey} by user {UserId}.", objectKey, userId);
        }

        return new BackupRestorePlan(
            objectKey,
            GetDateTime(snapshot, "createdAt"),
            snapshot.TryGetProperty("version", out var v) && v.TryGetInt32(out var version) ? version : 0,
            apply,
            items,
            notes);
    }

    // --- 種別ごとの復元 -------------------------------------------------

    private async Task<RestorePlanItem> RestoreSettingsAsync(
        JsonElement snapshot, bool apply, long? userId, CancellationToken ct)
    {
        var existing = await db.SystemSettings.ToListAsync(ct);
        int added = 0, updated = 0, unchanged = 0;
        var seen = new HashSet<SettingCategory>();

        foreach (var entry in Elements(snapshot, "settings"))
        {
            if (!Enum.TryParse<SettingCategory>(GetString(entry, "category"), out var category))
            {
                continue;
            }

            seen.Add(category);
            var value = GetString(entry, "value") ?? string.Empty;
            var current = existing.FirstOrDefault(s => s.Category == category);

            if (current is null)
            {
                added++;
                if (apply)
                {
                    db.SystemSettings.Add(new SystemSetting
                    {
                        Category = category,
                        Value = value,
                        UpdatedAt = Now,
                        UpdatedByUserId = userId,
                    });
                }
            }
            else if (current.Value != value)
            {
                updated++;
                if (apply)
                {
                    current.Value = value;
                    current.UpdatedAt = Now;
                    current.UpdatedByUserId = userId;
                }
            }
            else
            {
                unchanged++;
            }
        }

        return new RestorePlanItem(
            "設定", added, updated, unchanged, existing.Count(s => !seen.Contains(s.Category)));
    }

    private async Task<RestorePlanItem> RestoreCidrsAsync(
        JsonElement snapshot, bool apply, long? userId, CancellationToken ct)
    {
        var existing = await db.TrustedNetworkCidrs.ToListAsync(ct);
        int added = 0, unchanged = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Elements(snapshot, "trustedNetworkCidrs"))
        {
            var cidr = GetString(entry, "cidr");
            if (string.IsNullOrWhiteSpace(cidr))
            {
                continue;
            }

            seen.Add(cidr);
            if (existing.Any(c => string.Equals(c.Cidr, cidr, StringComparison.OrdinalIgnoreCase)))
            {
                unchanged++;
                continue;
            }

            added++;
            if (apply)
            {
                db.TrustedNetworkCidrs.Add(new TrustedNetworkCidr
                {
                    Cidr = cidr,
                    Description = GetString(entry, "description"),
                    CreatedAt = Now,
                    CreatedByUserId = userId,
                });
            }
        }

        return new RestorePlanItem(
            "許可ネットワーク", added, 0, unchanged, existing.Count(c => !seen.Contains(c.Cidr)));
    }

    private async Task<RestorePlanItem> RestoreTargetsAsync(
        JsonElement snapshot, bool apply, long? userId, CancellationToken ct)
    {
        var existing = await db.MonitoringTargets.Include(t => t.Profile).ToListAsync(ct);
        int added = 0, updated = 0, unchanged = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // プロファイルはバックアップ内の対象IDで紐づく
        var profiles = Elements(snapshot, "targetProfiles")
            .ToDictionary(p => GetInt64(p, "targetId"), p => GetString(p, "settingsJson") ?? "{}");

        foreach (var entry in Elements(snapshot, "monitoringTargets"))
        {
            var name = GetString(entry, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            seen.Add(name);
            var settingsJson = profiles.GetValueOrDefault(GetInt64(entry, "id"), "{}");
            var description = GetString(entry, "description");
            var isEnabled = GetBoolean(entry, "isEnabled");
            var templateId = GetString(entry, "templateId") ?? string.Empty;

            // 名前で照合する。IDはバックアップ時点のもので、現在のDBとは一致しない
            var current = existing.FirstOrDefault(t => t.Name == name);

            if (current is null)
            {
                added++;
                if (apply)
                {
                    // 自動復旧と許可コンテナはバックアップに無い。
                    // 既定(OFF・空)のままにする。**復元で自動実行が広がらないようにする**
                    var target = new MonitoringTarget
                    {
                        Name = name,
                        TemplateId = templateId,
                        Description = description,
                        IsEnabled = isEnabled,
                        CreatedAt = Now,
                        UpdatedAt = Now,
                        CreatedByUserId = userId,
                        Profile = new TargetProfile { SettingsJson = settingsJson, UpdatedAt = Now },
                    };
                    db.MonitoringTargets.Add(target);
                }
            }
            else if (current.TemplateId != templateId
                || current.Description != description
                || current.IsEnabled != isEnabled
                || (current.Profile?.SettingsJson ?? "{}") != settingsJson)
            {
                updated++;
                if (apply)
                {
                    // バックアップに入っている項目だけを戻す。
                    // 自動復旧・許可コンテナ・収集間隔には触れない
                    current.TemplateId = templateId;
                    current.Description = description;
                    current.IsEnabled = isEnabled;
                    current.UpdatedAt = Now;
                    if (current.Profile is null)
                    {
                        current.Profile = new TargetProfile
                        {
                            TargetId = current.Id, SettingsJson = settingsJson, UpdatedAt = Now,
                        };
                    }
                    else
                    {
                        current.Profile.SettingsJson = settingsJson;
                        current.Profile.UpdatedAt = Now;
                    }
                }
            }
            else
            {
                unchanged++;
            }
        }

        return new RestorePlanItem(
            "監視対象", added, updated, unchanged, existing.Count(t => !seen.Contains(t.Name)));
    }

    private async Task<RestorePlanItem> RestoreRulesAsync(
        JsonElement snapshot, bool apply, CancellationToken ct)
    {
        var existing = await db.DiagnosticRules.ToListAsync(ct);
        int added = 0, updated = 0, unchanged = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in Elements(snapshot, "diagnosticRules"))
        {
            var name = GetString(entry, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            seen.Add(name);
            var condition = GetString(entry, "conditionJson") ?? "{}";
            var classification = GetString(entry, "classification") ?? string.Empty;
            var isEnabled = GetBoolean(entry, "isEnabled");
            var recommended = GetString(entry, "recommendedActionId");
            var priority = (int)GetInt64(entry, "priority");
            var template = GetString(entry, "rationaleTemplate") ?? string.Empty;

            if (!Enum.TryParse<DiagnosticRuleType>(GetString(entry, "ruleType"), out var ruleType)
                || !Enum.TryParse<IncidentSeverity>(GetString(entry, "severity"), out var severity))
            {
                continue;
            }

            var current = existing.FirstOrDefault(r => r.Name == name);
            if (current is null)
            {
                added++;
                if (apply)
                {
                    db.DiagnosticRules.Add(new DiagnosticRule
                    {
                        Name = name,
                        Classification = classification,
                        RuleType = ruleType,
                        ConditionJson = condition,
                        Severity = severity,
                        RecommendedActionId = recommended,
                        Priority = priority,
                        RationaleTemplate = template,
                        IsEnabled = isEnabled,
                        CreatedAt = Now,
                        UpdatedAt = Now,
                    });
                }
            }
            else if (current.ConditionJson != condition
                || current.Classification != classification
                || current.IsEnabled != isEnabled
                || current.Priority != priority
                || current.Severity != severity
                || current.RuleType != ruleType
                || current.RecommendedActionId != recommended
                || current.RationaleTemplate != template)
            {
                updated++;
                if (apply)
                {
                    current.Classification = classification;
                    current.RuleType = ruleType;
                    current.ConditionJson = condition;
                    current.Severity = severity;
                    current.RecommendedActionId = recommended;
                    current.Priority = priority;
                    current.RationaleTemplate = template;
                    current.IsEnabled = isEnabled;
                    current.UpdatedAt = Now;
                }
            }
            else
            {
                unchanged++;
            }
        }

        return new RestorePlanItem(
            "診断ルール", added, updated, unchanged, existing.Count(r => !seen.Contains(r.Name)));
    }

    // --- 取得と復号 -----------------------------------------------------

    private async Task<JsonElement> FetchSnapshotAsync(string objectKey, CancellationToken ct)
    {
        var encryptionKey = await settingsProvider.GetEncryptionKeyAsync(ct);
        if (string.IsNullOrWhiteSpace(encryptionKey))
        {
            throw new InvalidOperationException("バックアップ暗号化キーが設定されていません。");
        }

        // 保存先が持つキーだけを受け付ける。任意のキーを取りに行かせない
        var generations = await ListGenerationsAsync(ct);
        if (!generations.Any(g => g.ObjectKey == objectKey))
        {
            throw new InvalidOperationException("指定されたバックアップが保存先にありません。");
        }

        var blob = await objectStore.GetAsync(objectKey, ct);

        byte[] plaintext;
        try
        {
            plaintext = BackupService.Decrypt(blob, encryptionKey);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // 鍵を変更した後は、変更前に取ったバックアップを開けない
            throw new InvalidOperationException(
                "復号できません。暗号化キーが違うか、データが壊れています。");
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(plaintext, JsonOptions);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("バックアップの中身を読めません。");
        }
    }

    // --- JSONの読み取り -------------------------------------------------

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private static List<JsonElement> Elements(JsonElement snapshot, string name) =>
        snapshot.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().ToList()
            : [];

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => value.ToString(),
            }
            : null;

    private static long GetInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;

    private static bool GetBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    private static DateTime GetDateTime(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetDateTime(out var parsed)
            ? parsed
            : default;
}
