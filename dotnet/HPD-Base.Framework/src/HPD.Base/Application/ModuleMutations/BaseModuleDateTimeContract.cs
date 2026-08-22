using System.Globalization;
using System.Text.Json;

namespace HPD.Base;

internal static class BaseModuleDateTimeContract
{
    internal const string Format = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    internal static bool TryRead(JsonElement value, out DateTimeOffset result)
    {
        result = default;
        return value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParseExact(
                value.GetString(),
                Format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out result)
            && result.Offset == TimeSpan.Zero;
    }
}
