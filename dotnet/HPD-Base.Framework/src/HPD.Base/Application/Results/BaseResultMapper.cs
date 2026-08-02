
namespace HPD.Base;

internal static class BaseResultMapper
{
    /// <summary>Executes the map operation.</summary>
    public static BaseResult<BaseUnit> Map(OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status.IsSuccess())
        {
            return new BaseSuccess<BaseUnit>(
                BaseUnit.Value,
                result.Status,
                result.Warnings,
                result.Revision,
                result.Events,
                result.Diagnostics);
        }

        return new BaseFailure<BaseUnit>(
            result.Status,
            result.Error ?? MalformedResultError(),
            result.Warnings,
            result.Diagnostics);
    }

    /// <summary>Executes the map operation.</summary>
    public static BaseResult<TOutput> Map<TInput, TOutput>(
        OperationResult<TInput> result,
        Func<TInput, TOutput> map)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(map);

        if (result.Status.IsSuccess() && result.Value is not null)
        {
            return new BaseSuccess<TOutput>(
                map(result.Value),
                result.Status,
                result.Warnings,
                result.Revision,
                result.Events,
                result.Diagnostics);
        }

        return new BaseFailure<TOutput>(
            result.Status.IsSuccess() ? OperationStatus.StoreError : result.Status,
            result.Error ?? MalformedResultError(),
            result.Warnings,
            result.Diagnostics);
    }

    private static BaseError MalformedResultError() =>
        new()
        {
            Code = "base.application.malformedRuntimeResult",
            Message = "BASE returned an invalid operation result.",
            Category = ErrorCategory.Unexpected,
        };
}

/// <summary>Represents a successful BASE operation with no returned payload.</summary>
public readonly record struct BaseUnit
{
    /// <summary>Gets the value.</summary>
    public static BaseUnit Value => default;
}
