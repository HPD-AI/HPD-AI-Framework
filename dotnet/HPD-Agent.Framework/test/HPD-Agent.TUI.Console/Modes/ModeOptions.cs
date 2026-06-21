namespace HPD.Agent.TUI.Console.Modes;

internal sealed class ModeOptions
{
    private readonly Dictionary<string, string> _values;

    private ModeOptions(Dictionary<string, string> values)
    {
        _values = values;
    }

    public static ModeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal) || arg.Length <= 2)
            {
                continue;
            }

            var key = arg[2..];
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
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
