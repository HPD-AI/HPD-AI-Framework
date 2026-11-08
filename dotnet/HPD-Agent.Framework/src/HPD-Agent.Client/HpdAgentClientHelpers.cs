using System.Globalization;

namespace HPD.Agent.Client;

/// <summary>Wire-safe formatting used by generated path and query bindings.</summary>
public static class HpdAgentClientHelpers
{
    public static string Path(object? value) =>
        Uri.EscapeDataString(value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        });

    public static string Query(object? value) =>
        value switch
        {
            null => string.Empty,
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
}
