using HPD.Payments.Extensions.OutOfProcess;
using HPD.Payments.Primitives.Identity;

var failures = new List<string>();
void Check(bool value, string message) { if (!value) failures.Add(message); }
var scope = ScopeId.Create("tenant", "test", "outproc");
SemanticId Id(string local) => SemanticId.Create(scope, "extensions", "request", local);
var version = ContractVersion.Create(1, 0);
var key = Enumerable.Range(1, 32).Select(x => (byte)x).ToArray();
var requestId = Id("one");
var encodedRequest = OutOfProcessProtocol.Encode(OutOfProcessProtocol.Create(version,
    OutOfProcessFrameKind.Request, requestId, 99, [7, 8, 9], key));
Check(OutOfProcessProtocol.TryDecode(encodedRequest, out var decodedRequest) && decodedRequest is not null &&
    decodedRequest.RequestId == requestId && decodedRequest.Nonce == 99 &&
    decodedRequest.CopyPayload().SequenceEqual(new byte[] { 7, 8, 9 }) &&
    OutOfProcessProtocol.Authenticate(decodedRequest, key), "strict frame codec did not round-trip authenticated bytes");
var trailingFrame = encodedRequest.Concat(new byte[] { 0 }).ToArray();
var unknownSchemaFrame = encodedRequest.ToArray(); unknownSchemaFrame[4] = 2;
var tamperedFrame = encodedRequest.ToArray(); tamperedFrame[^33] ^= 1;
Check(!OutOfProcessProtocol.TryDecode(encodedRequest.AsSpan(0, encodedRequest.Length - 1), out _) &&
    !OutOfProcessProtocol.TryDecode(trailingFrame, out _) &&
    !OutOfProcessProtocol.TryDecode(unknownSchemaFrame, out _) &&
    OutOfProcessProtocol.TryDecode(tamperedFrame, out var decodedTamper) && decodedTamper is not null &&
    !OutOfProcessProtocol.Authenticate(decodedTamper, key),
    "truncated, trailing, unknown-schema, or unauthenticated wire frame was accepted");

var successClient = new OutOfProcessClient(new Loopback(version, key, LoopbackMode.Success), version, key);
var response = await successClient.InvokeAsync(requestId, 1, new byte[] { 1, 2, 3 }).ConfigureAwait(false);
Check(response.State == OutOfProcessTransportState.ResponseReceived && response.Response!.CopyPayload().SequenceEqual(new byte[] { 1, 2, 3 }),
    "authenticated loopback response failed");
var possible = await new OutOfProcessClient(new Loopback(version, key, LoopbackMode.PossibleDispatch), version, key)
    .InvokeAsync(requestId, 2, ReadOnlyMemory<byte>.Empty).ConfigureAwait(false);
Check(possible.State == OutOfProcessTransportState.PossibleDispatch, "host crash/partition was flattened into definite failure");
var notSent = await new OutOfProcessClient(new Loopback(version, key, LoopbackMode.DefiniteNotSent), version, key)
    .InvokeAsync(requestId, 3, ReadOnlyMemory<byte>.Empty).ConfigureAwait(false);
Check(notSent.State == OutOfProcessTransportState.DefiniteNotSent, "pre-write failure lost definite-not-sent evidence");
var skew = await new OutOfProcessClient(new Loopback(ContractVersion.Create(2, 0), key, LoopbackMode.Success), version, key)
    .InvokeAsync(requestId, 4, ReadOnlyMemory<byte>.Empty).ConfigureAwait(false);
Check(skew.Code == "protocol-skew" && skew.State == OutOfProcessTransportState.PossibleDispatch, "protocol skew was accepted");
var wrongKey = Enumerable.Range(33, 32).Select(x => (byte)x).ToArray();
var unauthenticated = await new OutOfProcessClient(new Loopback(version, wrongKey, LoopbackMode.Success), version, key)
    .InvokeAsync(requestId, 5, ReadOnlyMemory<byte>.Empty).ConfigureAwait(false);
Check(unauthenticated.Code == "response-authentication-failed", "unauthenticated response was accepted");

if (failures.Count != 0) { foreach (var failure in failures) await Console.Error.WriteLineAsync(failure).ConfigureAwait(false); return 1; }
var message = "PASS outproc client: strict codec, auth/version/binding, definite-not-sent, possible-dispatch, response-received";
await Console.Out.WriteLineAsync(message).ConfigureAwait(false);
return 0;

enum LoopbackMode { Success, DefiniteNotSent, PossibleDispatch }

sealed class Loopback(ContractVersion responseVersion, byte[] key, LoopbackMode mode) : IOutOfProcessTransport
{
    public ValueTask<OutOfProcessTransportResult> ExchangeAsync(OutOfProcessFrame request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return mode switch
        {
            LoopbackMode.DefiniteNotSent => ValueTask.FromResult(new OutOfProcessTransportResult(
                OutOfProcessTransportState.DefiniteNotSent, null, "pre-write-failure")),
            LoopbackMode.PossibleDispatch => ValueTask.FromResult(new OutOfProcessTransportResult(
                OutOfProcessTransportState.PossibleDispatch, null, "host-crash-after-write")),
            _ => ValueTask.FromResult(new OutOfProcessTransportResult(OutOfProcessTransportState.ResponseReceived,
                OutOfProcessProtocol.Create(responseVersion, OutOfProcessFrameKind.Response, request.RequestId,
                    request.Nonce, request.CopyPayload(), key), "response")),
        };
    }
}
