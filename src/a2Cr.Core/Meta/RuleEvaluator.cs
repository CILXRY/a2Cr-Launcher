using System.Text.Json.Nodes;

namespace a2Cr.Core.Meta;

public static class RuleEvaluator
{
    public static bool IsAllowed(
        JsonArray? rules,
        string osName,
        string osArch,
        IReadOnlyDictionary<string, bool>? features = null)
    {
        if (rules is null || rules.Count == 0)
        {
            return true;
        }

        for (var index = rules.Count - 1; index >= 0; index--)
        {
            if (rules[index] is not JsonObject rule || !Matches(rule, osName, osArch, features))
            {
                continue;
            }

            return string.Equals(rule["action"]?.GetValue<string>(), "allow", StringComparison.Ordinal);
        }

        return false;
    }

    public static bool IsAllowed(JsonNode? rules, string osName, string osArch,
        IReadOnlyDictionary<string, bool>? features = null)
    {
        return IsAllowed(rules as JsonArray, osName, osArch, features);
    }

    private static bool Matches(JsonObject rule, string osName, string osArch,
        IReadOnlyDictionary<string, bool>? features)
    {
        if (rule["os"] is JsonObject os)
        {
            var name = os["name"]?.GetValue<string>();
            var arch = os["arch"]?.GetValue<string>();
            if (name is not null && !string.Equals(name, osName, StringComparison.Ordinal)) return false;
            if (arch is not null && !string.Equals(arch, osArch, StringComparison.Ordinal)) return false;
        }

        if (rule["features"] is JsonObject required)
        {
            foreach (var item in required)
            {
                var wanted = item.Value?.GetValue<bool>() ?? false;
                if ((features?.GetValueOrDefault(item.Key) ?? false) != wanted) return false;
            }
        }

        return true;
    }
}
