using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

/// <summary>Runs bounded host-owned schema administration commands.</summary>
public sealed class BaseSchemaCommandHost
{
    private readonly IBaseSchemaManager _schemas;

    /// <summary>Initializes a schema command host over the application's configured schema manager.</summary>
    public BaseSchemaCommandHost(IBaseSchemaManager schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        _schemas = schemas;
    }

    /// <summary>Runs a <c>plan</c>, <c>verify</c>, or <c>apply</c> command and returns its stable process exit classification.</summary>
    public async ValueTask<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        if (!TryParse(arguments, out Command command))
        {
            await output.WriteLineAsync("schema command rejected; code=base.schema.command.invalid").ConfigureAwait(false);
            return BaseSchemaCommandExitCodes.InvalidInput;
        }

        try
        {
            return command.Name switch
            {
                "plan" => await WriteAsync(
                    await _schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = command.StoreId! }, cancellationToken).ConfigureAwait(false),
                    HPDBaseJsonSerializerContext.Default.OperationResultBaseSchemaPlan,
                    command.Json, output, cancellationToken).ConfigureAwait(false),
                "verify" => await WriteAsync(
                    await _schemas.VerifyAsync(new BaseSchemaVerifyRequest { StoreId = command.StoreId! }, cancellationToken).ConfigureAwait(false),
                    HPDBaseJsonSerializerContext.Default.OperationResultBaseSchemaObservedState,
                    command.Json, output, cancellationToken).ConfigureAwait(false),
                "apply" => await WriteAsync(
                    await _schemas.ApplyAsync(new BaseSchemaApplyRequest
                    {
                        ProtectedArtifact = command.Artifact!,
                        AllowDestructive = command.AllowDestructive,
                    }, cancellationToken).ConfigureAwait(false),
                    HPDBaseJsonSerializerContext.Default.OperationResultBaseSchemaApplyResult,
                    command.Json, output, cancellationToken).ConfigureAwait(false),
                _ => BaseSchemaCommandExitCodes.InvalidInput,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await output.WriteLineAsync("schema command failed; code=base.schema.command.failed").ConfigureAwait(false);
            return BaseSchemaCommandExitCodes.ConfirmedFailure;
        }
    }

    private static async ValueTask<int> WriteAsync<T>(
        OperationResult<T> result,
        JsonTypeInfo<OperationResult<T>> jsonTypeInfo,
        bool json,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (json)
        {
            OperationResult<T> safe = SafeResult(result);
            string serialized = JsonSerializer.Serialize(safe, jsonTypeInfo);
            await output.WriteLineAsync(serialized.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        else if (result.IsSuccess() && result.Value is not null)
        {
            await output.WriteLineAsync(SafeSuccess(result.Value)).ConfigureAwait(false);
        }
        else
        {
            await output.WriteLineAsync($"schema command failed; code={SafeCode(result.Error?.Code)}").ConfigureAwait(false);
        }

        return ExitCode(result);
    }

    private static string SafeSuccess<T>(T value) => value switch
    {
        BaseSchemaPlan plan => $"schema plan completed; classification={plan.Classification}; operations={plan.Operations.Length}",
        BaseSchemaObservedState observed => $"schema verification completed; compatibility={observed.Compatibility}; generation={observed.Generation}; assets={observed.Assets.Length}",
        BaseSchemaApplyResult applied => $"schema apply completed; outcome={applied.Outcome}; generation={applied.Generation}; state={applied.State}",
        _ => "schema command completed",
    };

    private static int ExitCode<T>(OperationResult<T> result)
    {
        if (result.IsSuccess())
        {
            if (result.Value is BaseSchemaPlan
                {
                    Classification: BaseSchemaPlanClassification.DataMigrationRequired or
                        BaseSchemaPlanClassification.DriftBlocked
                }) return BaseSchemaCommandExitCodes.Rejected;
            if (result.Value is BaseSchemaPlan { Classification: BaseSchemaPlanClassification.Unsupported })
                return BaseSchemaCommandExitCodes.ProviderUnavailable;
            if (result.Value is BaseSchemaObservedState { Compatibility: not BaseSchemaCompatibility.Compatible })
                return BaseSchemaCommandExitCodes.Rejected;
            return BaseSchemaCommandExitCodes.Completed;
        }
        string? code = result.Error?.Code;
        if (code == BaseSchemaErrorCodes.MigrationIndeterminate) return BaseSchemaCommandExitCodes.Indeterminate;
        if (code is BaseSchemaErrorCodes.PlanStale or BaseSchemaErrorCodes.BaselineMismatch or
            BaseSchemaErrorCodes.DriftDetected or BaseSchemaErrorCodes.MigrationRequired)
            return BaseSchemaCommandExitCodes.Rejected;
        if (result.Status is OperationStatus.CapabilityUnavailable or OperationStatus.Unsupported ||
            code is BaseSchemaErrorCodes.MigrationBusy or BaseSchemaErrorCodes.MigrationUnsupported)
            return BaseSchemaCommandExitCodes.ProviderUnavailable;
        if (result.Status == OperationStatus.ValidationFailed ||
            code is BaseSchemaErrorCodes.Invalid or BaseSchemaErrorCodes.PlanInvalid or
            BaseSchemaErrorCodes.PlanExpired or BaseSchemaErrorCodes.PlanLimitExceeded)
            return BaseSchemaCommandExitCodes.InvalidInput;
        return BaseSchemaCommandExitCodes.ConfirmedFailure;
    }

    private static OperationResult<T> SafeResult<T>(OperationResult<T> result) => result.IsSuccess()
        ? result
        : new OperationResult<T>
        {
            Status = result.Status,
            Error = new BaseError
            {
                Code = SafeCode(result.Error?.Code),
                Message = "The schema command failed.",
                Category = result.Error?.Category ?? ErrorCategory.Unexpected,
            },
        };

    private static string SafeCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && code.Length <= 128 &&
        code.All(static character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
            ? code
            : "base.schema.command.failed";

    private static bool TryParse(IReadOnlyList<string> arguments, out Command command)
    {
        command = default;
        if (arguments.Count == 0 || arguments[0] is not ("plan" or "verify" or "apply")) return false;
        string name = arguments[0];
        string? store = null;
        byte[]? artifact = null;
        bool json = false;
        bool destructive = false;
        for (int index = 1; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--json" when !json:
                    json = true;
                    break;
                case "--allow-destructive" when name == "apply" && !destructive:
                    destructive = true;
                    break;
                case "--store" when name is "plan" or "verify" && store is null && ++index < arguments.Count:
                    store = arguments[index];
                    break;
                case "--artifact" when name == "apply" && artifact is null && ++index < arguments.Count:
                    try { artifact = Convert.FromBase64String(arguments[index]); }
                    catch (FormatException) { return false; }
                    break;
                default:
                    return false;
            }
        }
        if (store is { Length: > 256 } || string.IsNullOrWhiteSpace(store) && name is "plan" or "verify" ||
            name == "apply" && (artifact is null || artifact.Length is 0 or > 16 * 1024 * 1024)) return false;
        command = new Command(name, store, artifact, json, destructive);
        return true;
    }

    private readonly record struct Command(string Name, string? StoreId, byte[]? Artifact, bool Json, bool AllowDestructive);
}

/// <summary>Stable process exit classifications for BASE schema administration commands.</summary>
public static class BaseSchemaCommandExitCodes
{
    /// <summary>The command completed successfully.</summary>
    public const int Completed = 0;
    /// <summary>The declaration, configuration, arguments, or plan artifact were invalid.</summary>
    public const int InvalidInput = 2;
    /// <summary>Drift, staleness, a baseline mismatch, or required migration rejected the command.</summary>
    public const int Rejected = 3;
    /// <summary>The provider was unavailable, busy, or lacked a required capability.</summary>
    public const int ProviderUnavailable = 4;
    /// <summary>The command failed with a confirmed outcome or rollback.</summary>
    public const int ConfirmedFailure = 5;
    /// <summary>The schema-apply outcome is indeterminate.</summary>
    public const int Indeterminate = 6;
}
