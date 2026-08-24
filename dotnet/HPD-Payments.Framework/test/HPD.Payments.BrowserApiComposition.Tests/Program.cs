using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

var productRoot = Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(productRoot, "eng", "commands", "commands.json")))
    throw new InvalidOperationException("Composition proof must run from the HPD.Payments product root.");

var hostAssembly = Path.Combine(productRoot, "src", "HPD.Payments.Host.Api", "bin", "Release", "net10.0", "HPD.Payments.Host.Api.dll");
var workerAssembly = Path.Combine(productRoot, "src", "HPD.Payments.Worker", "bin", "Release", "net10.0", "HPD.Payments.Worker.dll");
var browserProof = Path.Combine(productRoot, "test", "HPD.Payments.BrowserApiComposition.Tests", "browser-api-integration.mjs");
RequireFile(hostAssembly);
RequireFile(workerAssembly);
RequireFile(browserProof);

await ProveProfileAsync("--inmemory", "EmbeddedInMemory").ConfigureAwait(false);
await ProveProfileAsync("--sqlite", "EmbeddedSqlite").ConfigureAwait(false);

var ambiguous = await RunAsync("dotnet", [hostAssembly, "--inmemory", "--sqlite"], productRoot).ConfigureAwait(false);
if (ambiguous.ExitCode != 64) throw new InvalidOperationException("Ambiguous profile selection did not fail closed.");

var worker = await RunAsync("dotnet", [workerAssembly, "--self-test"], productRoot).ConfigureAwait(false);
if (worker.ExitCode != 0 || !worker.StandardOutput.Contains("claim epoch and crash-expiry reconciliation", StringComparison.Ordinal))
    throw new InvalidOperationException("Worker process proof failed.");

Console.WriteLine("PASS Browser/API/Worker: real processes, exact profiles, version rejection, and fenced worker");
return 0;

async Task ProveProfileAsync(string profileArgument, string expectedProfile)
{
    var port = ReservePort();
    var baseUrl = $"http://127.0.0.1:{port}";
    using var host = Start("dotnet", [hostAssembly, profileArgument], productRoot,
        new Dictionary<string, string?> { ["ASPNETCORE_URLS"] = baseUrl, ["DOTNET_NOLOGO"] = "1" });
    try
    {
        await WaitUntilReadyAsync(baseUrl, host).ConfigureAwait(false);
        var browser = await RunAsync("node", [browserProof, baseUrl, expectedProfile], productRoot).ConfigureAwait(false);
        if (browser.ExitCode != 0 || !browser.StandardOutput.Contains($"profile={expectedProfile}", StringComparison.Ordinal))
            throw new InvalidOperationException($"Browser/API proof failed for {expectedProfile}: {browser.StandardError}");
    }
    finally
    {
        if (!host.HasExited) host.Kill(entireProcessTree: true);
        await host.WaitForExitAsync().ConfigureAwait(false);
    }
}

static async Task WaitUntilReadyAsync(string baseUrl, Process host)
{
    using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(250) };
    client.DefaultRequestHeaders.Add("x-hpd-payments-version", "hpd.payments.api.v1");
    for (var attempt = 0; attempt < 80; attempt++)
    {
        if (host.HasExited) throw new InvalidOperationException("API host exited before readiness.");
        try
        {
            using var response = await client.GetAsync(new Uri($"{baseUrl}/hpd/payments/v1/health", UriKind.Absolute)).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK) return;
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        await Task.Delay(50).ConfigureAwait(false);
    }
    throw new TimeoutException("API host readiness timed out.");
}

static int ReservePort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static Process Start(string fileName, IReadOnlyList<string> arguments, string workingDirectory,
    IReadOnlyDictionary<string, string?>? environment = null)
{
    var start = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true,
        RedirectStandardError = true, UseShellExecute = false };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    if (environment is not null)
        foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
    return Process.Start(start) ?? throw new InvalidOperationException($"Could not start {fileName}.");
}

static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAsync(
    string fileName, IReadOnlyList<string> arguments, string workingDirectory)
{
    using var process = Start(fileName, arguments, workingDirectory);
    var standardOutput = process.StandardOutput.ReadToEndAsync();
    var standardError = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
    return (process.ExitCode, await standardOutput.ConfigureAwait(false), await standardError.ConfigureAwait(false));
}

static void RequireFile(string path)
{
    if (!File.Exists(path)) throw new FileNotFoundException("Required composition artifact is missing.", path);
}
