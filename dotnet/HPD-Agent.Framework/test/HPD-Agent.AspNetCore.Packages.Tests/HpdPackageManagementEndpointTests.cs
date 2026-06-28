using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HPD.Agent.Packages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.AspNetCore.Packages.Tests;

public sealed class HpdPackageManagementEndpointTests
{
    [Fact]
    public async Task GetPackages_ReturnsLoadedPackages()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var response = await client.GetFromJsonAsync<HpdPackageListResponse>("/packages");

        response.Should().NotBeNull();
        response!.Packages.Should().BeEmpty();
    }

    [Fact]
    public async Task EnableRegisteredPackage_EnablesPackageAndListShowsIt()
    {
        var packageId = $"hpd.test.endpoint.{Guid.NewGuid():N}";
        HpdPackageRegistry.Register(new EndpointTestPackage(packageId));
        using var server = CreateServer();
        using var client = server.CreateClient();

        var enable = await client.PostAsync($"/packages/{packageId}/enable?scope=workspace", null);
        var list = await client.GetFromJsonAsync<HpdPackageListResponse>("/packages");

        enable.StatusCode.Should().Be(HttpStatusCode.OK);
        var action = await enable.Content.ReadFromJsonAsync<HpdPackageActionResponse>();
        action.Should().NotBeNull();
        action!.Package.Id.Should().Be(packageId);
        action.Package.Scope.Should().Be(HpdPackageScopes.Workspace);
        action.Package.Contributions.AgentContributors.Should().Contain("endpoint.agent");
        list.Should().NotBeNull();
        list!.Packages.Should().ContainSingle(package =>
            package.Id == packageId &&
            package.Scope == HpdPackageScopes.Workspace);
    }

    [Fact]
    public async Task DisablePackage_RemovesLoadedPackage()
    {
        var packageId = $"hpd.test.endpoint.{Guid.NewGuid():N}";
        HpdPackageRegistry.Register(new EndpointTestPackage(packageId));
        using var server = CreateServer();
        using var client = server.CreateClient();
        await client.PostAsync($"/packages/{packageId}/enable", null);

        var disable = await client.PostAsync($"/packages/{packageId}/disable", null);
        var list = await client.GetFromJsonAsync<HpdPackageListResponse>("/packages");

        disable.StatusCode.Should().Be(HttpStatusCode.OK);
        var disabled = await disable.Content.ReadFromJsonAsync<HpdPackageDisableResponse>();
        disabled.Should().Be(new HpdPackageDisableResponse(packageId, Disabled: true));
        list.Should().NotBeNull();
        list!.Packages.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAndCommitPackage_ValidatesThenEnablesPackage()
    {
        var packageId = $"hpd.test.endpoint.{Guid.NewGuid():N}";
        HpdPackageRegistry.Register(new EndpointTestPackage(packageId));
        using var server = CreateServer();
        using var client = server.CreateClient();

        var prepare = await client.PostAsync($"/packages/{packageId}/prepare?scope=workspace", null);
        var prepareBody = await prepare.Content.ReadFromJsonAsync<HpdPackagePrepareResponse>();

        prepare.StatusCode.Should().Be(HttpStatusCode.OK);
        prepareBody.Should().NotBeNull();
        prepareBody!.CanCommit.Should().BeTrue();
        prepareBody.Contributions.AgentContributors.Should().Contain("endpoint.agent");

        var commit = await client.PostAsJsonAsync(
            "/packages/commit",
            new HpdPackagePrepareRequest(packageId, HpdPackageScopes.Workspace));
        var action = await commit.Content.ReadFromJsonAsync<HpdPackageActionResponse>();

        commit.StatusCode.Should().Be(HttpStatusCode.OK);
        action.Should().NotBeNull();
        action!.Package.Id.Should().Be(packageId);
        action.Package.Scope.Should().Be(HpdPackageScopes.Workspace);
    }

    [Fact]
    public async Task PrepareConflictingPackage_ReturnsDiagnosticsWithoutMutatingLoadedPackages()
    {
        var activePackageId = $"hpd.test.endpoint.active.{Guid.NewGuid():N}";
        var conflictingPackageId = $"hpd.test.endpoint.conflict.{Guid.NewGuid():N}";
        HpdPackageRegistry.Register(new EndpointTestPackage(activePackageId));
        HpdPackageRegistry.Register(new EndpointTestPackage(conflictingPackageId));
        using var server = CreateServer();
        using var client = server.CreateClient();
        await client.PostAsync($"/packages/{activePackageId}/enable?scope=workspace", null);

        var prepare = await client.PostAsync(
            $"/packages/{conflictingPackageId}/prepare?scope=workspace",
            null);
        var prepareBody = await prepare.Content.ReadFromJsonAsync<HpdPackagePrepareResponse>();
        var list = await client.GetFromJsonAsync<HpdPackageListResponse>("/packages");

        prepare.StatusCode.Should().Be(HttpStatusCode.OK);
        prepareBody.Should().NotBeNull();
        prepareBody!.CanCommit.Should().BeFalse();
        prepareBody.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "HPD_PACKAGE_CONFLICT" &&
            diagnostic.Message.Contains("endpoint.agent", StringComparison.Ordinal));
        list.Should().NotBeNull();
        list!.Packages.Should().ContainSingle(package => package.Id == activePackageId);
        list.Packages.Should().NotContain(package => package.Id == conflictingPackageId);
    }

    [Fact]
    public async Task EnableUnknownPackage_ReturnsNotFound()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var response = await client.PostAsync("/packages/missing/enable", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<HpdPackageErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Contain("missing");
    }

    private static TestServer CreateServer()
    {
        var builder = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddHPDAgentPackageManagement();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapHPDAgentPackageManagement("/packages");
                });
            });

        return new TestServer(builder);
    }

    private sealed class EndpointTestPackage : HpdPackage
    {
        public EndpointTestPackage(string id)
        {
            Manifest = new HpdPackageManifest(id, "Endpoint Test", new Version(1, 0));
        }

        public override HpdPackageManifest Manifest { get; }

        public override void Configure(IHpdPackageBuilder builder)
        {
            builder.AddAgentContributor("endpoint.agent", new DelegateAgentBuilderContributor(_ => { }));
        }
    }
}
