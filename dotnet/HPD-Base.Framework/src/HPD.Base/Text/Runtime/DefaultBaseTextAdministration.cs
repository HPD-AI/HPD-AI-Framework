namespace HPD.Base;

internal sealed class DefaultBaseTextAdministration(IEnumerable<IBaseTextProvider> providers, BaseTextOperationalState operationalState) : IBaseTextAdministration
{
    public ValueTask<OperationResult<BaseTextIndexStatus[]>> ListAsync(CancellationToken cancellationToken = default) => One((provider, token) => provider.ListAsync(token), static provider => provider.Descriptor.Capability.MaximumInspectionTime, cancellationToken);
    public ValueTask<OperationResult<BaseTextIndexStatus>> GetAsync(string collectionId, string textIndexId, CancellationToken cancellationToken = default) => One((provider, token) => provider.GetAsync(collectionId, textIndexId, token), static provider => provider.Descriptor.Capability.MaximumInspectionTime, cancellationToken);
    public ValueTask<OperationResult<BaseTextRebuildResult>> RebuildAsync(BaseTextRebuildRequest request, CancellationToken cancellationToken = default) => One((provider, token) => provider.RebuildAsync(request, token), static provider => provider.Descriptor.Capability.MaximumRebuildTime, cancellationToken);

    private async ValueTask<OperationResult<T>> One<T>(Func<IBaseTextProvider, CancellationToken, ValueTask<OperationResult<T>>> execute, Func<IBaseTextProvider, TimeSpan> timeout, CancellationToken cancellationToken)
    {
        IBaseTextProvider[] values = providers.ToArray();
        if (values.Length != 1) return Unavailable<T>();
        try { return await operationalState.InvokeAsync(token => execute(values[0], token), timeout(values[0]), cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TimeoutException) { return new() { Status = OperationStatus.StoreError, Error = new BaseError { Code = BaseTextErrorCodes.Timeout, Message = "The text administration operation timed out.", Category = ErrorCategory.Store } }; }
        catch { return Unavailable<T>(); }
    }
    private static OperationResult<T> Unavailable<T>() => new() { Status = OperationStatus.CapabilityUnavailable, Error = new BaseError { Code = BaseTextErrorCodes.CapabilityUnavailable, Message = "The text provider is unavailable.", Category = ErrorCategory.Capability } };
}
