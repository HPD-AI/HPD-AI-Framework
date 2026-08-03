using System.Security.Cryptography;
using System.Text;

namespace HPD.Agent.Providers;

internal static class ProviderClientFingerprint
{
    internal static string? Combine(
        string? providerConfigFingerprint,
        IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0)
            return providerConfigFingerprint;

        var normalizedHeaders = string.Join(
            "\n",
            headers.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => $"{pair.Key.ToLowerInvariant()}:{pair.Value}"));
        var value = $"{providerConfigFingerprint ?? string.Empty}\n{normalizedHeaders}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
