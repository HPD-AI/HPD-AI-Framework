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
    GatewaySharedAdmissionDecision ExpectedDecision);

public sealed record GatewaySharedAdmissionCertificationReport(
    bool Passed,
    int Executed,
    ImmutableArray<string> Diagnostics);

public static class GatewaySharedAdmissionCertification
{
    public const int MaximumVectors = 4_096;

    public static async ValueTask<GatewaySharedAdmissionCertificationReport> VerifyAsync(
        IGatewaySharedAdmissionProvider provider,
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
                !GatewaySharedAdmissionContract.IsValidDecision(vector.ExpectedDecision)))
            return new(false, 0, ["certification-input-invalid"]);

        var diagnostics = ImmutableArray.CreateBuilder<string>();
        var executed = 0;
        foreach (GatewaySharedAdmissionCertificationVector vector in bounded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                GatewaySharedAdmissionDecision actual = await provider.AcquireAsync(vector.Request, cancellationToken).ConfigureAwait(false);
                executed++;
                if (!GatewaySharedAdmissionContract.IsValidDecision(actual) || actual != vector.ExpectedDecision)
                    diagnostics.Add($"vector[{executed - 1}]-mismatch");
            }
            catch
            {
                executed++;
                diagnostics.Add($"vector[{executed - 1}]-provider-failure");
            }
        }
        return new(diagnostics.Count == 0, executed, diagnostics.ToImmutable());
    }
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
}
