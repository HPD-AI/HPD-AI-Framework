#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

// Architecture guard for the voice-activity lane.
//
// Mirrors the grep-based gate: every rule fails the run when a source line matches.
// Patterns are POSIX-ERE translations; [[:alnum:]_] and [[:space:]] become
// [A-Za-z0-9_] and \s so the .NET engine sees the same language.
var root = ResolveFrameworkRoot();
var sourceRoot = Path.Combine(root, "src");
var voice = Path.Combine(sourceRoot, "HPD-Agent.Audio", "HPD-Agent.Audio", "Abstractions", "VoiceActivity");
var silero = Path.Combine(sourceRoot, "HPD-Agent.Providers.Audio", "HPD-Agent.Providers.Audio.Silero");
var agentProject = Path.Combine(sourceRoot, "HPD-Agent", "HPD-Agent.csproj");

FailIfMatch(
    "legacy-live-contract",
    @"IVoiceActivityDetector|(^|[^A-Za-z0-9_])Vad(Event|Result|State)([^A-Za-z0-9_]|$)|VoiceActivityEvidenceDetail|TurnEvidence.*VoiceActivity",
    sourceRoot);
FailIfMatch(
    "duplicate-authority",
    @"VadProviderRegistry|DetectorRegistry|VadScheduler|VadClock|VoiceActivityProviderRegistry|VoiceActivityScheduler",
    sourceRoot);
FailIfMatch("hidden-work", @"new\s+Thread|Task\.Run|Channel\.Create|ConcurrentQueue", voice, silero);
FailIfMatch("ambient-time", @"DateTime\.Now|DateTime\.UtcNow|Stopwatch\.GetTimestamp", voice, silero);
FailIfMatch("runtime-reflection", @"Assembly\.Load|GetTypes\(|Activator\.CreateInstance", voice, silero);
FailIfMatch(
    "public-media-owner",
    @"public[^\n]*(MemoryOwner|IMemoryOwner|AudioFrameView|OwnedAudioFrame)",
    voice);
FailIfMatch(
    "adjacent-authority-call",
    @"CreateResponse|Interrupt(Output)?|CancelOutput|CommitSemantic|CommitEndpoint|MutateRoute|AppendAgentInput",
    Path.Combine(voice, "VoiceActivityPromotionV1.cs"));

// Exactly one voice-activity file may depend on HPD.Events, and only the diagnostic projection.
var expectedEventsFile = Path.Combine(voice, "VoiceActivityStatusProjectionV1.cs");
var eventsFiles = FindMatchingFiles(@"HPD\.Events", voice);
if (eventsFiles.Count != 1 || !string.Equals(eventsFiles[0], expectedEventsFile, StringComparison.Ordinal))
{
    Fail("hpd-events-authority", eventsFiles.Count == 0 ? "none" : string.Join(Environment.NewLine, eventsFiles));
}

// A single compiler and a single promoter keep the writer set closed.
var compilerCount = FindMatches(@"internal static class VoiceActivityPlanCompilerV1", voice).Count;
var promoterCount = FindMatches(@"internal sealed class VoiceActivityPromoterV1", voice).Count;
if (compilerCount != 1 || promoterCount != 1)
{
    Fail($"writer-count compiler={compilerCount} promoter={promoterCount}", string.Empty);
}

// The agent package must never depend back on the audio lane.
if (File.Exists(agentProject) &&
    Regex.IsMatch(
        File.ReadAllText(agentProject),
        @"<ProjectReference[^>]+HPD-Agent\.Audio",
        RegexOptions.CultureInvariant))
{
    Fail("reverse-audio-dependency", string.Empty);
}

FailIfMatch(
    "compatibility-surface",
    @"namespace\s+.*(LegacyVoiceActivity|VoiceActivityLegacy)|class\s+.*(LegacyVad|VadCompatibility)",
    sourceRoot);

Console.WriteLine(
    $"voice-activity-architecture=pass compiler={compilerCount} promoter={promoterCount} events=diagnostic-only");
return;

static void FailIfMatch(string code, string pattern, params string[] targets)
{
    var matches = FindMatches(pattern, targets);
    if (matches.Count == 0)
        return;

    Console.Error.WriteLine($"voice-activity-architecture={code}");
    foreach (var match in matches)
        Console.Error.WriteLine(match);
    Environment.Exit(1);
}

static void Fail(string code, string detail)
{
    Console.Error.WriteLine($"voice-activity-architecture={code}");
    if (!string.IsNullOrEmpty(detail))
        Console.Error.WriteLine(detail);
    Environment.Exit(1);
}

static List<string> FindMatches(string pattern, params string[] targets)
{
    var regex = new Regex(pattern, RegexOptions.CultureInvariant);
    var matches = new List<string>();
    foreach (var target in targets)
    {
        foreach (var file in EnumerateSourceFiles(target))
        {
            var lines = TryReadAllLines(file);
            if (lines is null)
                continue;

            for (var index = 0; index < lines.Length; index++)
            {
                if (regex.IsMatch(lines[index]))
                    matches.Add($"{file}:{index + 1}:{lines[index]}");
            }
        }
    }

    return matches;
}

static List<string> FindMatchingFiles(string pattern, params string[] targets)
{
    var regex = new Regex(pattern, RegexOptions.CultureInvariant);
    var files = new List<string>();
    foreach (var target in targets)
    {
        foreach (var file in EnumerateSourceFiles(target))
        {
            var lines = TryReadAllLines(file);
            if (lines is null)
                continue;

            if (Array.Exists(lines, line => regex.IsMatch(line)))
                files.Add(file);
        }
    }

    return files;
}

static string[]? TryReadAllLines(string file)
{
    try
    {
        return File.ReadAllLines(file);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        return null;
    }
}

// Mirrors `grep -R --include='*.cs'`: recurses everything, tolerates unreadable directories.
static IEnumerable<string> EnumerateSourceFiles(string target)
{
    if (File.Exists(target))
    {
        if (string.Equals(Path.GetExtension(target), ".cs", StringComparison.OrdinalIgnoreCase))
            yield return target;
        yield break;
    }

    if (!Directory.Exists(target))
        yield break;

    var pending = new Stack<string>();
    pending.Push(target);
    while (pending.Count > 0)
    {
        var current = pending.Pop();
        string[] files;
        string[] directories;
        try
        {
            files = Directory.GetFiles(current, "*.cs");
            directories = Directory.GetDirectories(current);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            continue;
        }

        foreach (var file in files)
            yield return file;
        foreach (var directory in directories)
            pending.Push(directory);
    }
}

// Anchored to this file rather than the working directory so the guard reports the
// same result no matter where a caller invokes it from.
static string ResolveFrameworkRoot() =>
    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile())!, ".."));

static string ThisFile([CallerFilePath] string path = "") => path;
