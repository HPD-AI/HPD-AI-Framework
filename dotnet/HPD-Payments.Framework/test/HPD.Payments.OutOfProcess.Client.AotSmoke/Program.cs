using HPD.Payments.Extensions.OutOfProcess;
using HPD.Payments.Primitives.Identity;

var version = ContractVersion.Create(1, 0);
var scope = ScopeId.Create("tenant", "aot", "outproc");
var requestId = SemanticId.Create(scope, "extension", "invocation", "aot-smoke");
var key = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
var payload = "bounded-aot-payload"u8.ToArray();
var wire = OutOfProcessProtocol.Encode(OutOfProcessProtocol.Create(version, OutOfProcessFrameKind.Request,
    requestId, 6, payload, key));
if (!OutOfProcessProtocol.TryDecode(wire, out var decoded) || decoded is null ||
    !OutOfProcessProtocol.Authenticate(decoded, key) || !decoded.CopyPayload().SequenceEqual(payload))
    return 4;

var client = new OutOfProcessClient(new Loopback(version, key), version, key);
var success = await client.InvokeAsync(requestId, 7, payload).ConfigureAwait(false);
if (success.State != OutOfProcessTransportState.ResponseReceived || success.Response is not { } response ||
    !response.CopyPayload().SequenceEqual(payload))
    return 1;

var skewed = new OutOfProcessClient(new Loopback(ContractVersion.Create(2, 0), key), version, key);
var skewResult = await skewed.InvokeAsync(requestId, 8, payload).ConfigureAwait(false);
if (skewResult.State != OutOfProcessTransportState.PossibleDispatch || skewResult.Code != "protocol-skew")
    return 2;

var ambiguous = new OutOfProcessClient(new AmbiguousTransport(), version, key);
var ambiguousResult = await ambiguous.InvokeAsync(requestId, 9, payload).ConfigureAwait(false);
if (ambiguousResult.State != OutOfProcessTransportState.PossibleDispatch || ambiguousResult.Response is not null)
    return 3;

await Console.Out.WriteLineAsync("PASS outproc client AOT: strict codec, authenticated loopback, version skew, possible dispatch").ConfigureAwait(false);
return 0;

internal sealed class Loopback(ContractVersion responseVersion, byte[] key) : IOutOfProcessTransport
{
    public ValueTask<OutOfProcessTransportResult> ExchangeAsync(OutOfProcessFrame request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = OutOfProcessProtocol.Create(responseVersion, OutOfProcessFrameKind.Response,
            request.RequestId, request.Nonce, request.CopyPayload(), key);
        return ValueTask.FromResult(new OutOfProcessTransportResult(
            OutOfProcessTransportState.ResponseReceived, response, "response"));
    }
}

internal sealed class AmbiguousTransport : IOutOfProcessTransport
{
    public ValueTask<OutOfProcessTransportResult> ExchangeAsync(OutOfProcessFrame request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new OutOfProcessTransportResult(
            OutOfProcessTransportState.PossibleDispatch, null, "lost-response"));
    }
}
