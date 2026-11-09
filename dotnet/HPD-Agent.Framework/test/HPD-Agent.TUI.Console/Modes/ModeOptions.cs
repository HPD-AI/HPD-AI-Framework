namespace HPD.Agent.TUI.Console.Modes;

internal sealed class ModeOptions
{
    private static readonly HashSet<string> BooleanOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "list-tools"
    };

    private readonly Dictionary<string, string> _values;

    private ModeOptions(Dictionary<string, string> values)
    {
        _values = values;
    }

    public static ModeOptions Parse(string[] args, params string[] allowedOptions)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allowed = new HashSet<string>(allowedOptions, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal) || arg.Length <= 2)
            {
                throw new ModeOptionException($"Unexpected argument: {arg}");
            }

            var key = arg[2..];
            if (!allowed.Contains(key))
            {
                throw new ModeOptionException($"Unknown option: --{key}");
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                if (!BooleanOptions.Contains(key))
                {
                    throw new ModeOptionException($"Option --{key} requires a value.");
                }

                values[key] = "true";
                continue;
            }

            values[key] = args[++i];
        }

        return new ModeOptions(values);
    }

    public string Get(string key, string fallback)
        => _values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    public bool Has(string key)
        => _values.ContainsKey(key);
}

internal sealed class ModeOptionException(string message) : ArgumentException(message);
