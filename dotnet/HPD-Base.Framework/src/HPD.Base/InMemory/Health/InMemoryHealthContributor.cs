using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class InMemoryHealthContributor : IBaseHealthContributor
{
    private readonly HPDBaseInMemoryStoreOptions _options;
    private readonly InMemoryRecordStore _store;

    /// <summary>Initializes a new instance.</summary>
    public InMemoryHealthContributor(
        IOptions<HPDBaseInMemoryStoreOptions> options,
        InMemoryRecordStore store)
    {
        _options = options.Value;
        _store = store;
    }

    /// <summary>Gets the ID.</summary>
    public string Id => _options.HealthRefId;

    /// <summary>Executes the get health async operation.</summary>
    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool logicalIndexesQuarantined = _store.LogicalIndexStoreIsQuarantined;
        return ValueTask.FromResult(new[]
        {
            new HealthDescriptor
            {
                Id = _options.HealthRefId,
                Scope = HealthScope.Store,
                TargetRef = _options.StoreId,
                Status = logicalIndexesQuarantined ? HealthStatus.Unhealthy : HealthStatus.Healthy,
                CheckedAt = DateTimeOffset.UtcNow,
                Summary = logicalIndexesQuarantined
                    ? "InMemory logical-index authority is quarantined."
                    : "InMemory store is registered.",
                PublicSafe = false,
                Visibility = VisibilityLevel.Admin,
                Metrics =
                [
                    new HealthMetric
                    {
                        Name = "logicalIndexQuarantined",
                        Kind = HealthMetricValueKind.Boolean,
                        BooleanValue = logicalIndexesQuarantined,
                    },
                    new HealthMetric
                    {
                        Name = "logicalIndexReasonCode",
                        Kind = HealthMetricValueKind.Text,
                        TextValue = logicalIndexesQuarantined
                            ? BaseSchemaErrorCodes.ProviderEvidenceInvalid
                            : null,
                    },
                ],
            }
        });
    }
}
