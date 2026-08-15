using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Extensions.Dynamic;

/// <summary>Names the only resource claim available to an in-process dynamic extension.</summary>
public enum DynamicResourceClaim
{
    /// <summary>No resource observation is available.</summary>
    Unavailable = 0,
    /// <summary>Cooperative elapsed-time and allocation observations were collected without hard containment.</summary>
    SoftObserved,
}

/// <summary>Declares one explicitly constructed dynamic extension artifact and its compatibility pins.</summary>
public sealed record DynamicExtensionManifest
{
    /// <summary>Gets the stable extension identity.</summary>
    public SemanticId ExtensionId { get; }
    /// <summary>Gets the exact contract version.</summary>
    public ContractVersion ContractVersion { get; }
    /// <summary>Gets the code revision.</summary>
    public Revision CodeRevision { get; }
    /// <summary>Gets the configuration revision.</summary>
    public Revision ConfigurationRevision { get; }
    /// <summary>Gets the digest of the authenticated artifact manifest.</summary>
    public CanonicalDigest ArtifactDigest { get; }
    /// <summary>Gets whether signature verification succeeded before activation.</summary>
    public bool SignatureVerified { get; }

    /// <summary>Creates a closed dynamic extension manifest.</summary>
    public DynamicExtensionManifest(SemanticId extensionId, ContractVersion contractVersion, Revision codeRevision,
        Revision configurationRevision, CanonicalDigest artifactDigest, bool signatureVerified)
    {
        ArgumentNullException.ThrowIfNull(artifactDigest);
        if (!extensionId.IsValid || !contractVersion.IsValid || !codeRevision.IsValid || !configurationRevision.IsValid)
            throw new ArgumentException("Dynamic extension manifest requires identity, contract, code, config, and digest.");
        ExtensionId = extensionId; ContractVersion = contractVersion; CodeRevision = codeRevision;
        ConfigurationRevision = configurationRevision; ArtifactDigest = artifactDigest; SignatureVerified = signatureVerified;
    }
}

/// <summary>Owns a bounded dynamic invocation request.</summary>
public sealed class DynamicExtensionRequest
{
    /// <summary>Maximum admitted payload bytes.</summary>
    public const int MaximumPayloadBytes = 1_048_576;
    private readonly byte[] _payload;

    /// <summary>Gets the invocation identity.</summary>
    public SemanticId InvocationId { get; }
    /// <summary>Gets the targeted extension identity.</summary>
    public SemanticId ExtensionId { get; }
    /// <summary>Gets the exact contract version.</summary>
    public ContractVersion ContractVersion { get; }
    /// <summary>Gets the configuration revision expected by the caller.</summary>
    public Revision ConfigurationRevision { get; }
    /// <summary>Gets the copied payload length.</summary>
    public int PayloadLength => _payload.Length;

    /// <summary>Copies a bounded request payload.</summary>
    public DynamicExtensionRequest(SemanticId invocationId, SemanticId extensionId, ContractVersion contractVersion,
        Revision configurationRevision, ReadOnlySpan<byte> payload)
    {
        if (!invocationId.IsValid || !extensionId.IsValid || invocationId.Scope != extensionId.Scope ||
            !contractVersion.IsValid || !configurationRevision.IsValid || payload.Length > MaximumPayloadBytes)
            throw new ArgumentException("Dynamic request requires same-scope identities, versions, and bounded payload.");
        InvocationId = invocationId; ExtensionId = extensionId; ContractVersion = contractVersion;
        ConfigurationRevision = configurationRevision; _payload = payload.ToArray();
    }

    /// <summary>Returns a new payload copy.</summary>
    public byte[] CopyPayload() => _payload.ToArray();
}

/// <summary>Owns one bounded dynamic invocation result.</summary>
public sealed class DynamicExtensionResult
{
    private readonly byte[] _payload;

    /// <summary>Gets whether extension execution completed.</summary>
    public bool Completed { get; }
    /// <summary>Gets a bounded result code.</summary>
    public string Code { get; }
    /// <summary>Gets the honest in-process resource claim.</summary>
    public DynamicResourceClaim ResourceClaim { get; }
    /// <summary>Gets the copied result length.</summary>
    public int PayloadLength => _payload.Length;

    /// <summary>Copies a bounded result.</summary>
    public DynamicExtensionResult(bool completed, string code, DynamicResourceClaim resourceClaim, ReadOnlySpan<byte> payload)
    {
        if (!ScopeId.TryCreate("dynamic", "result", code, out _) || !Enum.IsDefined(resourceClaim) ||
            payload.Length > DynamicExtensionRequest.MaximumPayloadBytes)
            throw new ArgumentException("Dynamic result requires bounded code, resource claim, and payload.");
        Completed = completed; Code = code; ResourceClaim = resourceClaim; _payload = payload.ToArray();
    }

    /// <summary>Returns a new result payload copy.</summary>
    public byte[] CopyPayload() => _payload.ToArray();
}

/// <summary>Defines one explicitly constructed in-process dynamic extension.</summary>
public interface IDynamicPaymentExtension
{
    /// <summary>Gets the immutable extension manifest.</summary>
    DynamicExtensionManifest Manifest { get; }
    /// <summary>Cooperatively executes one bounded request.</summary>
    ValueTask<DynamicExtensionResult> InvokeAsync(DynamicExtensionRequest request, CancellationToken cancellationToken);
}

/// <summary>Routes requests through an explicitly supplied closed extension set without reflection or service location.</summary>
public sealed class DynamicExtensionLane
{
    private readonly Dictionary<SemanticId, IDynamicPaymentExtension> _extensions;

    /// <summary>Copies and validates an explicit extension set.</summary>
    public DynamicExtensionLane(IEnumerable<IDynamicPaymentExtension> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        var items = extensions.ToArray();
        if (items.Any(x => x is null) || items.Select(x => x.Manifest.ExtensionId).Distinct().Count() != items.Length)
            throw new ArgumentException("Dynamic extension set must be non-null and uniquely identified.", nameof(extensions));
        _extensions = items.ToDictionary(x => x.Manifest.ExtensionId);
    }

    /// <summary>Validates manifest pins and invokes one extension cooperatively.</summary>
    public async ValueTask<DynamicExtensionResult> InvokeAsync(DynamicExtensionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_extensions.TryGetValue(request.ExtensionId, out var extension))
            return new(false, "extension-unavailable", DynamicResourceClaim.Unavailable, []);
        var manifest = extension.Manifest;
        if (!manifest.SignatureVerified)
            return new(false, "signature-unverified", DynamicResourceClaim.Unavailable, []);
        if (manifest.ContractVersion != request.ContractVersion)
            return new(false, "contract-skew", DynamicResourceClaim.Unavailable, []);
        if (manifest.ConfigurationRevision != request.ConfigurationRevision)
            return new(false, "configuration-stale", DynamicResourceClaim.Unavailable, []);
        return await extension.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
