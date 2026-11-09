using FluentAssertions;
using HPD.Agent.Bots.AspNetCore;
using HPD.Agent.Bots.AspNetCore.EndpointMapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Bots.Tests.Unit.EndpointMapping;

public class MapHpdBotsTests
{
    [Fact]
    public void MapHPDBots_UsesRegistrationDefaultInsteadOfOverridingIt()
    {
        string? mappedPath = "not-called";
        using var services = new ServiceCollection()
            .AddSingleton<IBotRegistryProvider>(new TestRegistryProvider(
                new BotRegistration(
                    "teams",
                    typeof(object),
                    (_, path) =>
                    {
                        mappedPath = path;
                        return new NoopEndpointConventionBuilder();
                    },
                    "/api/messages")))
            .BuildServiceProvider();
        var endpoints = new TestEndpointRouteBuilder(services);

        endpoints.MapHPDBots();

        mappedPath.Should().BeNull();
    }

    private sealed class TestRegistryProvider(BotRegistration registration) : IBotRegistryProvider
    {
        public IEnumerable<BotRegistration> GetAll() => [registration];
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider)
        : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IApplicationBuilder CreateApplicationBuilder()
            => throw new NotSupportedException();
    }

    private sealed class NoopEndpointConventionBuilder : IEndpointConventionBuilder
    {
        public void Add(Action<EndpointBuilder> convention)
        {
        }
    }
}
