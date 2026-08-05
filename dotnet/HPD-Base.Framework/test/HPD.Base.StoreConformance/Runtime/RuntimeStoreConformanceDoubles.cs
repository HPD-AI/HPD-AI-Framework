using HPD.Base;

namespace HPD.Base.StoreConformance.Runtime;

public sealed class ConformanceAllowPolicyEvaluator : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = request;
        return ValueTask.FromResult(new PolicyDecision
        {
            Effect = PolicyEffect.Allow,
            Outcome = PolicyOutcome.Allowed
        });
    }
}

public sealed class ConformanceDenyPolicyEvaluator : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = request;
        return ValueTask.FromResult(new PolicyDecision
        {
            Effect = PolicyEffect.Deny,
            Outcome = PolicyOutcome.Denied,
            ReasonCode = "conformance.denied",
            SafeMessage = "Denied by conformance policy."
        });
    }
}

public sealed class ConformanceDenyExistingRecordPolicyEvaluator : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(request.Resource.ExistingRecord is null
            ? new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.Allowed
            }
            : new PolicyDecision
            {
                Effect = PolicyEffect.Deny,
                Outcome = PolicyOutcome.FilteredOut,
                ReasonCode = "conformance.filtered",
                SafeMessage = "Filtered by conformance policy."
            });
    }
}

public sealed class ConformanceCapturingEventPublisher : IBaseEventPublisher
{
    public BaseRecordMutationEvent? LastEvent { get; private set; }

    public ValueTask<OperationResult<EventPublishResult>> PublishAsync(
        BaseEvent @event,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (@event is not BaseRecordMutationEvent mutation)
        {
            throw new InvalidOperationException("Expected a BASE record mutation event.");
        }

        LastEvent = mutation;
        return ValueTask.FromResult(OperationResults.Ok(new EventPublishResult
        {
            EventId = mutation.EventId,
            Stream = mutation.Resource.CollectionId,
            PublishedAt = mutation.Timestamp,
            Guarantee = EventDeliveryGuarantee.BestEffort
        }));
    }
}

public sealed class ConformanceThrowingRecordStore : IRecordStore
{
    private readonly Exception _exception;

    public ConformanceThrowingRecordStore(StoreCapabilityDescriptor capabilities, Exception exception)
    {
        Capabilities = capabilities;
        _exception = exception;
    }

    public StoreCapabilityDescriptor Capabilities { get; }

    public ValueTask<OperationResult<RecordPage>> ListAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        throw _exception;

    public ValueTask<OperationResult<RecordEnvelope>> GetAsync(
        CollectionDefinition collection,
        RecordId id,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        throw _exception;

    public ValueTask<OperationResult<RecordEnvelope>> CreateAsync(
        CollectionDefinition collection,
        RecordCreateRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        throw _exception;

    public ValueTask<OperationResult<RecordEnvelope>> PatchAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordPatchRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        throw _exception;

    public ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordReplaceRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        throw _exception;

    public ValueTask<OperationResult<DeleteResult>> DeleteAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordDeleteRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        throw _exception;
}
