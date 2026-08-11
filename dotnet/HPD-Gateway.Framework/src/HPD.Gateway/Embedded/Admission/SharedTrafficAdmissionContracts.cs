using System.Text.Json.Serialization;
using System.Text;
using System.Collections.Immutable;

namespace HPD.Gateway;

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewaySharedAdmissionDecisionKind>))]
public enum GatewaySharedAdmissionDecisionKind : byte
{
    Acquired = 0,
    Rejected = 1,
    ConfigurationConflict = 2,
    UnavailableBeforePossibleCommit = 3,
    IndeterminateAfterPossibleCommit = 4,
    CanceledBeforeDispatch = 5,
}

public sealed record GatewaySharedAdmissionRequest(
    ushort ContractVersion,
    string ProviderId,
    string AuthorityId,
    string Profile,
    ContentHash BehaviorIdentity,
    string PartitionKey,
    TrafficAdmissionRateAlgorithm Algorithm,
    long PermitLimit,
    long TokensPerPeriod,
    long WindowMilliseconds,
    int SegmentsPerWindow,
    int PermitCount,
    string AttemptId);

public sealed record GatewaySharedAdmissionDecision(
    GatewaySharedAdmissionDecisionKind Kind,
    long? Remaining,
    long? RetryAfterMilliseconds,
    long? ResetAfterMilliseconds,
    string? ObservationId,
    string? DiagnosticCode);

public interface IGatewaySharedAdmissionProvider
{
    ValueTask<GatewaySharedAdmissionDecision> AcquireAsync(
        GatewaySharedAdmissionRequest request,
        CancellationToken cancellationToken);
}

public sealed record GatewaySharedAdmissionSegmentState(long Epoch, long Count);

public sealed record GatewaySharedAdmissionRetainedState(
    ushort ContractVersion,
    TrafficAdmissionRateAlgorithm Algorithm,
    long LastObservedMilliseconds,
    long? WindowStartMilliseconds,
    long? Used,
    long? Tokens,
    long? LastRefillMilliseconds,
    long? Remainder,
    ImmutableArray<GatewaySharedAdmissionSegmentState> Segments,
    long ExpiryAtMilliseconds);

public interface IGatewaySharedAdmissionCertificationAuthority : IGatewaySharedAdmissionProvider
{
    ValueTask<GatewaySharedAdmissionRetainedState> ObserveStateAsync(
        GatewaySharedAdmissionRequest request,
        CancellationToken cancellationToken);
}

public sealed class GatewaySharedAdmissionProviderOptions
{
    public required string AuthorityId { get; set; }
    public required ContentHash BehaviorIdentity { get; set; }
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
    public int MaximumConcurrentInvocations { get; set; } = 1_024;
}

public sealed class GatewaySharedAdmissionProfileOptions : GatewayLocalAdmissionOptions
{
    public TrafficAdmissionFailureDisposition FailureDisposition { get; set; } = TrafficAdmissionFailureDisposition.Reject;
    public string? LocalFallbackProfile { get; set; }
}

public sealed record GatewaySharedAdmissionProviderStatistics(
    int ActiveInvocations,
    int MaximumConcurrentInvocations,
    long SaturatedBeforeDispatch,
    long TimedOutDetached,
    long LateCompletions,
    bool IsDisposed);

public sealed record GatewaySharedAdmissionCertificationVector(
    GatewaySharedAdmissionRequest Request,
    GatewaySharedAdmissionDecision ExpectedDecision,
    GatewaySharedAdmissionRetainedState ExpectedState);

public sealed record GatewaySharedAdmissionCertificationReport(
    bool Passed,
    int Executed,
    ImmutableArray<string> Diagnostics);

public static class GatewaySharedAdmissionCertification
{
    public const int MaximumVectors = 4_096;

    public static async ValueTask<GatewaySharedAdmissionCertificationReport> VerifyAsync(
        IGatewaySharedAdmissionCertificationAuthority provider,
        IEnumerable<GatewaySharedAdmissionCertificationVector> vectors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(vectors);
        var bounded = new List<GatewaySharedAdmissionCertificationVector>(MaximumVectors + 1);
        using IEnumerator<GatewaySharedAdmissionCertificationVector> enumerator = vectors.GetEnumerator();
        while (bounded.Count <= MaximumVectors && enumerator.MoveNext()) bounded.Add(enumerator.Current);
        if (bounded.Count > MaximumVectors || bounded.Any(static vector => vector is null ||
                !GatewaySharedAdmissionContract.IsValidRequest(vector.Request) ||
                !GatewaySharedAdmissionContract.IsValidDecision(vector.Request, vector.ExpectedDecision) ||
                !GatewaySharedAdmissionContract.IsValidState(vector.Request, vector.ExpectedState)))
            return new(false, 0, ["certification-input-invalid"]);

        var diagnostics = ImmutableArray.CreateBuilder<string>();
        var executed = 0;
        foreach (GatewaySharedAdmissionCertificationVector vector in bounded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                GatewaySharedAdmissionDecision actual = await provider.AcquireAsync(vector.Request, cancellationToken).ConfigureAwait(false);
                GatewaySharedAdmissionRetainedState state = await provider.ObserveStateAsync(vector.Request, cancellationToken).ConfigureAwait(false);
                executed++;
                if (!GatewaySharedAdmissionContract.IsValidDecision(vector.Request, actual) || actual != vector.ExpectedDecision ||
                    !GatewaySharedAdmissionContract.IsValidState(vector.Request, state) || !SameState(state, vector.ExpectedState))
                    diagnostics.Add($"vector[{executed - 1}]-mismatch");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                executed++;
                diagnostics.Add($"vector[{executed - 1}]-provider-failure");
            }
        }
        return new(diagnostics.Count == 0, executed, diagnostics.ToImmutable());
    }

    private static bool SameState(GatewaySharedAdmissionRetainedState left, GatewaySharedAdmissionRetainedState right) =>
        left with { Segments = [] } == right with { Segments = [] } && left.Segments.SequenceEqual(right.Segments);
}

public static class GatewaySharedAdmissionContract
{
    public const ushort Version = 1;
    public const long MaximumExactInteger = 9_007_199_254_740_991;

    public static bool IsValidDecision(GatewaySharedAdmissionDecision? decision)
    {
        if (decision is null || !Enum.IsDefined(decision.Kind) ||
            !Bounded(decision.ObservationId, 256) || !Bounded(decision.DiagnosticCode, 128))
            return false;
        return decision.Kind switch
        {
            GatewaySharedAdmissionDecisionKind.Acquired =>
                decision.Remaining is >= 0 and <= MaximumExactInteger &&
                decision.RetryAfterMilliseconds is null && decision.ResetAfterMilliseconds is > 0 and <= MaximumExactInteger,
            GatewaySharedAdmissionDecisionKind.Rejected =>
                decision.Remaining is >= 0 and <= MaximumExactInteger &&
                decision.RetryAfterMilliseconds is > 0 and <= MaximumExactInteger &&
                decision.ResetAfterMilliseconds >= decision.RetryAfterMilliseconds &&
                decision.ResetAfterMilliseconds <= MaximumExactInteger,
            _ => decision.Remaining is null && decision.RetryAfterMilliseconds is null && decision.ResetAfterMilliseconds is null,
        };
    }

    public static bool IsValidDecision(GatewaySharedAdmissionRequest request, GatewaySharedAdmissionDecision? decision)
    {
        if (!IsValidRequest(request) || !IsValidDecision(decision)) return false;
        if (decision!.Kind is not (GatewaySharedAdmissionDecisionKind.Acquired or GatewaySharedAdmissionDecisionKind.Rejected)) return true;
        if (decision.Remaining > request.PermitLimit ||
            decision.Kind == GatewaySharedAdmissionDecisionKind.Acquired && decision.Remaining > request.PermitLimit - request.PermitCount ||
            decision.Kind == GatewaySharedAdmissionDecisionKind.Rejected && decision.Remaining >= request.PermitCount)
            return false;
        var maximumReset = request.Algorithm == TrafficAdmissionRateAlgorithm.TokenBucket
            ? CeilingDivide(checked((UInt128)(ulong)request.PermitLimit * (ulong)request.WindowMilliseconds), (ulong)request.TokensPerPeriod)
            : (UInt128)(ulong)request.WindowMilliseconds;
        return maximumReset <= MaximumExactInteger && decision.ResetAfterMilliseconds <= (long)maximumReset &&
            (decision.RetryAfterMilliseconds is null || decision.RetryAfterMilliseconds <= decision.ResetAfterMilliseconds) &&
            (request.Algorithm != TrafficAdmissionRateAlgorithm.FixedWindow ||
             decision.Kind != GatewaySharedAdmissionDecisionKind.Rejected || decision.RetryAfterMilliseconds == decision.ResetAfterMilliseconds);
    }

    public static bool IsValidState(GatewaySharedAdmissionRequest request, GatewaySharedAdmissionRetainedState? state)
    {
        if (!IsValidRequest(request) || state is null || state.ContractVersion != Version || state.Algorithm != request.Algorithm ||
            state.LastObservedMilliseconds < 0 || state.ExpiryAtMilliseconds <= state.LastObservedMilliseconds || state.Segments.IsDefault ||
            state.Segments.Length > 64 || state.Segments.Any(static value => value is null || value.Epoch < 0 || value.Count < 0) ||
            state.Segments.Select(static value => value.Epoch).Distinct().Count() != state.Segments.Length ||
            !state.Segments.Select(static value => value.Epoch).SequenceEqual(state.Segments.Select(static value => value.Epoch).Order()))
            return false;
        return request.Algorithm switch
        {
            TrafficAdmissionRateAlgorithm.FixedWindow => state.WindowStartMilliseconds is >= 0 && state.Used is >= 0 && state.Used <= request.PermitLimit &&
                state.Tokens is null && state.LastRefillMilliseconds is null && state.Remainder is null && state.Segments.IsEmpty,
            TrafficAdmissionRateAlgorithm.SlidingWindow => state.WindowStartMilliseconds is null && state.Used is null && state.Tokens is null &&
                state.LastRefillMilliseconds is null && state.Remainder is null && state.Segments.Length <= request.SegmentsPerWindow &&
                state.Segments.Sum(static value => value.Count) <= request.PermitLimit,
            TrafficAdmissionRateAlgorithm.TokenBucket => state.WindowStartMilliseconds is null && state.Used is null &&
                state.Tokens is >= 0 && state.Tokens <= request.PermitLimit && state.LastRefillMilliseconds is >= 0 &&
                state.Remainder is >= 0 && state.Remainder < request.WindowMilliseconds && state.Segments.IsEmpty,
            _ => false,
        };
    }

    public static bool IsValidRequest(GatewaySharedAdmissionRequest? request, bool requireUnitPermit = false)
    {
        if (request is null || request.ContractVersion != Version ||
            !GatewayIdentifier.IsCanonical(request.ProviderId) || string.IsNullOrWhiteSpace(request.AuthorityId) ||
            request.AuthorityId.Length > 256 || request.AuthorityId.Any(char.IsControl) ||
            !GatewayIdentifier.IsCanonical(request.Profile) || request.BehaviorIdentity.Algorithm != "sha-256" ||
            request.BehaviorIdentity.Value is not { Length: 64 } hash ||
            !hash.All(static value => value is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            string.IsNullOrEmpty(request.PartitionKey) || Encoding.UTF8.GetByteCount(request.PartitionKey) > 256 ||
            !request.PartitionKey.IsNormalized(NormalizationForm.FormC) || !Enum.IsDefined(request.Algorithm) ||
            request.PermitLimit is < 1 or > 100_000_000 || request.PermitCount < 1 ||
            request.PermitCount > request.PermitLimit || requireUnitPermit && request.PermitCount != 1 ||
            request.WindowMilliseconds is < 100 or > 86_400_000 ||
            request.AttemptId is not { Length: 32 } attempt ||
            !attempt.All(static value => value is >= '0' and <= '9' or >= 'a' and <= 'f'))
            return false;
        return request.Algorithm switch
        {
            TrafficAdmissionRateAlgorithm.FixedWindow => request.WindowMilliseconds >= 1_000 &&
                request.TokensPerPeriod == 0 && request.SegmentsPerWindow == 0,
            TrafficAdmissionRateAlgorithm.SlidingWindow => request.WindowMilliseconds >= 1_000 &&
                request.TokensPerPeriod == 0 && request.SegmentsPerWindow is >= 2 and <= 64 &&
                request.WindowMilliseconds % request.SegmentsPerWindow == 0,
            TrafficAdmissionRateAlgorithm.TokenBucket => request.TokensPerPeriod is >= 1 and <= 100_000_000 &&
                request.SegmentsPerWindow == 0,
            _ => false,
        };
    }

    private static bool Bounded(string? value, int maximum) => value is null ||
        value.Length <= maximum && !value.Any(char.IsControl);

    private static UInt128 CeilingDivide(UInt128 value, ulong divisor) => (value + divisor - 1) / divisor;
}
