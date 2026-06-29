using HPD.Base.Health;
using HPD.Base.InMemory.Configuration;
using HPD.Base.Runtime.Health;
using Microsoft.Extensions.Options;

namespace HPD.Base.InMemory.Health;

internal sealed class InMemoryHealthContributor : IBaseHealthContributor
{
    private readonly HPDBaseInMemoryOptions _options;

    public InMemoryHealthContributor(IOptions<HPDBaseInMemoryOptions> options)
    {
        _options = options.Value;
    }

    public string Id => _options.HealthRefId;

    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new[]
        {
            new HealthDescriptor
            {
                Id = _options.HealthRefId,
                Scope = HealthScope.Store,
                TargetRef = _options.StoreId,
                Status = HealthStatus.Healthy,
                CheckedAt = DateTimeOffset.UtcNow,
                Summary = "InMemory store is registered.",
                PublicSafe = false,
                Visibility = VisibilityLevel.Admin
            }
        });
    }
}
