using a2Cr.Core.Errors;

namespace a2Cr.Cli;

/// <summary>全局与各命令共用的选项解析；未知选项一律按用法错误处理。</summary>
internal sealed class CommandLine
{
    private static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "root", "type", "limit", "loader", "loader-version", "version", "instance",
        "memory", "java-major", "account", "username", "name"
    };

    private static readonly HashSet<string> FlagOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "json", "verbose", "yes", "no-progress", "refresh", "offline", "detach", "copy",
        "dry-run", "all"
    };

    private readonly Dictionary<string, string> _options;

    private CommandLine(Dictionary<string, string> options, List<string> positionals)
    {
        _options = options;
        Positionals = positionals;
    }

    public IReadOnlyList<string> Positionals { get; }

    public bool Json => Has("json");
    public bool Verbose => Has("verbose");
    public string? Root => Get("root");

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Get(string name) => _options.GetValueOrDefault(name);

    public int GetInt(string name, int fallback, int min, int max)
    {
        var value = Get(name);
        if (value is null) return fallback;
        if (!int.TryParse(value, out var parsed) || parsed < min || parsed > max)
        {
            throw new UsageException($"选项 --{name} 需要 {min}–{max} 之间的整数。");
        }

        return parsed;
    }

    public int? GetOptionalInt(string name, int min, int max)
    {
        var value = Get(name);
        if (value is null) return null;
        if (!int.TryParse(value, out var parsed) || parsed < min || parsed > max)
        {
            throw new UsageException($"选项 --{name} 需要 {min}–{max} 之间的整数。");
        }

        return parsed;
    }

    public static CommandLine Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(argument);
                continue;
            }

            var body = argument[2..];
            if (body.Length == 0) throw new UsageException("无法识别的选项 --。", "使用 --help 查看用法。");
            var equals = body.IndexOf('=');
            var name = equals >= 0 ? body[..equals] : body;
            var inlineValue = equals >= 0 ? body[(equals + 1)..] : null;
            if (ValueOptions.Contains(name))
            {
                if (inlineValue is null)
                {
                    if (index + 1 >= args.Length)
                    {
                        throw new UsageException($"选项 --{name} 需要一个值。", "使用 --help 查看用法。");
                    }

                    inlineValue = args[++index];
                }

                options[name] = inlineValue;
            }
            else if (FlagOptions.Contains(name))
            {
                if (inlineValue is not null) throw new UsageException($"选项 --{name} 不接受值。");
                options[name] = "true";
            }
            else
            {
                throw new UsageException($"未知选项 --{name}。", "使用 --help 查看用法。");
            }
        }

        return new CommandLine(options, positionals);
    }
}
