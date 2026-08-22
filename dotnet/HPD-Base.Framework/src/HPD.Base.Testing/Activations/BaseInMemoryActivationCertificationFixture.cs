namespace HPD.Base.Testing;

/// <summary>Supplies a fresh InMemory provider to the host-owned activation certification matrix.</summary>
public sealed class BaseInMemoryActivationCertificationFixture : IBaseActivationCertificationFixture
{
    private readonly InMemoryRecordStore _store = new();

    /// <inheritdoc />
    public BaseActivationProviderDescriptor Descriptor => _store.Descriptor;

    /// <inheritdoc />
    public IBaseActivationProvider Provider => _store;

    /// <inheritdoc />
    public ValueTask PrepareAsync(
        BaseActivationCertificationCaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
