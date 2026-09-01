using HPD.Payments.Extensions.Dynamic;
using HPD.Payments.Extensions.OutOfProcess.Host;
using HPD.Payments.Primitives.Identity;

if (args is not ["--stdio-loopback"])
{
    await Console.Error.WriteLineAsync("Usage: HPD.Payments.Extensions.OutOfProcess.Host --stdio-loopback").ConfigureAwait(false);
    return 64;
}

string? encodedKey = Environment.GetEnvironmentVariable("HPD_PAYMENTS_OUTPROC_KEY_HEX");
if (string.IsNullOrWhiteSpace(encodedKey) || encodedKey.Length is < 64 or > 2048 || encodedKey.Length % 2 != 0)
{
    await Console.Error.WriteLineAsync("HPD_PAYMENTS_OUTPROC_KEY_HEX must contain 32-1024 key bytes.").ConfigureAwait(false);
    return 78;
}

byte[] key;
try { key = Convert.FromHexString(encodedKey); }
catch (FormatException)
{
    await Console.Error.WriteLineAsync("HPD_PAYMENTS_OUTPROC_KEY_HEX is malformed.").ConfigureAwait(false);
    return 78;
}
if (key.Length is < 32 or > 1024) return 78;

ContractVersion version = ContractVersion.Create(1, 0);
ScopeId scope = ScopeId.Create("hpd-payments", "outproc", "loopback-v1");
SemanticId extensionId = SemanticId.Create(scope, "extensions", "extension", "loopback");
var profile = new CanonicalDigestProfileId("outproc-host", version, "fields", "ordinal", "utc", "ordered", "none");
var manifest = new DynamicExtensionManifest(extensionId, version, Revision.Create("code", 1),
    Revision.Create("config", 1), CanonicalDigest.Sha256(profile, "loopback-v1"u8), true);
var host = new AuthenticatedOutOfProcessHost(new LoopbackExtension(manifest), version, key);
using Stream input = Console.OpenStandardInput();
using Stream output = Console.OpenStandardOutput();
return await StdioOutOfProcessHost.RunSingleAsync(host, input, output).ConfigureAwait(false);

internal sealed class LoopbackExtension(DynamicExtensionManifest manifest) : IDynamicPaymentExtension
{
    public DynamicExtensionManifest Manifest { get; } = manifest;
    public ValueTask<DynamicExtensionResult> InvokeAsync(DynamicExtensionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new DynamicExtensionResult(true, "completed", DynamicResourceClaim.SoftObserved,
            request.CopyPayload()));
    }
}
