using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Sandbox;

public static class SandboxFunctionMetadata
{
    public static SandboxConfigOverride? TryGetOverride(AIFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return TryGetOverride(function.AdditionalProperties);
    }

    public static SandboxConfigOverride? TryGetOverride(IReadOnlyDictionary<string, object?> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        if (!properties.TryGetValue("IsSandboxable", out var isSandboxable) ||
            isSandboxable is not true)
        {
            return null;
        }

        return new SandboxConfigOverride
        {
            NetworkMode = GetNullableEnumProperty(properties, "SandboxNetworkMode"),
            AllowedDomains = GetStringArrayProperty(properties, "SandboxAllowedDomains"),
            DeniedDomains = GetStringArrayProperty(properties, "SandboxDeniedDomains"),
            AllowWrite = GetStringArrayProperty(properties, "SandboxAllowWrite"),
            DenyRead = GetStringArrayProperty(properties, "SandboxDenyRead"),
            AllowRead = GetStringArrayProperty(properties, "SandboxAllowRead"),
            DenyWrite = GetStringArrayProperty(properties, "SandboxDenyWrite"),
            AllowUnixSockets = GetStringArrayProperty(properties, "SandboxAllowUnixSockets"),
            AllowMachLookup = GetStringArrayProperty(properties, "SandboxAllowMachLookup"),
            AllowPty = GetNullableBoolProperty(properties, "SandboxAllowPty"),
            AllowLocalBinding = GetNullableBoolProperty(properties, "SandboxAllowLocalBinding"),
            AllowAllUnixSockets = GetNullableBoolProperty(properties, "SandboxAllowAllUnixSockets"),
            AllowMacOSTrustdLookup = GetNullableBoolProperty(properties, "SandboxAllowMacOSTrustdLookup"),
            AllowGitConfig = GetNullableBoolProperty(properties, "SandboxAllowGitConfig"),
            EnableWeakerNestedSandbox = GetNullableBoolProperty(properties, "SandboxEnableWeakerNestedSandbox"),
            MandatoryDenySearchDepth = GetNullableIntProperty(properties, "SandboxMandatoryDenySearchDepth"),
            IgnoreViolationPatterns = GetStringArrayProperty(properties, "SandboxIgnoreViolationPatterns"),
            AllowedEnvironmentVariables = GetStringArrayProperty(properties, "SandboxAllowedEnvironmentVariables")
        };
    }

    private static SandboxNetworkMode? GetNullableEnumProperty(
        IReadOnlyDictionary<string, object?> properties,
        string key)
    {
        if (!properties.TryGetValue(key, out var value))
            return null;

        return value switch
        {
            SandboxNetworkMode mode => mode,
            string text when Enum.TryParse<SandboxNetworkMode>(text, ignoreCase: true, out var mode) => mode,
            JsonElement { ValueKind: JsonValueKind.String } json when
                Enum.TryParse<SandboxNetworkMode>(json.GetString(), ignoreCase: true, out var mode) => mode,
            _ => null
        };
    }

    private static string[]? GetStringArrayProperty(
        IReadOnlyDictionary<string, object?> properties,
        string key)
    {
        if (!properties.TryGetValue(key, out var value))
            return null;

        return value switch
        {
            string[] strings => strings,
            IReadOnlyCollection<string> strings => strings.ToArray(),
            IEnumerable<string> strings => strings.ToArray(),
            string text => SplitCommaSeparated(text),
            JsonElement { ValueKind: JsonValueKind.Array } json =>
                json.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!)
                    .ToArray(),
            JsonElement { ValueKind: JsonValueKind.String } json => SplitCommaSeparated(json.GetString() ?? ""),
            _ => null
        };
    }

    private static bool? GetNullableBoolProperty(IReadOnlyDictionary<string, object?> properties, string key)
    {
        if (!properties.TryGetValue(key, out var value))
            return null;

        return value switch
        {
            bool boolean => boolean,
            string text when bool.TryParse(text, out var boolean) => boolean,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            _ => null
        };
    }

    private static int? GetNullableIntProperty(
        IReadOnlyDictionary<string, object?> properties,
        string key)
    {
        if (!properties.TryGetValue(key, out var value))
            return null;

        return value switch
        {
            int number => number,
            string text when int.TryParse(text, out var number) => number,
            JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetInt32(out var number) => number,
            _ => null
        };
    }

    private static string[] SplitCommaSeparated(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
