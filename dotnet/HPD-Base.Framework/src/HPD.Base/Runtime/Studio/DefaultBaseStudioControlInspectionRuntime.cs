namespace HPD.Base;

/// <summary>Reads and validates provider-neutral Studio control facts.</summary>
public interface IBaseStudioControlInspectionRuntime
{
    /// <summary>Executes one bounded read and rejects hostile provider evidence.</summary>
    ValueTask<OperationResult<BaseStudioControlInspectionPage>> ReadAsync(IBaseStudioControlInspectionStore store,
        BaseStudioControlInspectionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Default provider-neutral control inspection Runtime.</summary>
public sealed class DefaultBaseStudioControlInspectionRuntime : IBaseStudioControlInspectionRuntime
{
    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseStudioControlInspectionPage>> ReadAsync(IBaseStudioControlInspectionStore store,
        BaseStudioControlInspectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store); ArgumentNullException.ThrowIfNull(request);
        if (!BaseStudioControlInspectionContract.IsValid(request)) throw new ArgumentException("Studio control inspection request is invalid.", nameof(request));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(request.Limits.Deadline);
        OperationResult<BaseStudioControlInspectionPage> result = await store.ReadStudioControlFactsAsync(request, deadline.Token).ConfigureAwait(false);
        if (!result.IsSuccess() || result.Value is null) return result;
        return BaseStudioControlInspectionContract.IsValidResult(request, result.Value) ? result :
            OperationResults.StoreError<BaseStudioControlInspectionPage>(new BaseError { Code = "base.studio.controlInspection.corrupt",
                Message = "The Studio control inspection evidence could not be validated.", Category = ErrorCategory.Store });
    }
}
