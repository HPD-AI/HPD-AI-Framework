#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

// Silero VAD qualification gate: architecture guard, corpus tests, warnings-as-errors
// provider build, NativeAOT publish, and the million-window soak.
//
// Port of eng/qualify-silero-vad.sh. Success lines are stable so CI can grep them.
const string Commit = "be95df9152c0d7618fa1edfeb296fc3dae32376f";
const string ModelExpectedSha256 = "1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3";
const string CorpusExpectedSha256 = "89f17d9c94c4b31eb320f424628bcbc920abaddbee6e2760fd868bfb1d9a2e47";

var root = ResolveFrameworkRoot();
var runtimeIdentifier = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
    ? args[0].Trim()
    : await InferRuntimeIdentifierAsync();

string[] supported = ["linux-arm64", "linux-x64", "osx-arm64", "osx-x64", "win-arm64", "win-x64"];
if (!supported.Contains(runtimeIdentifier, StringComparer.Ordinal))
{
    Console.Error.WriteLine($"Unsupported Silero qualification RID: {runtimeIdentifier}");
    return 2;
}

var work = Path.Combine(Path.GetTempPath(), $"hpd-silero-qualification.{Guid.NewGuid():N}");
Directory.CreateDirectory(work);

try
{
    var modelPath = await FetchQualificationInputsAsync(root);

    var tests = Path.Combine(root, "test", "HPD.Agent.Audio.V2.Tests", "HPD.Agent.Audio.V2.Tests.csproj");
    var provider = Path.Combine(
        root, "src", "HPD-Agent.Providers.Audio", "HPD-Agent.Providers.Audio.Silero", "HPD-Agent.Providers.Audio.Silero.csproj");
    var smoke = Path.Combine(
        root, "test", "HPD-Agent.Audio.VoiceActivity.AotSmoke", "HPD-Agent.Audio.VoiceActivity.AotSmoke.csproj");

    await RunAsync(
        ["run", "--file", Path.Combine(root, "eng", "verify-voice-activity-architecture.cs")],
        Path.Combine(work, "architecture.log"),
        environment: null,
        root);
    Console.WriteLine("voice-activity-architecture=pass");

    foreach (var framework in new[] { "net8.0", "net9.0", "net10.0" })
    {
        var environment = new Dictionary<string, string> { ["HPD_SILERO_VAD_MODEL_PATH"] = modelPath };
        var testLog = Path.Combine(work, $"test-{framework}.log");
        await RunAsync(
            ["test", tests, "-f", framework, "--filter", "FullyQualifiedName~AudioProviderV9ContractTests", "-v:q"],
            testLog,
            environment,
            root);

        // `dotnet test` exits 0 when a filter matches nothing, so an exit code alone previously
        // let this gate report success while running no tests at all. Demand real evidence.
        var executed = CountExecutedTests(await File.ReadAllLinesAsync(testLog));
        if (executed == 0)
            throw new InvalidOperationException($"The {framework} provider-contract filter matched no tests.");

        Console.WriteLine($"silero-tests-{framework}=pass tests={executed}");
    }

    await RunAsync(
        ["build", provider, "-f", "net10.0", "--no-dependencies", "-warnaserror", "-v:q"],
        Path.Combine(work, "warnings-as-errors.log"),
        environment: null,
        root);
    Console.WriteLine("silero-warnings-as-errors=pass");

    await RunAsync(
        ["publish", smoke, "-c", "Release", "-r", runtimeIdentifier, "--self-contained", "true", "-o", Path.Combine(work, "publish"), "-v:q"],
        Path.Combine(work, "aot-publish.log"),
        environment: null,
        root);

    var executable = Path.Combine(
        work, "publish", "HPD-Agent.Audio.VoiceActivity.AotSmoke" + (runtimeIdentifier.StartsWith("win-", StringComparison.Ordinal) ? ".exe" : string.Empty));
    var aotRunLog = Path.Combine(work, "aot-run.log");
    await RunAsync(
        [executable],
        aotRunLog,
        new Dictionary<string, string>
        {
            ["HPD_SILERO_VAD_MODEL_PATH"] = modelPath,
            ["HPD_SILERO_SOAK_WINDOWS"] = "1000000"
        },
        root,
        executableIsDirect: true);

    var aotLines = await File.ReadAllLinesAsync(aotRunLog);
    if (!aotLines.Contains("voice-activity-aot=pass", StringComparer.Ordinal))
        throw new InvalidOperationException("The NativeAOT smoke did not report voice-activity-aot=pass.");

    foreach (var line in aotLines.Where(line => line.StartsWith("silero-soak-windows=", StringComparison.Ordinal)))
        Console.WriteLine(line);

    Console.WriteLine($"silero-native-aot-{runtimeIdentifier}=pass");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"silero-qualification-{runtimeIdentifier}=fail");
    if (exception is not StepFailedException)
        Console.Error.WriteLine(exception.Message);

    // Surface the failing step's own log first, then anything else that looks like an error.
    if (exception is StepFailedException stepFailure && File.Exists(stepFailure.LogPath))
        await DumpErrorsAsync(stepFailure.LogPath);

    foreach (var log in Directory.GetFiles(work, "*.log"))
    {
        if (exception is StepFailedException step && string.Equals(log, step.LogPath, StringComparison.Ordinal))
            continue;
        await DumpErrorsAsync(log);
    }

    return exception is StepFailedException stepFailed ? stepFailed.ExitCode : 1;
}
finally
{
    try
    {
        Directory.Delete(work, recursive: true);
    }
    catch (IOException)
    {
        // Qualification is authoritative; a leftover temp directory is not a gate failure.
    }
}

// Pinned inputs. Inlined rather than shelling out so stdout stays reserved for the
// model path that callers capture.
static async Task<string> FetchQualificationInputsAsync(string root)
{
    var directory = Path.Combine(root, "artifacts", "silero-vad", "v6.2");
    var modelPath = Path.Combine(directory, "silero_vad.onnx");
    var corpusPath = Path.Combine(directory, "test.wav");

    Directory.CreateDirectory(directory);

    using var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HPD-SileroQualification/1.0");

    await EnsureAsync(
        httpClient,
        ModelExpectedSha256,
        modelPath,
        $"https://raw.githubusercontent.com/snakers4/silero-vad/{Commit}/src/silero_vad/data/silero_vad.onnx");
    await EnsureAsync(
        httpClient,
        CorpusExpectedSha256,
        corpusPath,
        $"https://raw.githubusercontent.com/snakers4/silero-vad/{Commit}/tests/data/test.wav");

    return modelPath;
}

static async Task EnsureAsync(HttpClient httpClient, string expectedSha256, string destination, string url)
{
    if (File.Exists(destination) && await MatchesAsync(destination, expectedSha256))
        return;

    var fileName = Path.GetFileName(destination);
    Console.Error.WriteLine($"silero-qualification: downloading {fileName}");

    var temporary = destination + ".download";
    try
    {
        using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = File.Create(temporary);
            await source.CopyToAsync(target);
        }

        var actual = await ComputeSha256Async(temporary);
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Silero digest mismatch for {fileName}: expected {expectedSha256}, found {actual}.");
        }

        File.Move(temporary, destination, overwrite: true);
    }
    finally
    {
        if (File.Exists(temporary))
            File.Delete(temporary);
    }
}

static async Task<bool> MatchesAsync(string path, string expectedSha256) =>
    string.Equals(await ComputeSha256Async(path), expectedSha256, StringComparison.OrdinalIgnoreCase);

static async Task<string> ComputeSha256Async(string path)
{
    await using var stream = File.OpenRead(path);
    return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
}

static async Task<string> InferRuntimeIdentifierAsync()
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    startInfo.ArgumentList.Add("--info");

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start dotnet to infer the runtime identifier.");
    var output = await process.StandardOutput.ReadToEndAsync();
    await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    foreach (var line in ReadLines(output))
    {
        var separator = line.IndexOf(':');
        if (separator < 0 || !line.AsSpan(0, separator).Trim().SequenceEqual("RID"))
            continue;

        var value = line[(separator + 1)..].Trim();
        if (value.Length > 0)
            return value;
    }

    throw new InvalidOperationException("Could not infer a runtime identifier from `dotnet --info`.");
}

static async Task RunAsync(
    IReadOnlyList<string> arguments,
    string logPath,
    IReadOnlyDictionary<string, string>? environment,
    string workingDirectory,
    bool executableIsDirect = false)
{
    var startInfo = new ProcessStartInfo(executableIsDirect ? arguments[0] : "dotnet")
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    if (!executableIsDirect)
    {
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
    }
    else
    {
        for (var index = 1; index < arguments.Count; index++)
            startInfo.ArgumentList.Add(arguments[index]);
    }

    if (environment is not null)
    {
        foreach (var pair in environment)
            startInfo.Environment[pair.Key] = pair.Value;
    }

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Failed to start process: {startInfo.FileName}");
    var standardOutput = process.StandardOutput.ReadToEndAsync();
    var standardError = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    var combined = new StringBuilder()
        .Append(await standardOutput)
        .Append(await standardError)
        .ToString();
    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
    await File.WriteAllTextAsync(logPath, combined, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    if (process.ExitCode != 0)
        throw new StepFailedException(arguments[0], process.ExitCode, logPath);
}

static IEnumerable<string> ReadLines(string text)
{
    using var reader = new StringReader(text);
    while (reader.ReadLine() is { } line)
        yield return line;
}

// Echoes the interesting lines from one step log, bounded like the shell gate's `tail`.
// Matches bare `error :` lines too, since MSBuild task failures (for example a missing
// platform linker) carry no diagnostic code.
static async Task DumpErrorsAsync(string log)
{
    var errorPattern = new Regex(
        @"error|FAILED|Exception|Expected:|Actual:|\[FAIL\]|No space",
        RegexOptions.CultureInvariant);
    var lines = await File.ReadAllLinesAsync(log);
    foreach (var line in lines.Where(line => errorPattern.IsMatch(line)).TakeLast(40))
        Console.Error.WriteLine(line);
}

// Sums the `Passed: N` tallies from the run summary. Returns 0 when nothing executed.
static int CountExecutedTests(IReadOnlyList<string> logLines)
{
    var pattern = new Regex(@"\bPassed:\s*(\d+)", RegexOptions.CultureInvariant);
    var total = 0;
    foreach (var line in logLines)
    {
        var match = pattern.Match(line);
        if (match.Success)
            total += int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    return total;
}

// Anchored to this file rather than the working directory so the gate behaves the
// same no matter where a caller invokes it from.
static string ResolveFrameworkRoot() =>
    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile())!, ".."));

static string ThisFile([CallerFilePath] string path = "") => path;

sealed class StepFailedException(string step, int exitCode, string logPath)
    : Exception($"Step '{step}' exited with code {exitCode}.")
{
    public int ExitCode { get; } = exitCode;
    public string LogPath { get; } = logPath;
}
