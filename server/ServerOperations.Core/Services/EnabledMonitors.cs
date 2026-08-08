using System.Text.Json;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services;

/// <summary>
/// 対象ごとに行う収集の種類。
///
/// 未設定(null)はテンプレートで行えるものすべてを意味する。
/// 「空配列 = 何もしない」とはしない。すべて外すなら監視自体を止めるべきで、
/// 収集だけ止めた対象は「監視しているのに何も見ていない」状態になるため。
/// </summary>
public static class EnabledMonitors
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>この対象で実際に行う収集の種類を返す。</summary>
    public static IReadOnlyList<string> Resolve(MonitoringTarget target, AdapterTemplate template)
    {
        if (string.IsNullOrWhiteSpace(target.EnabledMonitorsJson))
        {
            return template.CollectableMonitors;
        }

        List<string>? selected;
        try
        {
            selected = JsonSerializer.Deserialize<List<string>>(target.EnabledMonitorsJson, JsonOptions);
        }
        catch (JsonException)
        {
            // 壊れた設定は「テンプレートの既定どおり」として扱う。
            // 空として扱うと、設定が壊れただけで監視が黙って止まる。
            return template.CollectableMonitors;
        }

        if (selected is null || selected.Count == 0)
        {
            return template.CollectableMonitors;
        }

        // テンプレートで行えないものが混ざっていても無視する
        return selected
            .Where(m => template.CollectableMonitors.Contains(m, StringComparer.Ordinal))
            .ToList();
    }

    public static bool IsEnabled(MonitoringTarget target, AdapterTemplate template, string monitor) =>
        Resolve(target, template).Contains(monitor, StringComparer.Ordinal);

    /// <summary>
    /// 保存する値を作る。テンプレートで行えるものすべてが選ばれている場合はnullにする。
    /// 「既定のまま」と「たまたま全部選んだ」を同じ状態として持ち、
    /// 後からテンプレートに収集が増えたときに自動で追従できるようにする。
    /// </summary>
    public static string? Serialize(IEnumerable<string>? monitors, AdapterTemplate template)
    {
        if (monitors is null)
        {
            return null;
        }

        var normalized = monitors
            .Select(m => m.Trim())
            .Where(m => m.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Where(m => template.CollectableMonitors.Contains(m, StringComparer.Ordinal))
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();

        if (normalized.Count == 0 || normalized.Count == template.CollectableMonitors.Count)
        {
            return null;
        }

        return JsonSerializer.Serialize(normalized, JsonOptions);
    }

    /// <summary>
    /// 保存前の検証。テンプレートで行えない種類を指定させない。
    /// 黙って捨てると、選んだのに効かない設定ができる。
    /// </summary>
    public static void Validate(IEnumerable<string>? monitors, AdapterTemplate template)
    {
        if (monitors is null)
        {
            return;
        }

        var unknown = monitors
            .Select(m => m.Trim())
            .Where(m => m.Length > 0)
            .FirstOrDefault(m => !template.CollectableMonitors.Contains(m, StringComparer.Ordinal));

        if (unknown is not null)
        {
            throw AppException.BadRequest(
                "unknown_monitor",
                $"このテンプレートで行えない監視項目です: {unknown}");
        }
    }
}
