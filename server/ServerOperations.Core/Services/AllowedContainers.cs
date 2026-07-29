using System.Text.Json;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services;

/// <summary>
/// 監視対象ごとの操作許可コンテナリスト。
/// 空の場合はどのコンテナも操作できない(初期状態は安全側)。
/// </summary>
public static class AllowedContainers
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<string> Parse(MonitoringTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.AllowedContainersJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(target.AllowedContainersJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            // 壊れた設定は「許可なし」として扱う
            return [];
        }
    }

    public static string Serialize(IEnumerable<string> containers) =>
        JsonSerializer.Serialize(
            containers
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToList(),
            JsonOptions);

    /// <summary>指定コンテナが操作を許可されているか。</summary>
    public static bool IsAllowed(MonitoringTarget target, string? containerName) =>
        !string.IsNullOrWhiteSpace(containerName)
        && Parse(target).Contains(containerName, StringComparer.Ordinal);
}
