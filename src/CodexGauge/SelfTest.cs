using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CodexGauge;

public static class SelfTest
{
    public static int Run(string path)
    {
        var checks = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
        List<QuotaWindow> Parse(string json) { using var doc = JsonDocument.Parse(json); return QuotaParser.Parse(doc.RootElement); }
        try
        {
            var sample = Parse("""{"rateLimits":{"primary":{"usedPercent":99}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":55,"windowDurationMins":10080,"resetsAt":1788750107},"secondary":null},"spark":{"primary":{"usedPercent":0,"windowDurationMins":300}}}}""");
            Check(sample.Count == 2, "Предпочтение rateLimitsByLimitId перед legacy");
            Check(sample[0].Remaining == 45, "55% использовано = 45% осталось");
            Check(sample[0].Period == "7 дней", "Недельный лимит распознан в primary");
            Check(QuotaParser.Select(sample, null)?.Bucket == "codex", "По умолчанию основной недельный лимит");
            Check(QuotaParser.Select(sample, "spark:300")?.Bucket == "spark", "Выбор отдельной модели");
            Check(QuotaParser.Select(sample, "missing")?.Bucket == "codex", "Исчезнувший выбор заменяется основным");
            Check(Parse("""{"rateLimits":{"primary":{"usedPercent":100}}}""")[0].Remaining == 0, "Ноль не считается отсутствующими данными");
            Check(Parse("""{"rateLimits":{"primary":{"usedPercent":-20},"secondary":{"usedPercent":125}}}""").Select(x => x.Remaining).SequenceEqual(new double[] { 100, 0 }), "Значения ограничены диапазоном 0–100");
            Check(Parse("""{"rateLimits":{"primary":{"usedPercent":null},"secondary":null}}""").Count == 0, "null не превращается в 100%");
            Check(Parse("""{"rateLimitsByLimitId":{},"rateLimits":{"primary":{"usedPercent":50}}}""").Count == 0, "Пустая новая карта не подменяется устаревшими данными");
            var unknown = Parse("""{"rateLimits":{"primary":{"usedPercent":50,"resetsAt":9223372036854775807}}}""")[0];
            Check(unknown.Reset == null && unknown.Minutes == null, "Неизвестный период и некорректный timestamp безопасны");
            Check(Parse("""{"rateLimits":{"primary":null,"secondary":{"usedPercent":10,"windowDurationMins":10080}}}""")[0].Period == "7 дней", "Неделя поддерживается и в secondary");
            File.WriteAllText(path, JsonSerializer.Serialize(new { ok = true, count = checks.Count, checks }, new JsonSerializerOptions { WriteIndented = true })); return 0;
        }
        catch (Exception e) { File.WriteAllText(path, JsonSerializer.Serialize(new { ok = false, passed = checks, failed = e.Message })); return 1; }
    }
}
