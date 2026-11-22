using System.Text.RegularExpressions;

namespace a2Cr.Core.Settings;

/// <summary>内存值统一写成 <c>数字 + G/M</c>；非法值一律回退，不让启动失败。</summary>
public static class MemoryValue
{
    private static readonly Regex Pattern = new("^(\\d+)([GMgm])$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value) && Pattern.IsMatch(value);

    public static string Normalize(string? value, string fallback = "4G")
    {
        if (!IsValid(value)) return fallback;
        var match = Pattern.Match(value!);
        return match.Groups[1].Value + match.Groups[2].Value.ToUpperInvariant();
    }
}
