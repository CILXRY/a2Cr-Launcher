using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace a2Cr.Core.Instances;

public static class InstanceNaming
{
    private static readonly Regex SlugPattern = new("[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex InvalidFileChars = new("[<>:\"/\\\\|?*\\x00-\\x1f]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string CreateFolderName(string displayName, string style, Func<string, bool> exists)
    {
        var baseName = style switch
        {
            "opaque" => Opaque(),
            "origin" => Origin(displayName),
            _ => Slug(displayName)
        };

        if (style == "opaque")
        {
            while (exists(baseName)) baseName = Opaque();
            return baseName;
        }

        var candidate = baseName;
        var index = 2;
        while (exists(candidate)) candidate = $"{baseName}-{index++}";
        return candidate;
    }

    public static string Slug(string name)
    {
        var value = SlugPattern.Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        if (value.Length == 0 || value.All(character => character == '-')) value = RandomFolder(6);
        return Truncate(value);
    }

    public static string Origin(string name)
    {
        var value = InvalidFileChars.Replace(name.Trim(), "_").Trim().TrimEnd('.', ' ');
        if (value.Length == 0 || value is "." or "..") value = RandomFolder(6);
        if (IsReserved(value)) value = "_" + value;
        return Truncate(value);
    }

    public static string Opaque() => "inst-" + RandomFolder(8);

    private static string RandomFolder(int length) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes((length + 1) / 2)).ToLowerInvariant()[..length];

    private static string Truncate(string value) => value.Length <= 48 ? value : value[..48].TrimEnd('-', '.', ' ');

    private static bool IsReserved(string name) => name.ToUpperInvariant() is
        "CON" or "PRN" or "AUX" or "NUL" or "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6"
        or "COM7" or "COM8" or "COM9" or "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6"
        or "LPT7" or "LPT8" or "LPT9";
}
