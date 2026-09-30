#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false

using System.Runtime.CompilerServices;
using System.Security.Cryptography;

// Pinned Silero VAD v6.2 qualification inputs.
//
// Contract: stdout is exactly the model path, so callers can capture it
// (`model="$(dotnet run --file eng/fetch-silero-vad-v6.2.cs)"`). All progress and
// diagnostics go to stderr to keep that capture clean.
const string Commit = "be95df9152c0d7618fa1edfeb296fc3dae32376f";
const string ModelExpectedSha256 = "1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3";
const string CorpusExpectedSha256 = "89f17d9c94c4b31eb320f424628bcbc920abaddbee6e2760fd868bfb1d9a2e47";

var directory = Path.Combine(ResolveFrameworkRoot(), "artifacts", "silero-vad", "v6.2");
var modelPath = Path.Combine(directory, "silero_vad.onnx");
var corpusPath = Path.Combine(directory, "test.wav");

Directory.CreateDirectory(directory);

using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HPD-SileroVadFetch/1.0");

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

Console.WriteLine(modelPath);

static async Task EnsureAsync(
    HttpClient httpClient,
    string expectedSha256,
    string destination,
    string url)
{
    if (File.Exists(destination) && await MatchesAsync(destination, expectedSha256))
        return;

    var fileName = Path.GetFileName(destination);
    Console.Error.WriteLine($"silero-fetch: downloading {fileName}");

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

// Anchored to this file rather than the working directory so the app behaves the
// same no matter where a caller invokes it from.
static string ResolveFrameworkRoot() =>
    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile())!, ".."));

static string ThisFile([CallerFilePath] string path = "") => path;
