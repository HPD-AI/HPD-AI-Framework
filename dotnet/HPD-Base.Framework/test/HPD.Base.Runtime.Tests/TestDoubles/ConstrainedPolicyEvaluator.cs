using HPD.Base.Policy;
using HPD.Base.Query;

namespace HPD.Base.Runtime.Tests;

internal sealed class ConstrainedPolicyEvaluator : IPolicyEvaluator
{
    private readonly FilterExpression? _recordFilter;
    private readonly FieldMask? _readMask;
    private readonly FieldMask? _writeMask;
    private readonly FilterExpression? _writeCheck;

    public ConstrainedPolicyEvaluator(
        FilterExpression? recordFilter = null,
        FieldMask? readMask = null,
        FieldMask? writeMask = null,
        FilterExpression? writeCheck = null)
    {
        _recordFilter = recordFilter;
        _readMask = readMask;
        _writeMask = writeMask;
        _writeCheck = writeCheck;
    }

    public ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = request;
        var hasConstraints = _recordFilter is not null
            || _readMask is not null
            || _writeMask is not null
            || _writeCheck is not null;
        return ValueTask.FromResult(new PolicyDecision
        {
            Effect = PolicyEffect.Allow,
            Outcome = hasConstraints ? PolicyOutcome.AllowedWithConstraints : PolicyOutcome.Allowed,
            Constraints = !hasConstraints
                ? null
                : new PolicyConstraints
                {
                    RecordFilter = _recordFilter,
                    ReadMask = _readMask,
                    WriteMask = _writeMask,
                    WriteCheck = _writeCheck
                }
        });
    }
}
