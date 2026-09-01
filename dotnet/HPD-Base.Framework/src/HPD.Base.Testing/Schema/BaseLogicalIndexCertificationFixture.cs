namespace HPD.Base.Testing;

internal enum BaseLogicalIndexGenerationConflictStrategy
{
    OptimisticCapture = 0,
    WriteOwnership = 1,
}

internal sealed record BaseLogicalIndexCertificationFixtureIdentity
{
    internal required string ProviderId { get; init; }
    internal required int ProviderVersion { get; init; }
    internal required string StoreProviderKind { get; init; }
    internal required BaseLogicalIndexGenerationConflictStrategy GenerationConflictStrategy { get; init; }
    internal required System.Collections.Immutable.ImmutableArray<string> NativeDependencyReceipts { get; init; }
}

internal sealed record BaseLogicalIndexCertificationRootRequest
{
    internal BaseLogicalIndexProviderCapability? CertificationCapability { get; init; }
    internal required bool ConstrainPolicyToTenantA { get; init; }
    internal IPolicyEvaluator? PolicyEvaluator { get; init; }
}

internal sealed class BaseLogicalIndexCertificationRoot : IAsyncDisposable
{
    private readonly Func<ValueTask>? _dispose;

    internal BaseLogicalIndexCertificationRoot(
        HPDBaseStoreProvider storeProvider,
        string? schemaStoreId,
        Func<CancellationToken, ValueTask<IAsyncDisposable>>? acquireCompetingWriteOwner = null,
        Func<ValueTask>? dispose = null)
    {
        StoreProvider = storeProvider ?? throw new ArgumentNullException(nameof(storeProvider));
        SchemaStoreId = schemaStoreId;
        AcquireCompetingWriteOwner = acquireCompetingWriteOwner;
        _dispose = dispose;
    }

    internal HPDBaseStoreProvider StoreProvider { get; }
    internal string? SchemaStoreId { get; }
    internal Func<CancellationToken, ValueTask<IAsyncDisposable>>? AcquireCompetingWriteOwner { get; }

    public async ValueTask DisposeAsync()
    {
        if (_dispose is not null)
            await _dispose().ConfigureAwait(false);
    }
}

internal interface IBaseLogicalIndexCertificationFixture
{
    BaseLogicalIndexCertificationFixtureIdentity Identity { get; }

    ValueTask<BaseLogicalIndexCertificationRoot> CreateRootAsync(
        BaseLogicalIndexCertificationRootRequest request,
        CancellationToken cancellationToken = default);
}
