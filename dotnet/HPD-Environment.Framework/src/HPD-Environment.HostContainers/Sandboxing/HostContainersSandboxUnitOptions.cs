namespace HPD.Environment.HostContainers;

using System.Text;
using System.Text.Json;
using HPD.Environment.Contracts;

/// <summary>Network profile applied to the retained sandbox container.</summary>
public enum HostContainersSandboxNetworkMode
{
    None,
    EngineDefault,
}

/// <summary>One validated host directory exposed in the sandbox container.</summary>
public sealed record HostContainersSandboxMount
{
    /// <summary>Absolute source directory on the local HPD host.</summary>
    public required HostPath HostSource { get; init; }

    /// <summary>Absolute destination directory inside the container.</summary>
    public required GuestPath ContainerPath { get; init; }

    /// <summary>Read-only or read-write projection access.</summary>
    public AccessMode Access { get; init; } = AccessMode.ReadOnly;
}

/// <summary>
/// Provider extension that switches one Host Containers execution unit from
/// the legacy host process topology to a retained sandbox container.
/// </summary>
public sealed record HostContainersSandboxUnitOptions
{
    /// <summary>Container image already available to the selected engine.</summary>
    public required string Image { get; init; }

    /// <summary>Default working directory inside the container.</summary>
    public GuestPath WorkingDirectory { get; init; } =
        new("/workspace");

    /// <summary>Workspace directories bound into the retained container.</summary>
    public IReadOnlyList<HostContainersSandboxMount> Mounts { get; init; } =
        Array.Empty<HostContainersSandboxMount>();

    /// <summary>Container network profile.</summary>
    public HostContainersSandboxNetworkMode NetworkMode { get; init; } =
        HostContainersSandboxNetworkMode.None;

    /// <summary>Environment variables injected into the container.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        EmptyContainerValues.Instance;

    /// <summary>
    /// Command that keeps the container alive while Agent processes execute
    /// through the retained namespace.
    /// </summary>
    public IReadOnlyList<string> KeepAliveCommand { get; init; } =
    [
        "/bin/sh",
        "-c",
        "trap 'exit 0' TERM INT; while :; do sleep 3600; done",
    ];

    /// <summary>Makes the container root filesystem read-only.</summary>
    public bool ReadOnlyRootFilesystem { get; init; } = true;

    /// <summary>Container paths mounted as private writable tmpfs areas.</summary>
    public IReadOnlyList<GuestPath> TmpfsPaths { get; init; } =
    [
        new("/tmp"),
    ];

    /// <summary>Optional container user identity.</summary>
    public string? User { get; init; }

    private sealed class EmptyContainerValues :
        Dictionary<string, string>
    {
        public static EmptyContainerValues Instance { get; } =
            new();

        private EmptyContainerValues()
            : base(StringComparer.Ordinal)
        {
        }
    }
}

/// <summary>
/// Encodes and decodes the Host Containers sandbox-unit provider extension.
/// </summary>
public static class HostContainersSandboxUnitExtension
{
    /// <summary>Stable provider-extension schema.</summary>
    public static SchemaId Schema { get; } =
        new("hpd.execution.host-containers.sandbox-unit.v1");

    /// <summary>Adds a validated sandbox-unit extension to an execution spec.</summary>
    public static ExecutionUnitSpec WithSandbox(
        this ExecutionUnitSpec spec,
        HostContainersSandboxUnitOptions options)
    {
        ArgumentNullException.ThrowIfNull(spec);
        Validate(options);
        return spec with
        {
            ProviderExtensions =
            [
                .. spec.ProviderExtensions.Where(extension =>
                    !extension.SchemaId.Equals(Schema)),
                new ProviderExtensionData(
                    HostContainersProviderDescriptor.ProviderId,
                    Schema,
                    new ContentType("application/json"),
                    Encode(options)),
            ],
        };
    }

    /// <summary>Reads a sandbox-unit extension or returns null for legacy units.</summary>
    public static HostContainersSandboxUnitOptions? TryRead(
        ExecutionUnitSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ProviderExtensionData? extension =
            spec.ProviderExtensions
                .Where(candidate =>
                candidate.ProviderId.Equals(
                    HostContainersProviderDescriptor.ProviderId) &&
                candidate.SchemaId.Equals(Schema))
                .Cast<ProviderExtensionData?>()
                .SingleOrDefault();
        if (extension is null)
        {
            return null;
        }
        HostContainersSandboxUnitOptions options =
            Decode(extension.Value.Payload);
        Validate(options);
        return options;
    }

    /// <summary>Validates all values before they reach a container-engine command.</summary>
    public static void Validate(
        HostContainersSandboxUnitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Image) ||
            options.Image.Length > 512 ||
            options.Image.Any(character =>
                char.IsWhiteSpace(character) ||
                char.IsControl(character)))
        {
            throw new ArgumentException(
                "Sandbox container image must be a non-empty engine image reference without whitespace or control characters.",
                nameof(options));
        }
        ValidateContainerPath(
            options.WorkingDirectory.Value,
            nameof(options.WorkingDirectory));
        if (options.KeepAliveCommand.Count == 0 ||
            options.KeepAliveCommand.Any(argument =>
                argument.Contains('\0')))
        {
            throw new ArgumentException(
                "Sandbox container keep-alive command must contain at least one NUL-free argument.",
                nameof(options));
        }
        HashSet<string> containerPaths =
            new(StringComparer.Ordinal);
        foreach (HostContainersSandboxMount mount in options.Mounts)
        {
            string source = Path.GetFullPath(mount.HostSource.Value);
            if (!Path.IsPathFullyQualified(source) ||
                !Directory.Exists(source))
            {
                throw new ArgumentException(
                    $"Sandbox mount source '{source}' must be an existing absolute directory.",
                    nameof(options));
            }
            ValidateContainerPath(
                mount.ContainerPath.Value,
                nameof(options.Mounts));
            if (!containerPaths.Add(mount.ContainerPath.Value))
            {
                throw new ArgumentException(
                    $"Sandbox mount destination '{mount.ContainerPath.Value}' is duplicated.",
                    nameof(options));
            }
            if (mount.Access is not (
                AccessMode.ReadOnly or
                AccessMode.ReadWrite))
            {
                throw new ArgumentException(
                    $"Sandbox mount access mode '{mount.Access}' is unsupported.",
                    nameof(options));
            }
        }
        foreach ((string name, string value) in options.Environment)
        {
            if (!IsEnvironmentName(name) ||
                value.Contains('\0'))
            {
                throw new ArgumentException(
                    $"Sandbox environment entry '{name}' is invalid.",
                    nameof(options));
            }
        }
        if (options.User is { Length: > 256 } ||
            options.User?.Contains('\0') == true)
        {
            throw new ArgumentException(
                "Sandbox user identity is invalid.",
                nameof(options));
        }
        foreach (GuestPath path in options.TmpfsPaths)
        {
            ValidateContainerPath(
                path.Value,
                nameof(options.TmpfsPaths));
        }
    }

    private static byte[] Encode(
        HostContainersSandboxUnitOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("image", options.Image);
            writer.WriteString(
                "workingDirectory",
                options.WorkingDirectory.Value);
            writer.WriteString(
                "networkMode",
                options.NetworkMode.ToString());
            writer.WriteBoolean(
                "readOnlyRootFilesystem",
                options.ReadOnlyRootFilesystem);
            if (options.User is not null)
            {
                writer.WriteString("user", options.User);
            }
            writer.WriteStartArray("mounts");
            foreach (HostContainersSandboxMount mount in
                     options.Mounts)
            {
                writer.WriteStartObject();
                writer.WriteString(
                    "hostSource",
                    Path.GetFullPath(mount.HostSource.Value));
                writer.WriteString(
                    "containerPath",
                    mount.ContainerPath.Value);
                writer.WriteString(
                    "access",
                    mount.Access.ToString());
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartObject("environment");
            foreach ((string name, string value) in
                     options.Environment.OrderBy(
                         pair => pair.Key,
                         StringComparer.Ordinal))
            {
                writer.WriteString(name, value);
            }
            writer.WriteEndObject();
            writer.WriteStartArray("keepAliveCommand");
            foreach (string argument in options.KeepAliveCommand)
            {
                writer.WriteStringValue(argument);
            }
            writer.WriteEndArray();
            writer.WriteStartArray("tmpfsPaths");
            foreach (GuestPath path in options.TmpfsPaths)
            {
                writer.WriteStringValue(path.Value);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static HostContainersSandboxUnitOptions Decode(
        ReadOnlyMemory<byte> payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        var mounts = new List<HostContainersSandboxMount>();
        foreach (JsonElement mount in
                 root.GetProperty("mounts").EnumerateArray())
        {
            mounts.Add(new HostContainersSandboxMount
            {
                HostSource = new HostPath(
                    mount.GetProperty("hostSource").GetString()!),
                ContainerPath = new GuestPath(
                    mount.GetProperty("containerPath").GetString()!),
                Access = Enum.Parse<AccessMode>(
                    mount.GetProperty("access").GetString()!,
                    ignoreCase: false),
            });
        }
        var environment =
            new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in
                 root.GetProperty("environment").EnumerateObject())
        {
            environment[property.Name] =
                property.Value.GetString()!;
        }
        return new HostContainersSandboxUnitOptions
        {
            Image = root.GetProperty("image").GetString()!,
            WorkingDirectory = new GuestPath(
                root.GetProperty(
                    "workingDirectory").GetString()!),
            NetworkMode = Enum.Parse<
                HostContainersSandboxNetworkMode>(
                root.GetProperty(
                    "networkMode").GetString()!,
                ignoreCase: false),
            ReadOnlyRootFilesystem =
                root.GetProperty(
                    "readOnlyRootFilesystem").GetBoolean(),
            User = root.TryGetProperty(
                    "user",
                    out JsonElement user) &&
                user.ValueKind == JsonValueKind.String
                    ? user.GetString()
                    : null,
            Mounts = mounts,
            Environment = environment,
            KeepAliveCommand = root.GetProperty(
                    "keepAliveCommand")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray(),
            TmpfsPaths = root.GetProperty("tmpfsPaths")
                .EnumerateArray()
                .Select(value =>
                    new GuestPath(value.GetString()!))
                .ToArray(),
        };
    }

    private static void ValidateContainerPath(
        string value,
        string parameterName)
    {
        string[] segments = value.Split('/');
        if (!value.StartsWith('/') ||
            value.Contains('\0') ||
            value.Contains('\\') ||
            segments.Any(segment =>
                segment is "." or ".."))
        {
            throw new ArgumentException(
                $"Container path '{value}' must be an absolute normalized POSIX path.",
                parameterName);
        }
    }

    private static bool IsEnvironmentName(string value)
    {
        if (value.Length == 0 ||
            !(value[0] == '_' || char.IsAsciiLetter(value[0])))
        {
            return false;
        }
        foreach (char character in value.AsSpan(1))
        {
            if (character != '_' &&
                !char.IsAsciiLetterOrDigit(character))
            {
                return false;
            }
        }
        return true;
    }
}
