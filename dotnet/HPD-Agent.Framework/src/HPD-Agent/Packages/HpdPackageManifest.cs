using System.Text.Json.Serialization;

namespace HPD.Agent.Packages;

public sealed record HpdPackageManifest(
    string Id,
    string DisplayName,
    Version Version)
{
    public HpdPackageHostCompatibility HostCompatibility { get; init; } = new();

    public HpdPackageTrust Trust { get; init; } = HpdPackageTrust.Unknown;

    public HpdPackageLoadMode LoadMode { get; init; } = HpdPackageLoadMode.BuildTimeInProcess;

    public HpdPackageTargets Targets { get; init; } = new();

    public HpdPackageEntrypoints Entrypoints { get; init; } = new();

    public HpdPackageContributes Contributes { get; init; } = new();
}

public sealed record HpdPackageHostCompatibility
{
    public string? HpdAgent { get; init; }

    public string? HpdAgentTui { get; init; }
}

public enum HpdPackageTrust
{
    Unknown,
    Trusted,
    OutOfProcess,
    SandboxRequired
}

public enum HpdPackageLoadMode
{
    BuildTimeInProcess,
    RuntimeInProcessDotNet,
    OutOfProcess
}

public sealed record HpdPackageTargets
{
    public HpdPackageTarget? Tui { get; init; }

    public HpdPackageTarget? Backend { get; init; }

    public HpdPackageTarget? External { get; init; }
}

public sealed record HpdPackageTarget
{
    public bool Required { get; init; }

    public HpdPackageEntrypointKind Entrypoint { get; init; } = HpdPackageEntrypointKind.DotNet;
}

public enum HpdPackageEntrypointKind
{
    DotNet,
    Mcp,
    Process
}

public sealed record HpdPackageEntrypoints
{
    public HpdDotNetPackageEntrypoint? DotNet { get; init; }

    public IReadOnlyList<HpdMcpPackageEntrypoint> Mcp { get; init; } = [];

    public HpdProcessPackageEntrypoint? Process { get; init; }
}

public sealed record HpdDotNetPackageEntrypoint
{
    public required string Assembly { get; init; }

    public required string PackageType { get; init; }
}

public sealed record HpdMcpPackageEntrypoint
{
    public required string Name { get; init; }

    public required string Command { get; init; }

    public IReadOnlyList<string> Args { get; init; } = [];
}

public sealed record HpdProcessPackageEntrypoint
{
    public required string Command { get; init; }

    public IReadOnlyList<string> Args { get; init; } = [];

    public string? Protocol { get; init; }
}

public sealed record HpdPackageContributes
{
    public bool Agent { get; init; }

    public bool Tui { get; init; }

    public bool Providers { get; init; }

    public bool McpServers { get; init; }

    public bool Prompts { get; init; }

    public bool Skills { get; init; }

    public bool Themes { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(HpdPackageManifest))]
[JsonSerializable(typeof(HpdPackageHostCompatibility))]
[JsonSerializable(typeof(HpdPackageTargets))]
[JsonSerializable(typeof(HpdPackageTarget))]
[JsonSerializable(typeof(HpdPackageEntrypoints))]
[JsonSerializable(typeof(HpdDotNetPackageEntrypoint))]
[JsonSerializable(typeof(HpdMcpPackageEntrypoint))]
[JsonSerializable(typeof(HpdProcessPackageEntrypoint))]
[JsonSerializable(typeof(HpdPackageContributes))]
public partial class HpdPackageManifestJsonContext : JsonSerializerContext;
