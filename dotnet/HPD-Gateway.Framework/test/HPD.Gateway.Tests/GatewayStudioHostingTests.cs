using FluentAssertions;
using HPD.AI.Platform.Studio;
using HPD.Gateway.ControlPlane;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayStudioHostingTests
{
    [Fact]
    public void Edition_contribution_is_content_addressed_and_optional()
    {
        BaseStudioEditionModuleAssetContribution contribution =
            GatewayStudioModuleRegistry.CreateEditionAssetContribution();

        contribution.ModuleId.Should().Be("gateway");
        contribution.ModuleVersion.Should().Be(1);
        contribution.Asset.Necessity.Should().Be(BaseStudioModuleNecessity.Optional);
        contribution.Asset.EntryModulePath.Should().MatchRegex(
            "^gateway/[0-9a-f]{64}\\.js$");
        contribution.Asset.Assets.Should().ContainSingle();
        contribution.Asset.Assets[0].Length.Should().BeGreaterThan(1);
        Convert.ToHexString(contribution.FrontendAbiChecksum.ToArray()).ToLowerInvariant().Should().Be(
            "0fbbcdb6092c657371b74781d6064ef257a99f51fd3a7aaf5012f2a2db3c3e81");
    }

    [Fact]
    public void Frontend_asset_matches_the_exact_gateway_client_slot()
    {
        BaseStudioEditionModuleAssetContribution contribution =
            GatewayStudioModuleRegistry.CreateEditionAssetContribution();

        contribution.Asset.EntryExportName.Should().Be("activateStudioModule");
        contribution.Asset.GetType().Should().NotBeNull();
        contribution.Asset.ShellContractChecksum.Should().Be(BaseStudioShellContract.Current.Checksum);
    }

    [Fact]
    public void Registration_binds_the_generated_gateway_contract_and_shell_transport()
    {
        string[] pages = ["gateway.configure", "gateway.diagnose", "gateway.operate", "gateway.overview"];
        BaseStudioFrameworkClientRegistration client = GatewayStudioModuleRegistry.CreateClientRegistration(pages);
        client.Protocol.Should().Be(BaseStudioFrameworkClientProtocol.FrameworkGeneratedContractV1);
        client.TransportClass.Should().Be(BaseStudioFrameworkClientTransportClass.SameOriginShellAuthenticated);
        client.EndpointSurfaceId.Should().Be("gateway.admin.v1");
        client.Limits.MaximumOperations.Should().Be(23);
        client.OwningPageIds.Should().Equal(pages);
        Convert.ToHexString(client.GeneratedContractChecksum.ToArray()).ToLowerInvariant().Should().Be(
            "02c406f8c49752d24278f14e4db91694c8e84bf8ff2ef37b2e3feed81cdb21f7");
        Convert.ToHexString(client.OperationInventoryChecksum.ToArray()).ToLowerInvariant().Should().Be(
            "b577087395ac45ad1cd9ce74ca577ab6797591c5e7d5a564f31b826878f3b8bc");
    }
}
