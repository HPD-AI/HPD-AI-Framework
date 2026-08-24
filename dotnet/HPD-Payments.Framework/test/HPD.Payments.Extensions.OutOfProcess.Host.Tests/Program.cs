using System.Buffers.Binary;
using System.Diagnostics;
using HPD.Payments.Extensions.Dynamic;
using HPD.Payments.Extensions.OutOfProcess;
using HPD.Payments.Extensions.OutOfProcess.Host;
using HPD.Payments.Primitives.Identity;

if (args is ["--stdio-child"])
    return await RunChildAsync(crashAfterRead: false).ConfigureAwait(false);
if (args is ["--stdio-child-crash-after-read"])
    return await RunChildAsync(crashAfterRead: true).ConfigureAwait(false);

var failures = new List<string>();
void Check(bool value, string message) { if (!value) failures.Add(message); }
var scope = ScopeId.Create("tenant", "test", "outproc-host");
SemanticId Id(string kind, string local) => SemanticId.Create(scope, "extensions", kind, local);
var version = ContractVersion.Create(1, 0);
var key = Enumerable.Range(1, 32).Select(x => (byte)x).ToArray();
var profile = new CanonicalDigestProfileId("host", version, "fields", "ordinal", "utc", "ordered", "none");
var manifest = new DynamicExtensionManifest(Id("extension", "loopback"), version, Revision.Create("code", 1),
    Revision.Create("config", 1), CanonicalDigest.Sha256(profile, "artifact"u8), true);
var host = new AuthenticatedOutOfProcessHost(new Loopback(manifest), version, key);
var requestId = Id("request", "one");
var request = OutOfProcessProtocol.Create(version, OutOfProcessFrameKind.Request, requestId, 1, new byte[] { 4, 5 }, key);
var response = await host.ProcessAsync(request).ConfigureAwait(false);
var responseFrame = response.Response;
Check(response.State == OutOfProcessTransportState.ResponseReceived &&
    responseFrame is not null &&
    OutOfProcessProtocol.Authenticate(responseFrame, key) &&
    responseFrame.CopyPayload().SequenceEqual(new byte[] { 4, 5 }), "authenticated host exchange failed");
var replay = await host.ProcessAsync(request).ConfigureAwait(false);
Check(replay.Code == "nonce-replay" && replay.State == OutOfProcessTransportState.DefiniteNotSent, "nonce replay reached extension");
var wrongKey = Enumerable.Range(33, 32).Select(x => (byte)x).ToArray();
var unauthenticated = await host.ProcessAsync(
    OutOfProcessProtocol.Create(version, OutOfProcessFrameKind.Request, requestId, 2, [], wrongKey)).ConfigureAwait(false);
Check(unauthenticated.Code == "request-authentication-failed", "unauthenticated request reached extension");
var skew = await host.ProcessAsync(
    OutOfProcessProtocol.Create(ContractVersion.Create(2, 0), OutOfProcessFrameKind.Request, requestId, 3, [], key)).ConfigureAwait(false);
Check(skew.Code == "protocol-skew", "protocol skew reached extension");

var processSuccess = await ExchangeWithChildAsync(request, crashAfterRead: false).ConfigureAwait(false);
Check(processSuccess.State == OutOfProcessTransportState.ResponseReceived && processSuccess.Response is { } processFrame &&
    OutOfProcessProtocol.Authenticate(processFrame, key) && processFrame.CopyPayload().SequenceEqual(new byte[] { 4, 5 }),
    "real child-process authenticated stdio exchange failed");
var processCrash = await ExchangeWithChildAsync(
    OutOfProcessProtocol.Create(version, OutOfProcessFrameKind.Request, requestId, 4, [8, 9], key),
    crashAfterRead: true).ConfigureAwait(false);
Check(processCrash.State == OutOfProcessTransportState.PossibleDispatch && processCrash.Response is null &&
    processCrash.Code == "child-exited-after-request-read",
    "child crash after completed IPC write was flattened into definite-not-sent or completion");
var productionScope = ScopeId.Create("hpd-payments", "outproc", "loopback-v1");
var productionRequest = OutOfProcessProtocol.Create(version, OutOfProcessFrameKind.Request,
    SemanticId.Create(productionScope, "extensions", "request", "production-one"), 5, [10, 11], key);
var productionResult = await ExchangeWithProductionHostAsync(productionRequest, key).ConfigureAwait(false);
Check(productionResult.State == OutOfProcessTransportState.ResponseReceived &&
    productionResult.Response is { } productionFrame && OutOfProcessProtocol.Authenticate(productionFrame, key) &&
    productionFrame.CopyPayload().SequenceEqual(new byte[] { 10, 11 }), "production stdio host exchange failed");

if (failures.Count != 0) { foreach (var failure in failures) await Console.Error.WriteLineAsync(failure).ConfigureAwait(false); return 1; }
var message = "PASS outproc host: auth/version/replay, real stdio process exchange, crash-after-read possible dispatch";
await Console.Out.WriteLineAsync(message).ConfigureAwait(false);
return 0;

static async Task<int> RunChildAsync(bool crashAfterRead)
{
    using var input = Console.OpenStandardInput();
    var lengthBytes = new byte[4];
    if (!await ReadExactAsync(input, lengthBytes).ConfigureAwait(false)) return 80;
    var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
    if (length is < 1 or > OutOfProcessFrame.MaximumPayloadBytes + 4096) return 81;
    var wire = new byte[length];
    if (!await ReadExactAsync(input, wire).ConfigureAwait(false) ||
        !OutOfProcessProtocol.TryDecode(wire, out var request) || request is null) return 82;
    if (crashAfterRead) return 91;

    var childVersion = ContractVersion.Create(1, 0);
    var childKey = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
    var childScope = ScopeId.Create("tenant", "test", "outproc-host");
    SemanticId ChildId(string kind, string local) => SemanticId.Create(childScope, "extensions", kind, local);
    var childProfile = new CanonicalDigestProfileId("host", childVersion, "fields", "ordinal", "utc", "ordered", "none");
    var childManifest = new DynamicExtensionManifest(ChildId("extension", "loopback"), childVersion,
        Revision.Create("code", 1), Revision.Create("config", 1), CanonicalDigest.Sha256(childProfile, "artifact"u8), true);
    var childHost = new AuthenticatedOutOfProcessHost(new Loopback(childManifest), childVersion, childKey);
    var result = await childHost.ProcessAsync(request).ConfigureAwait(false);
    if (result.State != OutOfProcessTransportState.ResponseReceived || result.Response is null) return 83;
    var responseWire = OutOfProcessProtocol.Encode(result.Response);
    BinaryPrimitives.WriteInt32BigEndian(lengthBytes, responseWire.Length);
    using var output = Console.OpenStandardOutput();
    await output.WriteAsync(lengthBytes).ConfigureAwait(false);
    await output.WriteAsync(responseWire).ConfigureAwait(false);
    await output.FlushAsync().ConfigureAwait(false);
    return 0;
}

static async Task<OutOfProcessTransportResult> ExchangeWithChildAsync(OutOfProcessFrame request, bool crashAfterRead)
{
    var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Process executable is unavailable.");
    var start = new ProcessStartInfo(executable) { RedirectStandardInput = true, RedirectStandardOutput = true,
        RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
    start.ArgumentList.Add(crashAfterRead ? "--stdio-child-crash-after-read" : "--stdio-child");
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Child process did not start.");
    var wire = OutOfProcessProtocol.Encode(request);
    var lengthBytes = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(lengthBytes, wire.Length);
    await process.StandardInput.BaseStream.WriteAsync(lengthBytes).ConfigureAwait(false);
    await process.StandardInput.BaseStream.WriteAsync(wire).ConfigureAwait(false);
    await process.StandardInput.BaseStream.FlushAsync().ConfigureAwait(false);
    process.StandardInput.Close();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    if (crashAfterRead)
    {
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        return new(OutOfProcessTransportState.PossibleDispatch, null, "child-exited-after-request-read");
    }
    if (!await ReadExactAsync(process.StandardOutput.BaseStream, lengthBytes).ConfigureAwait(false))
        return new(OutOfProcessTransportState.PossibleDispatch, null, "child-response-missing");
    var responseLength = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
    if (responseLength is < 1 or > OutOfProcessFrame.MaximumPayloadBytes + 4096)
        return new(OutOfProcessTransportState.PossibleDispatch, null, "child-response-over-bound");
    var responseWire = new byte[responseLength];
    if (!await ReadExactAsync(process.StandardOutput.BaseStream, responseWire).ConfigureAwait(false) ||
        !OutOfProcessProtocol.TryDecode(responseWire, out var response) || response is null)
        return new(OutOfProcessTransportState.PossibleDispatch, null, "child-response-invalid");
    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
    return process.ExitCode == 0
        ? new(OutOfProcessTransportState.ResponseReceived, response, "response")
        : new(OutOfProcessTransportState.PossibleDispatch, null, "child-exit-nonzero");
}

static async Task<OutOfProcessTransportResult> ExchangeWithProductionHostAsync(OutOfProcessFrame request, byte[] key)
{
    string? nativeHost = Environment.GetEnvironmentVariable("HPD_PAYMENTS_PRODUCTION_HOST_PATH");
    string hostAssembly = Path.Combine(AppContext.BaseDirectory, "HPD.Payments.Extensions.OutOfProcess.Host.dll");
    var start = new ProcessStartInfo(nativeHost ?? "/usr/local/share/dotnet/dotnet")
    {
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        UseShellExecute = false, CreateNoWindow = true,
    };
    if (nativeHost is null) start.ArgumentList.Add(hostAssembly);
    start.ArgumentList.Add("--stdio-loopback");
    start.Environment["HPD_PAYMENTS_OUTPROC_KEY_HEX"] = Convert.ToHexString(key);
    using Process process = Process.Start(start) ?? throw new InvalidOperationException("Production host did not start.");
    byte[] wire = OutOfProcessProtocol.Encode(request);
    var lengthBytes = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(lengthBytes, wire.Length);
    await process.StandardInput.BaseStream.WriteAsync(lengthBytes).ConfigureAwait(false);
    await process.StandardInput.BaseStream.WriteAsync(wire).ConfigureAwait(false);
    await process.StandardInput.BaseStream.FlushAsync().ConfigureAwait(false);
    process.StandardInput.Close();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    if (!await ReadExactAsync(process.StandardOutput.BaseStream, lengthBytes).ConfigureAwait(false))
        return new(OutOfProcessTransportState.PossibleDispatch, null, "production-response-missing");
    int responseLength = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
    if (responseLength is < 1 or > OutOfProcessFrame.MaximumPayloadBytes + 4096)
        return new(OutOfProcessTransportState.PossibleDispatch, null, "production-response-over-bound");
    var responseWire = new byte[responseLength];
    if (!await ReadExactAsync(process.StandardOutput.BaseStream, responseWire).ConfigureAwait(false) ||
        !OutOfProcessProtocol.TryDecode(responseWire, out OutOfProcessFrame? response) || response is null)
        return new(OutOfProcessTransportState.PossibleDispatch, null, "production-response-invalid");
    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
    return process.ExitCode == 0
        ? new(OutOfProcessTransportState.ResponseReceived, response, "response")
        : new(OutOfProcessTransportState.PossibleDispatch, null, "production-exit-nonzero");
}

static async Task<bool> ReadExactAsync(Stream stream, Memory<byte> destination)
{
    var offset = 0;
    while (offset < destination.Length)
    {
        var read = await stream.ReadAsync(destination[offset..]).ConfigureAwait(false);
        if (read == 0) return false;
        offset += read;
    }
    return true;
}

sealed class Loopback(DynamicExtensionManifest manifest) : IDynamicPaymentExtension
{
    public DynamicExtensionManifest Manifest { get; } = manifest;
    public ValueTask<DynamicExtensionResult> InvokeAsync(DynamicExtensionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new DynamicExtensionResult(true, "completed", DynamicResourceClaim.SoftObserved, request.CopyPayload()));
    }
}
