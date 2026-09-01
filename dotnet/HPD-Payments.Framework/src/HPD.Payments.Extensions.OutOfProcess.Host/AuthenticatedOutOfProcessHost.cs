using HPD.Payments.Extensions.Dynamic;
using HPD.Payments.Extensions.OutOfProcess;
using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Extensions.OutOfProcess.Host;

/// <summary>Processes authenticated loopback protocol frames through one explicitly bound dynamic extension.</summary>
/// <remarks>Process, pipe, socket, sandbox, and hard resource containment remain deployment responsibilities.</remarks>
internal sealed class AuthenticatedOutOfProcessHost
{
    private readonly DynamicExtensionLane _lane;
    private readonly DynamicExtensionManifest _manifest;
    private readonly ContractVersion _protocolVersion;
    private readonly byte[] _key;
    private readonly HashSet<ulong> _nonces = [];
    private readonly object _gate = new();

    /// <summary>Creates a host bound to one explicit extension manifest and protocol key.</summary>
    public AuthenticatedOutOfProcessHost(IDynamicPaymentExtension extension, ContractVersion protocolVersion, ReadOnlySpan<byte> key)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (!protocolVersion.IsValid || key.Length is < 32 or > OutOfProcessProtocol.MaximumKeyBytes)
            throw new ArgumentException("Host requires extension, protocol version, and bounded key.");
        _manifest = extension.Manifest; _lane = new([extension]); _protocolVersion = protocolVersion; _key = key.ToArray();
    }

    /// <summary>Authenticates, replay-fences, invokes, and authenticates one response.</summary>
    public async ValueTask<OutOfProcessTransportResult> ProcessAsync(OutOfProcessFrame request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Kind != OutOfProcessFrameKind.Request)
            return new(OutOfProcessTransportState.DefiniteNotSent, null, "request-kind-invalid");
        if (request.ProtocolVersion != _protocolVersion)
            return new(OutOfProcessTransportState.DefiniteNotSent, null, "protocol-skew");
        if (!OutOfProcessProtocol.Authenticate(request, _key))
            return new(OutOfProcessTransportState.DefiniteNotSent, null, "request-authentication-failed");
        lock (_gate)
        {
            if (!_nonces.Add(request.Nonce))
                return new(OutOfProcessTransportState.DefiniteNotSent, null, "nonce-replay");
        }

        var dynamicRequest = new DynamicExtensionRequest(request.RequestId, _manifest.ExtensionId,
            _manifest.ContractVersion, _manifest.CodeRevision, _manifest.ConfigurationRevision,
            _manifest.ArtifactDigest, request.CopyPayload());
        var result = await _lane.InvokeAsync(dynamicRequest, cancellationToken).ConfigureAwait(false);
        var responsePayload = result.CopyPayload();
        var response = OutOfProcessProtocol.Create(_protocolVersion, OutOfProcessFrameKind.Response,
            request.RequestId, request.Nonce, responsePayload, _key);
        return new(OutOfProcessTransportState.ResponseReceived, response, result.Code);
    }
}
