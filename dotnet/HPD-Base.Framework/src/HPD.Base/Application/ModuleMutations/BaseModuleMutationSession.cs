namespace HPD.Base;

/// <summary>Resolves graph-installed registered module mutations for one principal-bound session.</summary>
public sealed class BaseModuleMutationSession
{
    private readonly BaseSession _session;

    internal BaseModuleMutationSession(BaseSession session) => _session = session;

    /// <summary>Resolves one inert generated identity to an executable session-bound handle.</summary>
    public BaseInstalledModuleMutationHandle<TRequest, TResult> Get<TRequest, TResult>(
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        BaseModuleMutationRegistry registry = _session.Services.GetService(typeof(BaseModuleMutationRegistry)) as BaseModuleMutationRegistry
            ?? throw new InvalidOperationException(BaseModuleMutationErrorCodes.NotInstalled);
        BaseRegisteredModuleMutationDefinition definition = registry.Find(identity.Id, identity.Version)
            ?? throw new InvalidOperationException(BaseModuleMutationErrorCodes.NotInstalled);
        if (!identity.Checksum.AsSpan().SequenceEqual(definition.Checksum.ToArray()))
            throw new InvalidOperationException(BaseModuleMutationErrorCodes.SchemaChanged);
        IBaseModuleMutationRuntime runtime = _session.Services.GetService(typeof(IBaseModuleMutationRuntime)) as IBaseModuleMutationRuntime
            ?? throw new InvalidOperationException(BaseModuleMutationErrorCodes.NotInstalled);
        return new BaseInstalledModuleMutationHandle<TRequest, TResult>(
            runtime,
            _session,
            definition,
            identity);
    }
}

/// <summary>Executes one exact graph-installed module mutation through its owning session.</summary>
public sealed class BaseInstalledModuleMutationHandle<TRequest, TResult>
{
    private readonly IBaseModuleMutationRuntime _runtime;
    private readonly BaseSession _session;
    private readonly BaseRegisteredModuleMutationDefinition _definition;
    private readonly BaseGeneratedModuleMutationIdentity<TRequest, TResult> _identity;

    internal BaseInstalledModuleMutationHandle(
        IBaseModuleMutationRuntime runtime,
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> identity)
    {
        _runtime = runtime;
        _session = session;
        _definition = definition;
        _identity = identity;
    }

    /// <summary>Creates one request identity from the complete installed source-generated request.</summary>
    /// <param name="request">The complete typed request.</param>
    /// <param name="idempotencyKey">The caller-owned bounded logical-attempt key.</param>
    /// <returns>An identity bound to the operation, canonical request, principal, and tenant.</returns>
    public BaseMutationRequestIdentity CreateRequestIdentity(
        TRequest request,
        string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        return BaseModuleMutationRequestIdentityContract.Create(
            _definition, _identity, request, idempotencyKey, _session.Principal);
    }

    /// <summary>Executes one identified registered module mutation.</summary>
    public ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ExecuteAsync(
        TRequest request,
        BaseMutationRequestIdentity identity,
        BaseModuleMutationExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(identity);
        return _runtime.ExecuteAsync<TRequest, TResult>(
            _session,
            _definition,
            _identity,
            request,
            identity,
            options,
            cancellationToken);
    }

    /// <summary>Resolves one historical identified result without re-executing the operation.</summary>
    public ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ResolveAsync(
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return _runtime.ResolveAsync<TRequest, TResult>(
            _session,
            _definition,
            _identity,
            identity,
            cancellationToken);
    }
}

internal interface IBaseModuleMutationRuntime
{
    ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ExecuteAsync<TRequest, TResult>(
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> generatedIdentity,
        TRequest request,
        BaseMutationRequestIdentity identity,
        BaseModuleMutationExecutionOptions? options,
        CancellationToken cancellationToken);

    ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ResolveAsync<TRequest, TResult>(
        BaseSession session,
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> generatedIdentity,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken);
}

internal static class BaseModuleMutationErrorCodes
{
    internal const string NotInstalled = "base.moduleMutation.notInstalled";
    internal const string SchemaChanged = "base.moduleMutation.schemaChanged";
    internal const string Invalid = "base.moduleMutation.invalid";
    internal const string Unauthorized = "base.moduleMutation.unauthorized";
    internal const string LimitExceeded = "base.moduleMutation.limitExceeded";
    internal const string CapabilityMissing = "base.moduleMutation.capabilityMissing";
    internal const string AuthorityChanged = "base.moduleMutation.authorityChanged";
    internal const string GenerationConflict = "base.moduleMutation.generationConflict";
    internal const string CommitIndeterminate = "base.moduleMutation.commitIndeterminate";
    internal const string ReceiptUnavailable = "base.moduleMutation.receiptUnavailable";
    internal const string Cancelled = "base.moduleMutation.cancelled";
    internal const string StoreError = "base.moduleMutation.storeError";
    internal const string ProviderContractInvalid = "base.moduleMutation.providerContractInvalid";
}
