using HPD.Base;
using HPD.Base.InMemory;
using Microsoft.Extensions.Options;

namespace HPD.Base.InMemory;

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
