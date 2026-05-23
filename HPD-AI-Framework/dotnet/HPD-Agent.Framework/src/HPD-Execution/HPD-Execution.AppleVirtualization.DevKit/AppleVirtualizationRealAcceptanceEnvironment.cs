namespace HPD.Execution.AppleVirtualization.DevKit;

using System.Collections.ObjectModel;
using HPD.Execution.Contracts;

public sealed record AppleVirtualizationRealAcceptanceEnvironment
{
    public required string SourcePath { get; init; }
    public required IReadOnlyDictionary<string, string> Variables { get; init; }
    public required string HelperPath { get; init; }
    public required string GuestKernelPath { get; init; }
    public required string GuestInitrdPath { get; init; }
    public required string GuestDiskPath { get; init; }
    public required string GuestSerialLogPath { get; init; }
    public required string ExpectedGuestAgentVersion { get; init; }
    public required EngineControlPlaneKind EngineKind { get; init; }
    public required EngineApiKind EngineApi { get; init; }
    public required EngineAuthorityMode AuthorityMode { get; init; }
    public required string EngineSocketLocus { get; init; }
    public required string EngineSocketPath { get; init; }
    public required string SmokeImage { get; init; }
    public string? GuestBundleRoot { get; init; }
    public string? GuestKernelCommandLine { get; init; }
    public string? VirtiofsHostPath { get; init; }
    public string? VirtiofsTag { get; init; }
    public bool EngineProvisioningEnabled { get; init; }
    public bool EngineProvisioningAllowPackageInstall { get; init; }
    public bool EngineProvisioningAllowServiceEnablement { get; init; }

    public static AppleVirtualizationRealAcceptanceEnvironmentLoadResult Load(string envFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envFilePath);

        List<AppleVirtualizationDevKitDiagnostic> diagnostics = [];
        if (!File.Exists(envFilePath))
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.EnvFileMissing",
                "The real acceptance env file does not exist.",
                path: envFilePath));
            return new(null, new AppleVirtualizationDevKitValidationResult { IsValid = false, Diagnostics = diagnostics });
        }

        Dictionary<string, string> variables = new(StringComparer.Ordinal);
        int lineNumber = 0;
        foreach (string line in File.ReadLines(envFilePath))
        {
            lineNumber++;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            if (!TryParseExportLine(trimmed.AsSpan(), out string? name, out string? value))
            {
                diagnostics.Add(Error(
                    "AppleVirtualization.DevKit.EnvLineInvalid",
                    $"The env file contains an unsupported line at {lineNumber}.",
                    path: envFilePath));
                continue;
            }

            variables[name] = value;
        }

        string required(string variable)
        {
            if (variables.TryGetValue(variable, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.RequiredVariableMissing",
                $"{variable} is required.",
                variable: variable,
                path: envFilePath));
            return string.Empty;
        }

        EngineControlPlaneKind engineKind = ParseEnum(
            required("HPD_APPLEVZ_CONTAINER_ENGINE_KIND"),
            EngineControlPlaneKind.DockerCompatible,
            "HPD_APPLEVZ_CONTAINER_ENGINE_KIND",
            diagnostics);
        EngineApiKind engineApi = ParseEnum(
            required("HPD_APPLEVZ_CONTAINER_ENGINE_API"),
            EngineApiKind.DockerCompatible,
            "HPD_APPLEVZ_CONTAINER_ENGINE_API",
            diagnostics);
        EngineAuthorityMode authorityMode = ParseEnum(
            required("HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE"),
            EngineAuthorityMode.Rootless,
            "HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE",
            diagnostics);

        AppleVirtualizationRealAcceptanceEnvironment environment = new()
        {
            SourcePath = envFilePath,
            Variables = new ReadOnlyDictionary<string, string>(variables),
            HelperPath = required("HPD_APPLEVZ_REAL_HELPER_PATH"),
            GuestKernelPath = required("HPD_APPLEVZ_GUEST_KERNEL"),
            GuestInitrdPath = required("HPD_APPLEVZ_GUEST_INITRD"),
            GuestDiskPath = required("HPD_APPLEVZ_GUEST_DISK"),
            GuestSerialLogPath = required("HPD_APPLEVZ_GUEST_SERIAL_LOG"),
            ExpectedGuestAgentVersion = required("HPD_APPLEVZ_EXPECTED_GUEST_AGENT_VERSION"),
            EngineKind = engineKind,
            EngineApi = engineApi,
            AuthorityMode = authorityMode,
            EngineSocketLocus = GetOrDefault(variables, "HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_LOCUS", "runtime-host"),
            EngineSocketPath = required("HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH"),
            SmokeImage = required("HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE"),
            GuestBundleRoot = NullIfWhiteSpace(GetOrDefault(variables, "HPD_APPLEVZ_GUEST_BUNDLE_ROOT", string.Empty)),
            GuestKernelCommandLine = NullIfWhiteSpace(GetOrDefault(variables, "HPD_APPLEVZ_GUEST_KERNEL_CMDLINE", string.Empty)),
            VirtiofsHostPath = NullIfWhiteSpace(GetOrDefault(variables, "HPD_APPLEVZ_VIRTIOFS_HOST_PATH", string.Empty)),
            VirtiofsTag = NullIfWhiteSpace(GetOrDefault(variables, "HPD_APPLEVZ_VIRTIOFS_TAG", string.Empty)),
            EngineProvisioningEnabled = ParseBool(GetOrDefault(variables, "HPD_APPLEVZ_ENGINE_PROVISIONING_ENABLED", "false"), "HPD_APPLEVZ_ENGINE_PROVISIONING_ENABLED", diagnostics),
            EngineProvisioningAllowPackageInstall = ParseBool(GetOrDefault(variables, "HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_PACKAGE_INSTALL", "false"), "HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_PACKAGE_INSTALL", diagnostics),
            EngineProvisioningAllowServiceEnablement = ParseBool(GetOrDefault(variables, "HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_SERVICE_ENABLEMENT", "false"), "HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_SERVICE_ENABLEMENT", diagnostics)
        };

        return new(
            diagnostics.Count == 0 ? environment : null,
            new AppleVirtualizationDevKitValidationResult { IsValid = diagnostics.Count == 0, Diagnostics = diagnostics });
    }

    internal static bool TryParseExportLine(ReadOnlySpan<char> line, out string name, out string value)
    {
        const string exportPrefix = "export ";
        name = string.Empty;
        value = string.Empty;

        if (!line.StartsWith(exportPrefix.AsSpan(), StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> assignment = line[exportPrefix.Length..];
        int equals = assignment.IndexOf('=');
        if (equals <= 0)
        {
            return false;
        }

        name = assignment[..equals].Trim().ToString();
        value = UnescapeShellValue(assignment[(equals + 1)..].Trim());
        return IsValidVariableName(name);
    }

    private static string UnescapeShellValue(ReadOnlySpan<char> value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"')))
        {
            value = value[1..^1];
        }

        Span<char> stack = stackalloc char[Math.Min(value.Length, 512)];
        char[]? rented = null;
        Span<char> buffer = value.Length <= stack.Length ? stack : rented = new char[value.Length];
        int written = 0;
        bool escaped = false;
        foreach (char ch in value)
        {
            if (escaped)
            {
                buffer[written++] = ch;
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            buffer[written++] = ch;
        }

        if (escaped)
        {
            buffer[written++] = '\\';
        }

        string result = new(buffer[..written]);
        if (rented is not null)
        {
            Array.Clear(rented);
        }

        return result;
    }

    private static bool IsValidVariableName(string name)
    {
        if (name.Length == 0 || (!char.IsAsciiLetter(name[0]) && name[0] != '_'))
        {
            return false;
        }

        for (int i = 1; i < name.Length; i++)
        {
            char ch = name[i];
            if (!char.IsAsciiLetterOrDigit(ch) && ch != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static string GetOrDefault(Dictionary<string, string> values, string name, string defaultValue) =>
        values.TryGetValue(name, out string? value) ? value : defaultValue;

    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static TEnum ParseEnum<TEnum>(
        string value,
        TEnum defaultValue,
        string variable,
        List<AppleVirtualizationDevKitDiagnostic> diagnostics)
        where TEnum : struct
    {
        if (Enum.TryParse(value, ignoreCase: false, out TEnum parsed))
        {
            return parsed;
        }

        diagnostics.Add(Error(
            "AppleVirtualization.DevKit.EnumVariableInvalid",
            $"{variable} has an invalid value: {value}.",
            variable: variable));
        return defaultValue;
    }

    private static bool ParseBool(string value, string variable, List<AppleVirtualizationDevKitDiagnostic> diagnostics)
    {
        if (bool.TryParse(value, out bool parsed))
        {
            return parsed;
        }

        diagnostics.Add(Error(
            "AppleVirtualization.DevKit.BoolVariableInvalid",
            $"{variable} must be true or false.",
            variable: variable));
        return false;
    }

    internal static AppleVirtualizationDevKitDiagnostic Error(
        string code,
        string message,
        string? variable = null,
        string? path = null) =>
        new()
        {
            Severity = AppleVirtualizationDevKitDiagnosticSeverity.Error,
            Code = code,
            Message = message,
            Variable = variable,
            Path = path
        };
}

public sealed record AppleVirtualizationRealAcceptanceEnvironmentLoadResult(
    AppleVirtualizationRealAcceptanceEnvironment? Environment,
    AppleVirtualizationDevKitValidationResult Validation);
