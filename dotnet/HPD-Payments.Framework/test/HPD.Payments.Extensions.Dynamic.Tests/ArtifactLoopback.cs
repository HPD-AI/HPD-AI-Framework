using HPD.Payments.Extensions.Dynamic;

namespace HPD.Payments.Extensions.Dynamic.Tests.Artifacts;

/// <summary>Named file-loaded fixture used to prove the JIT artifact boundary.</summary>
public sealed class ArtifactLoopback(DynamicExtensionManifest manifest) : IDynamicPaymentExtension
{
    /// <inheritdoc />
    public DynamicExtensionManifest Manifest { get; } = manifest;

    /// <inheritdoc />
    public ValueTask<DynamicExtensionResult> InvokeAsync(DynamicExtensionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new DynamicExtensionResult(true, "completed", DynamicResourceClaim.SoftObserved,
            request.CopyPayload()));
    }
}
