using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class VolatileHealthContributor : IBaseHealthContributor
{
    private readonly HPDBaseVolatileStoreOptions _options;

    /// <summary>Initializes a new instance.</summary>
    public VolatileHealthContributor(IOptions<HPDBaseVolatileStoreOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Gets the ID.</summary>
    public string Id => _options.HealthRefId;

    /// <summary>Executes the get health async operation.</summary>
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
                Summary = "Volatile store is registered.",
                PublicSafe = false,
                Visibility = VisibilityLevel.Admin
            }
        });
    }
}
