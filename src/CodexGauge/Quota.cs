using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodexGauge;

public sealed record QuotaWindow(string Key, string Bucket, string Label, double Remaining, int? Minutes, DateTimeOffset? Reset)
{
    public string Period => Minutes switch
    {
        10080 => "7 дней", 300 => "5 часов", 1440 => "24 часа",
        > 0 when Minutes % 1440 == 0 => $"{Minutes / 1440} дн.",
        > 0 when Minutes % 60 == 0 => $"{Minutes / 60} ч.",
        > 0 => $"{Minutes} мин.", _ => "Период неизвестен"
    };
    public string MenuLabel => $"{Label} · {Period} · {Remaining:0.#}%";
}

public static class QuotaParser
{
    public static List<QuotaWindow> Parse(JsonElement result)
    {
        var list = new List<QuotaWindow>();
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in buckets.EnumerateObject()) Add(list, item.Name, item.Value);
        }
        else if (result.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
            Add(list, String(legacy, "limitId") ?? "codex", legacy);
        return list;
    }

    private static string? String(JsonElement value, string name) => value.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static void Add(List<QuotaWindow> list, string id, JsonElement bucket)
    {
        if (bucket.ValueKind != JsonValueKind.Object) return;
        foreach (var slot in new[] { "primary", "secondary" })
        {
            if (!bucket.TryGetProperty(slot, out var w) || w.ValueKind != JsonValueKind.Object ||
                !w.TryGetProperty("usedPercent", out var used) || used.ValueKind != JsonValueKind.Number || !used.TryGetDouble(out var percent) || !double.IsFinite(percent)) continue;
            int? minutes = w.TryGetProperty("windowDurationMins", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out var duration) && duration > 0 ? duration : null;
            DateTimeOffset? reset = null;
            if (w.TryGetProperty("resetsAt", out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetInt64(out var seconds))
            {
                try { reset = DateTimeOffset.FromUnixTimeSeconds(seconds); } catch (ArgumentOutOfRangeException) { }
            }
            list.Add(new QuotaWindow(id + ":" + (minutes?.ToString() ?? slot), id,
                String(bucket, "limitName") ?? (id == "codex" ? "Codex" : id), Math.Clamp(100 - percent, 0, 100), minutes, reset));
        }
    }

    public static QuotaWindow? Select(List<QuotaWindow> windows, string? key) =>
        windows.FirstOrDefault(w => w.Key == key) ?? windows.FirstOrDefault(w => w.Bucket == "codex" && w.Minutes == 10080) ??
        windows.FirstOrDefault(w => w.Bucket == "codex") ?? windows.FirstOrDefault();
}
