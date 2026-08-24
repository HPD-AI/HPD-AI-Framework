using System.Diagnostics;
using HPD.Payments.Extensions.Dynamic;
using HPD.Payments.Primitives.Identity;

const int WarmupIterations = 1_024;
const int MeasuredIterations = 20_000;
var scope = ScopeId.Create("tenant", "perf", "dynamic");
SemanticId Id(string kind, string local) => SemanticId.Create(scope, "extensions", kind, local);
var version = ContractVersion.Create(1, 0);
var profile = new CanonicalDigestProfileId("dynamic", version, "fields", "ordinal", "utc", "ordered", "none");
CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(profile, System.Text.Encoding.UTF8.GetBytes(value));
var manifest = new DynamicExtensionManifest(Id("extension", "loopback"), version,
    Revision.Create("code", 1), Revision.Create("config", 1), Digest("artifact"), true);
var lane = new DynamicExtensionLane([new Loopback(manifest)]);
var unsignedLane = new DynamicExtensionLane([new Loopback(new DynamicExtensionManifest(manifest.ExtensionId,
    manifest.ContractVersion, manifest.CodeRevision, manifest.ConfigurationRevision, manifest.ArtifactDigest, false))]);
var payload = Enumerable.Range(0, 256).Select(static x => (byte)x).ToArray();

DynamicExtensionRequest Request(string id, SemanticId extension, ContractVersion contract, Revision config) =>
    new(Id("invocation", id), extension, contract, manifest.CodeRevision, config, manifest.ArtifactDigest, payload);
var valid = Request("valid", manifest.ExtensionId, version, manifest.ConfigurationRevision);
var skew = Request("skew", manifest.ExtensionId, ContractVersion.Create(2, 0), manifest.ConfigurationRevision);
var stale = Request("stale", manifest.ExtensionId, version, Revision.Create("config", 2));
var unavailable = Request("missing", Id("extension", "missing"), version, manifest.ConfigurationRevision);

var observations = new[]
{
    Measure("dynamic-valid-owned-copy", () =>
    {
        var result = Complete(lane.InvokeAsync(valid));
        var copy = result.CopyPayload();
        return result.Completed ? copy[0] + copy.Length : -1;
    }),
    Measure("dynamic-contract-skew", () => Complete(lane.InvokeAsync(skew)).Code.Length),
    Measure("dynamic-stale-configuration", () => Complete(lane.InvokeAsync(stale)).Code.Length),
    Measure("dynamic-extension-unavailable", () => Complete(lane.InvokeAsync(unavailable)).Code.Length),
    Measure("dynamic-signature-unverified", () => Complete(unsignedLane.InvokeAsync(valid)).Code.Length),
};

foreach (var observation in observations)
    await Console.Out.WriteLineAsync($"{observation.Path}|iterations={observation.Iterations}|allocatedBytes={observation.AllocatedBytes}|bytesPerOperation={observation.BytesPerOperation:F4}|elapsedTicks={observation.ElapsedTicks}|checksum={observation.Checksum}")
        .ConfigureAwait(false);
return observations.All(static x => x.Iterations == MeasuredIterations && x.AllocatedBytes >= 0 && x.Checksum != 0) ? 0 : 1;

static Measurement Measure(string path, Func<int> action)
{
    var checksum = 0;
    for (var i = 0; i < WarmupIterations; i++) checksum = unchecked(checksum * 31 + action());
    var before = GC.GetAllocatedBytesForCurrentThread();
    var timestamp = Stopwatch.GetTimestamp();
    for (var i = 0; i < MeasuredIterations; i++) checksum = unchecked(checksum * 31 + action());
    var elapsed = Stopwatch.GetTimestamp() - timestamp;
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    GC.KeepAlive(checksum);
    return new(path, MeasuredIterations, allocated, (double)allocated / MeasuredIterations, elapsed, checksum);
}

static DynamicExtensionResult Complete(ValueTask<DynamicExtensionResult> pending)
{
    if (!pending.IsCompletedSuccessfully)
        throw new InvalidOperationException("This exact synchronous-completion workload became asynchronous.");
    return pending.Result;
}

internal readonly record struct Measurement(string Path, int Iterations, long AllocatedBytes,
    double BytesPerOperation, long ElapsedTicks, int Checksum);

internal sealed class Loopback(DynamicExtensionManifest manifest) : IDynamicPaymentExtension
{
    public DynamicExtensionManifest Manifest { get; } = manifest;
    public ValueTask<DynamicExtensionResult> InvokeAsync(DynamicExtensionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new DynamicExtensionResult(true, "completed", DynamicResourceClaim.SoftObserved,
            request.CopyPayload()));
    }
}
