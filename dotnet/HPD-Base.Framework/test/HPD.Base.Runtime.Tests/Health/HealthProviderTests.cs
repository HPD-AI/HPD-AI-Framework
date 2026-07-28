using HPD.Base.Health;
using HPD.Base.Runtime.DependencyInjection;
using HPD.Base.Runtime.Health;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Tests.Health;

public sealed class HealthProviderTests
{
    [Fact]
    public async Task AggregatesExplicitHealthContributorsAndFiltersPublicDependencies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBaseHealthContributor, TestHealthContributor>();
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IBaseHealthProvider>().GetHealthAsync(
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.SchemaRead),
            VisibilityLevel.Public);

        var health = Assert.Single(result.Value!);
        Assert.Null(health.Dependencies);
    }

    private sealed class TestHealthContributor : IBaseHealthContributor
    {
        public string Id => "test";

        public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new[]
            {
                new HealthDescriptor
                {
                    Id = "runtime",
                    Scope = HealthScope.Runtime,
                    Status = HealthStatus.Healthy,
                    CheckedAt = DateTimeOffset.UnixEpoch,
                    Visibility = VisibilityLevel.Public,
                    PublicSafe = true,
                    Dependencies =
                    [
                        new HealthDependency { Id = "db", Kind = "database", Status = HealthStatus.Healthy }
                    ]
                }
            });
    }
}
