using HPD.Payments.Extensions.Dynamic;
using HPD.Payments.Primitives.Identity;

var failures = new List<string>();
void Check(bool value, string message) { if (!value) failures.Add(message); }
var scope = ScopeId.Create("tenant", "test", "dynamic");
SemanticId Id(string kind, string local) => SemanticId.Create(scope, "extensions", kind, local);
var profile = new CanonicalDigestProfileId("dynamic", ContractVersion.Create(1, 0), "fields", "ordinal", "utc", "ordered", "none");
CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(profile, System.Text.Encoding.UTF8.GetBytes(value));
var manifest = new DynamicExtensionManifest(Id("extension", "loopback"), ContractVersion.Create(1, 0),
    Revision.Create("code", 1), Revision.Create("config", 1), Digest("artifact"), true);
var lane = new DynamicExtensionLane([new Loopback(manifest)]);
var payload = new byte[] { 1, 2, 3 };
var request = new DynamicExtensionRequest(Id("invocation", "one"), manifest.ExtensionId, manifest.ContractVersion,
    manifest.ConfigurationRevision, payload);
payload[0] = 9;
var result = await lane.InvokeAsync(request).ConfigureAwait(false);
Check(result.Completed && result.Code == "completed" && result.ResourceClaim == DynamicResourceClaim.SoftObserved,
    "valid dynamic invocation failed");
Check(result.CopyPayload().SequenceEqual(new byte[] { 1, 2, 3 }), "request/result payload was aliased");
var copy = result.CopyPayload(); copy[0] = 8;
Check(result.CopyPayload()[0] == 1, "result copy escaped retained ownership");

var skew = await lane.InvokeAsync(new(Id("invocation", "two"), manifest.ExtensionId, ContractVersion.Create(2, 0),
    manifest.ConfigurationRevision, [])).ConfigureAwait(false);
Check(!skew.Completed && skew.Code == "contract-skew", "contract skew was invoked");
var stale = await lane.InvokeAsync(new(Id("invocation", "three"), manifest.ExtensionId, manifest.ContractVersion,
    Revision.Create("config", 2), [])).ConfigureAwait(false);
Check(!stale.Completed && stale.Code == "configuration-stale", "stale configuration was invoked");
var unsignedLane = new DynamicExtensionLane([new Loopback(new(manifest.ExtensionId, manifest.ContractVersion,
    manifest.CodeRevision, manifest.ConfigurationRevision, manifest.ArtifactDigest, false))]);
Check((await unsignedLane.InvokeAsync(request).ConfigureAwait(false)).Code == "signature-unverified", "unsigned artifact was invoked");
using var cancelled = new CancellationTokenSource(); await cancelled.CancelAsync().ConfigureAwait(false);
try { await lane.InvokeAsync(request, cancelled.Token).ConfigureAwait(false); failures.Add("cancellation was ignored"); }
catch (OperationCanceledException) { }
Check(typeof(DynamicExtensionLane).Assembly.GetTypes().All(x =>
    !x.FullName!.Contains("AssemblyLoadContext", StringComparison.Ordinal) &&
    !x.FullName.Contains("ServiceLocator", StringComparison.Ordinal)), "lane contains discovery/service-location types");

if (failures.Count != 0) { foreach (var failure in failures) await Console.Error.WriteLineAsync(failure).ConfigureAwait(false); return 1; }
var success = "PASS dynamic lane: explicit set, signature/version/config gates, owned bounds, cooperative cancellation, SoftObserved only";
await Console.Out.WriteLineAsync(success).ConfigureAwait(false);
return 0;

sealed class Loopback(DynamicExtensionManifest manifest) : IDynamicPaymentExtension
{
    public DynamicExtensionManifest Manifest { get; } = manifest;
    public ValueTask<DynamicExtensionResult> InvokeAsync(DynamicExtensionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new DynamicExtensionResult(true, "completed", DynamicResourceClaim.SoftObserved, request.CopyPayload()));
    }
}
