using System.Text.Json;
using FluentAssertions;
using HPD.Agent.Packages;

namespace HPD.Agent.Tests.Packages;

public sealed class HpdPackageManifestTests
{
    [Fact]
    public void Manifest_SerializesWithCamelCaseAndStringEnums()
    {
        var manifest = CreateManifest();

        var json = JsonSerializer.Serialize(
            manifest,
            HpdPackageManifestJsonContext.Default.HpdPackageManifest);

        json.Should().Contain("\"id\": \"hpd.github\"");
        json.Should().Contain("\"trust\": \"Trusted\"");
        json.Should().Contain("\"loadMode\": \"RuntimeInProcessDotNet\"");
        json.Should().Contain("\"targets\"");
        json.Should().Contain("\"backend\"");
        json.Should().Contain("\"packageType\": \"HPD.Package.GitHub.GitHubPackage\"");
    }

    [Fact]
    public void Manifest_RoundTripsThroughSourceGeneratedContext()
    {
        var manifest = CreateManifest();
        var json = JsonSerializer.Serialize(
            manifest,
            HpdPackageManifestJsonContext.Default.HpdPackageManifest);

        var roundTrip = JsonSerializer.Deserialize(
            json,
            HpdPackageManifestJsonContext.Default.HpdPackageManifest);

        roundTrip.Should().NotBeNull();
        roundTrip!.Id.Should().Be("hpd.github");
        roundTrip.Version.Should().Be(new Version(0, 1, 0));
        roundTrip.Trust.Should().Be(HpdPackageTrust.Trusted);
        roundTrip.LoadMode.Should().Be(HpdPackageLoadMode.RuntimeInProcessDotNet);
        roundTrip.Targets.Tui!.Required.Should().BeFalse();
        roundTrip.Targets.Backend!.Required.Should().BeTrue();
        roundTrip.Targets.External!.Entrypoint.Should().Be(HpdPackageEntrypointKind.Mcp);
        roundTrip.Entrypoints.DotNet!.Assembly.Should().Be("HPD.Package.GitHub.dll");
        roundTrip.Entrypoints.Mcp.Should().ContainSingle(entrypoint =>
            entrypoint.Name == "github" &&
            entrypoint.Command == "hpd-github-mcp");
        roundTrip.Contributes.Agent.Should().BeTrue();
        roundTrip.Contributes.Tui.Should().BeTrue();
    }

    [Fact]
    public void HpdPackage_BaseIdentityComesFromManifest()
    {
        var package = new ManifestBackedPackage();

        package.Id.Should().Be("hpd.manifest-backed");
        package.DisplayName.Should().Be("Manifest Backed");
        package.Version.Should().Be(new Version(1, 0, 0));
    }

    private static HpdPackageManifest CreateManifest() => new(
        "hpd.github",
        "GitHub",
        new Version(0, 1, 0))
    {
        HostCompatibility = new HpdPackageHostCompatibility
        {
            HpdAgent = ">=0.1.0",
            HpdAgentTui = ">=0.1.0"
        },
        Trust = HpdPackageTrust.Trusted,
        LoadMode = HpdPackageLoadMode.RuntimeInProcessDotNet,
        Targets = new HpdPackageTargets
        {
            Tui = new HpdPackageTarget
            {
                Required = false,
                Entrypoint = HpdPackageEntrypointKind.DotNet
            },
            Backend = new HpdPackageTarget
            {
                Required = true,
                Entrypoint = HpdPackageEntrypointKind.DotNet
            },
            External = new HpdPackageTarget
            {
                Required = false,
                Entrypoint = HpdPackageEntrypointKind.Mcp
            }
        },
        Entrypoints = new HpdPackageEntrypoints
        {
            DotNet = new HpdDotNetPackageEntrypoint
            {
                Assembly = "HPD.Package.GitHub.dll",
                PackageType = "HPD.Package.GitHub.GitHubPackage"
            },
            Mcp =
            [
                new HpdMcpPackageEntrypoint
                {
                    Name = "github",
                    Command = "hpd-github-mcp",
                    Args = ["--stdio"]
                }
            ]
        },
        Contributes = new HpdPackageContributes
        {
            Agent = true,
            Tui = true,
            Providers = false,
            McpServers = true,
            Prompts = true,
            Skills = true
        }
    };

    private sealed class ManifestBackedPackage : HpdPackage
    {
        public override HpdPackageManifest Manifest { get; } = new(
            "hpd.manifest-backed",
            "Manifest Backed",
            new Version(1, 0, 0));

        public override void Configure(IHpdPackageBuilder builder)
        {
        }
    }
}
