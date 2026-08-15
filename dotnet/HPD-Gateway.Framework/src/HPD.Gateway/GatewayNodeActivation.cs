using System.Collections.Immutable;
using System.Text;

namespace HPD.Gateway;

public sealed record GatewayNodeActivationRequest(
    string NamespaceId,
    string TargetNodeId,
    CandidateId CandidateId,
    string AuthorityId,
    string AuthorityEpoch,
    ulong AuthorityVersion,
    ImmutableArray<byte> Utf8Configuration);

public enum GatewayNodeActivationState : byte
{
    RejectedBeforePlanning = 0,
    RejectedBeforePublish = 1,
    PublicationCompleted = 2
}

public sealed record GatewayNodeActivationDiagnostic(
    string Code,
    string Path,
    string SafeMessage);

public sealed record GatewayNodeActivationResult(
    GatewayNodeActivationState State,
    GatewayPublicationOutcome? Publication,
    string? ApplicationId,
    ContentHash? SymbolicPlanIdentity,
    ImmutableArray<GatewayNodeActivationDiagnostic> Diagnostics)
{
    public bool IsActiveAcknowledged =>
        Publication?.State == GatewayPublicationState.ActiveAcknowledged;
}

public interface IGatewayNodeActivator
{
    ValueTask<GatewayNodeActivationResult> ActivateAsync(
        GatewayNodeActivationRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class GatewayNodeActivator(
    HostCapabilitySnapshot capabilities,
    GatewayRuntimePlanner planner,
    GatewayRuntimePublisher publisher) : IGatewayNodeActivator, IDisposable
{
    private const int MaximumAuthorityIdentityUtf8Bytes = 256;
    private static readonly TimeSpan AcknowledgementTimeout = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _activationLease = new(1, 1);
    private volatile bool _disposed;
    public async ValueTask<GatewayNodeActivationResult> ActivateAsync(
        GatewayNodeActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            await _activationLease.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Canceled("activation.canceled-before-admission");
        }
        try
        {
            if (_disposed)
                return Canceled("activation.stopping");
            var identityErrors = ValidateRequest(request);
            if (!identityErrors.IsEmpty)
                return Rejected(GatewayNodeActivationState.RejectedBeforePlanning, identityErrors);

            var candidate = GatewayCandidateReader.Read(request.Utf8Configuration.AsSpan(), capabilities);
            if (!candidate.IsAccepted)
                return Rejected(
                    GatewayNodeActivationState.RejectedBeforePlanning,
                    candidate.Errors.Select(static error => new GatewayNodeActivationDiagnostic(
                        $"candidate.{error.Code}", error.Path, error.Message)).ToImmutableArray());

            var identity = new PublicationCandidateIdentity(
                request.CandidateId,
                request.AuthorityId,
                request.AuthorityEpoch,
                request.AuthorityVersion,
                candidate.CanonicalDocument!.ContentHash);
            var planned = await planner.PlanAsync(
                candidate,
                identity,
                $"hpd-{Guid.NewGuid():N}",
                cancellationToken).ConfigureAwait(false);
            if (!planned.IsPlanned)
                return Rejected(
                    GatewayNodeActivationState.RejectedBeforePublish,
                    planned.Diagnostics.Select(static error => new GatewayNodeActivationDiagnostic(
                        error.Code, error.Path, error.SafeMessage)).ToImmutableArray());
            if (planned.PreparedApplication is null)
                return Rejected(
                    GatewayNodeActivationState.RejectedBeforePublish,
                    [new GatewayNodeActivationDiagnostic(
                        "planning.dependencies-unresolved", "$", "The symbolic runtime plan requires the governed pre-exchange resolver.")]);

            var publication = await publisher.PublishAsync(
                planned.PreparedApplication,
                request.NamespaceId,
                request.TargetNodeId,
                AcknowledgementTimeout,
                cancellationToken).ConfigureAwait(false);
            return new GatewayNodeActivationResult(
                GatewayNodeActivationState.PublicationCompleted,
                publication,
                publication.Active?.ApplicationId ?? planned.PreparedApplication.ApplicationId,
                publication.Active?.SymbolicPlanIdentity ?? planned.PreparedApplication.SymbolicPlanIdentity,
                publication.Diagnostics.Select(static error => new GatewayNodeActivationDiagnostic(
                    error.Code, "$", error.SafeMessage)).ToImmutableArray());
        }
        catch (OperationCanceledException)
        {
            return Canceled("activation.canceled-before-publication");
        }
        finally
        {
            _activationLease.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private static ImmutableArray<GatewayNodeActivationDiagnostic> ValidateRequest(
        GatewayNodeActivationRequest request)
    {
        var errors = ImmutableArray.CreateBuilder<GatewayNodeActivationDiagnostic>();
        ValidateAuthority(request.NamespaceId, "namespaceId", errors);
        ValidateAuthority(request.TargetNodeId, "targetNodeId", errors);
        if (!GatewayIdentifier.IsCanonical(request.CandidateId.Value))
            errors.Add(new("activation.candidate-id-invalid", "candidateId", "Candidate identity is not canonical."));
        ValidateAuthority(request.AuthorityId, "authorityId", errors);
        ValidateAuthority(request.AuthorityEpoch, "authorityEpoch", errors);
        if (request.AuthorityVersion == 0)
            errors.Add(new("activation.authority-version-invalid", "authorityVersion", "Authority version must be positive."));
        if (request.Utf8Configuration.IsDefault)
            errors.Add(new("activation.configuration-missing", "configuration", "Gateway configuration bytes are required."));
        return errors.ToImmutable();
    }

    private static void ValidateAuthority(
        string? value,
        string path,
        ImmutableArray<GatewayNodeActivationDiagnostic>.Builder errors)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Any(char.IsControl) ||
            Encoding.UTF8.GetByteCount(value) > MaximumAuthorityIdentityUtf8Bytes)
            errors.Add(new($"activation.{path}-invalid", path, "Authority identity is invalid or exceeds its bound."));
    }

    private static GatewayNodeActivationResult Rejected(
        GatewayNodeActivationState state,
        ImmutableArray<GatewayNodeActivationDiagnostic> diagnostics) =>
        new(state, null, null, null, diagnostics);

    private static GatewayNodeActivationResult Canceled(string code) =>
        Rejected(
            GatewayNodeActivationState.RejectedBeforePlanning,
            [new(code, "$", "Node activation was canceled before native publication.")]);
}
