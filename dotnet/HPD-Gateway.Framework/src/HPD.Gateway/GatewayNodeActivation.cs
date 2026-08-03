using System.Collections.Immutable;
using System.Text;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Core;
using HPD.Gateway.Effective;
using HPD.Gateway.Yarp;

namespace HPD.Gateway;

public sealed record GatewayNodeActivationRequest(
    CandidateId CandidateId,
    string AuthorityId,
    string AuthorityEpoch,
    ulong AuthorityVersion,
    ImmutableArray<byte> Utf8Configuration);

public enum GatewayNodeActivationState : byte
{
    RejectedBeforeMaterialization = 0,
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
    GatewayEffectiveSnapshot? EffectiveSnapshot,
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
    GatewayNativeMaterializer materializer,
    GatewayYarpPublisher publisher) : IGatewayNodeActivator, IDisposable
{
    private const int MaximumAuthorityIdentityUtf8Bytes = 256;
    private static readonly TimeSpan AcknowledgementTimeout = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _activationLease = new(1, 1);
    private bool _disposed;

    public async ValueTask<GatewayNodeActivationResult> ActivateAsync(
        GatewayNodeActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _activationLease.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var identityErrors = ValidateRequest(request);
            if (!identityErrors.IsEmpty)
                return Rejected(GatewayNodeActivationState.RejectedBeforeMaterialization, identityErrors);

            var candidate = GatewayCandidateReader.Read(request.Utf8Configuration.AsSpan(), capabilities);
            if (!candidate.IsAccepted)
                return Rejected(
                    GatewayNodeActivationState.RejectedBeforeMaterialization,
                    candidate.Errors.Select(static error => new GatewayNodeActivationDiagnostic(
                        $"candidate.{error.Code}", error.Path, error.Message)).ToImmutableArray());

            var identity = new PublicationCandidateIdentity(
                request.CandidateId,
                request.AuthorityId,
                request.AuthorityEpoch,
                request.AuthorityVersion,
                candidate.CanonicalDocument!.ContentHash);
            var materialized = await materializer.MaterializeAsync(
                candidate,
                identity,
                $"hpd-{Guid.NewGuid():N}",
                cancellationToken).ConfigureAwait(false);
            if (!materialized.IsMaterialized)
                return Rejected(
                    GatewayNodeActivationState.RejectedBeforePublish,
                    materialized.Diagnostics.Select(static error => new GatewayNodeActivationDiagnostic(
                        error.Code, error.Path, error.SafeMessage)).ToImmutableArray());

            var publication = await publisher.PublishAsync(
                materialized.Bundle!,
                AcknowledgementTimeout,
                cancellationToken).ConfigureAwait(false);
            return new GatewayNodeActivationResult(
                GatewayNodeActivationState.PublicationCompleted,
                publication,
                materialized.EffectiveSnapshot,
                publication.Diagnostics.Select(static error => new GatewayNodeActivationDiagnostic(
                    error.Code, "$", error.SafeMessage)).ToImmutableArray());
        }
        finally
        {
            _activationLease.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _activationLease.Dispose();
    }

    private static ImmutableArray<GatewayNodeActivationDiagnostic> ValidateRequest(
        GatewayNodeActivationRequest request)
    {
        var errors = ImmutableArray.CreateBuilder<GatewayNodeActivationDiagnostic>();
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
        new(state, null, null, diagnostics);
}
