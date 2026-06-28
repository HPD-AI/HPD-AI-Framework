using System.Net;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent.Packages;

namespace HPD.Agent.AspNetCore.Packages.Tests;

public sealed class HpdAspNetCorePackageRuntimeClientTests
{
    [Fact]
    public async Task ListAsync_LoadsPackageCache()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            request.RequestUri!.PathAndQuery.Should().Be("/api/hpd-agent/packages");
            return JsonResponse(new HpdPackageListResponse(
            [
                Package("hpd.test.client")
            ]));
        });
        var runtime = new HpdAspNetCorePackageRuntimeClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        });

        var packages = await runtime.ListAsync();

        packages.Should().ContainSingle(package => package.Id == "hpd.test.client");
        runtime.Packages.Should().ContainSingle(package => package.Id == "hpd.test.client");
    }

    [Fact]
    public async Task EnableRegisteredAsync_PostsAndRaisesChanged()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            request.RequestUri!.PathAndQuery.Should()
                .Be("/api/hpd-agent/packages/hpd.test.client/enable?scope=workspace");
            return JsonResponse(new HpdPackageActionResponse(Package("hpd.test.client", scope: HpdPackageScopes.Workspace)));
        });
        var runtime = new HpdAspNetCorePackageRuntimeClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        });
        var changes = new List<HpdPackageChangedEventArgs>();
        runtime.Changed += (_, args) => changes.Add(args);

        var package = await runtime.EnableRegisteredAsync("hpd.test.client", HpdPackageScopes.Workspace);

        package.Id.Should().Be("hpd.test.client");
        runtime.Packages.Should().ContainSingle(candidate => candidate.Id == "hpd.test.client");
        changes.Should().ContainSingle(change =>
            change.Kind == HpdPackageChangeKind.Enabled &&
            change.Package.Id == "hpd.test.client");
    }

    [Fact]
    public async Task DisableAsync_RemovesCachedPackageAndRaisesChanged()
    {
        var call = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            call++;
            if (call == 1)
            {
                return JsonResponse(new HpdPackageActionResponse(Package("hpd.test.client")));
            }

            request.RequestUri!.PathAndQuery.Should()
                .Be("/api/hpd-agent/packages/hpd.test.client/disable");
            return JsonResponse(new HpdPackageDisableResponse("hpd.test.client", Disabled: true));
        });
        var runtime = new HpdAspNetCorePackageRuntimeClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        });
        await runtime.EnableRegisteredAsync("hpd.test.client");
        var changes = new List<HpdPackageChangedEventArgs>();
        runtime.Changed += (_, args) => changes.Add(args);

        var disabled = await runtime.DisableAsync("hpd.test.client");

        disabled.Should().BeTrue();
        runtime.Packages.Should().BeEmpty();
        changes.Should().ContainSingle(change =>
            change.Kind == HpdPackageChangeKind.Disabled &&
            change.Package.Id == "hpd.test.client");
    }

    [Fact]
    public async Task EnableRegisteredAsync_NotFound_ThrowsKeyNotFoundException()
    {
        var handler = new StubHttpMessageHandler(_ =>
            JsonResponse(
                new HpdPackageErrorResponse("Package missing."),
                HttpStatusCode.NotFound));
        var runtime = new HpdAspNetCorePackageRuntimeClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        });

        var act = async () => await runtime.EnableRegisteredAsync("missing");

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("Package missing.");
    }

    private static HpdPackageResponse Package(
        string id,
        string scope = HpdPackageScopes.App)
        => new(
            id,
            "Test Package",
            "1.0",
            scope,
            HpdPackageLoadState.Enabled.ToString(),
            HpdPackageTrust.Trusted.ToString(),
            HpdPackageLoadMode.BuildTimeInProcess.ToString(),
            new HpdPackageContributionSummaryResponse(
                ["test.agent"],
                [],
                [],
                [],
                []),
            [HpdPackageChangeImpact.FutureAgentBuilds.ToString()],
            []);

    private static HttpResponseMessage JsonResponse(
        object body,
        HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8,
                "application/json")
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(_respond(request));
    }
}
