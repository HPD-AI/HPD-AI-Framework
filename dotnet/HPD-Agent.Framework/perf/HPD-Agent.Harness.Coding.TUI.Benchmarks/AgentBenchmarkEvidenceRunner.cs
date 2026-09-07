using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using HPD.Agent.TUI.Application;

namespace HPD.Agent.ToolHarness.Coding.TUI.Benchmarks;

/// <summary>Emits the agent-level proposal scenarios in the compositor comparison schema.</summary>
internal static class AgentBenchmarkEvidenceRunner
{
    private const int Seed = 0x485044;

    public static async Task RunAsync(string[] args)
    {
        var iterations = ReadInt(args, "--iterations", 20);
        var warmups = ReadInt(args, "--warmup", 5);
        var output = ReadString(args, "--output") ?? Path.Combine("BenchmarkDotNet.Artifacts", "hpd-agent-evidence.json");
        var history = CodingBenchmarkScenarios.CreateState();
        await CodingBenchmarkScenarios.PopulateCommandAsync(history, 1_000);
        var live = CodingBenchmarkScenarios.CreateState();
        await CodingBenchmarkScenarios.PopulateCommandAsync(live, 100);
        _ = CodingBenchmarkScenarios.RenderTranscript(history, 100, 18);
        _ = CodingBenchmarkScenarios.RenderTranscript(live, 100, 18);
        var sequence = 0;

        var results = new List<EvidenceResult>
        {
            await MeasureAsync("transcript-1000-viewport-16", 100, 18, warmups, iterations,
                () => ValueTask.FromResult(CodingBenchmarkScenarios.RenderTranscript(history, 100, 18))),
            await MeasureAsync("transcript-append-final", 100, 18, warmups, iterations, async () =>
            {
                await live.ApplyEventAsync(CodingBenchmarkScenarios.Output($"final-{sequence++}\n"));
                return CodingBenchmarkScenarios.RenderTranscript(live, 100, 18);
            }),
            await MeasureAsync("transcript-live-tail", 100, 18, warmups, iterations, async () =>
            {
                await live.ApplyEventAsync(CodingBenchmarkScenarios.Output($"tail-{sequence++}\n"));
                return CodingBenchmarkScenarios.RenderTranscript(live, 100, 18);
            }),
            await MeasureAsync("transcript-non-visible", 100, 18, warmups, iterations, async () =>
            {
                await history.ApplyEventAsync(CodingBenchmarkScenarios.Output($"offscreen-{sequence++}\n"));
                return CodingBenchmarkScenarios.RenderTranscript(history, 100, 18);
            }),
            await MeasureAsync("status-only", 100, 18, warmups, iterations,
                () => ValueTask.FromResult(CodingBenchmarkScenarios.RenderTranscript(history, 100, 18))),
            await MeasureAsync("streaming-markdown-100kb", 100, 18, warmups, iterations, async () =>
            {
                var state = CodingBenchmarkScenarios.CreateState();
                await CodingBenchmarkScenarios.PopulateCommandAsync(state, 1_000, line: $"**token** {new string('m', 90)}\n");
                return CodingBenchmarkScenarios.RenderTranscript(state, 100, 18);
            }),
            await MeasureAsync("transcript-scroll", 64, 48, warmups, iterations,
                () => ValueTask.FromResult(CodingBenchmarkScenarios.RenderTranscript(history, 64, 48))),
            await MeasureAsync("thread-switch-rehydrate", 100, 18, warmups, iterations,
                () => ValueTask.FromResult(CodingBenchmarkScenarios.RenderTranscript((sequence++ & 1) == 0 ? history : live, 100, 18))),
            await MeasureAsync("normal-terminal-scrollback", 120, 80, warmups, iterations,
                () => ValueTask.FromResult(CodingBenchmarkScenarios.RenderTranscript(history, 120, 80)))
        };

        var document = new EvidenceDocument(
            "hpd.tui.framework-comparison.v1", "hpd-agent-tui", Git("rev-parse", "HEAD"),
            new(RuntimeInformation.ProcessArchitecture.ToString(), CpuDescription(), RuntimeInformation.OSDescription,
                Run("dotnet", "--version"), RuntimeInformation.FrameworkDescription, System.Environment.Version.ToString(),
                System.Runtime.GCSettings.IsServerGC ? "server" : "workstation",
                System.Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "runtime-default",
                System.Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") ?? "runtime-default",
                false, "not-controlled", "not-controlled", warmups, iterations, Seed), results);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(document, JsonOptions));
        Console.WriteLine(Path.GetFullPath(output));
    }

    private static async Task<EvidenceResult> MeasureAsync(string scenario, int width, int height, int warmups, int iterations, Func<ValueTask<string>> operation)
    {
        for (var i = 0; i < warmups; i++) _ = await operation();
        var samples = new long[iterations];
        long outputBytes = 0;
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var gen0 = GC.CollectionCount(0); var gen1 = GC.CollectionCount(1);
        for (var i = 0; i < iterations; i++)
        {
            var start = Stopwatch.GetTimestamp();
            var rendered = await operation();
            samples[i] = (long)Stopwatch.GetElapsedTime(start).TotalNanoseconds;
            outputBytes += Encoding.UTF8.GetByteCount(rendered);
        }
        Array.Sort(samples);
        return new("hpd-agent-tui", scenario, width, height, 0, samples.Average(), samples[iterations / 2],
            samples[Math.Min(iterations - 1, (int)Math.Ceiling(iterations * .95) - 1)],
            GC.GetTotalAllocatedBytes(precise: true) - allocated, GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1,
            outputBytes, 0, 0, 0, 0, "memory");
    }

    private static int ReadInt(string[] args, string name, int fallback) => int.TryParse(ReadString(args, name), out var value) ? value : fallback;
    private static string? ReadString(string[] args, string name) { var index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    private static string Git(params string[] args) => Run("git", args);
    private static string CpuDescription() => OperatingSystem.IsMacOS() ? Run("sysctl", "-n", "machdep.cpu.brand_string") : RuntimeInformation.ProcessArchitecture + " / " + System.Environment.ProcessorCount + " logical processors";
    private static string Run(string file, params string[] args) { try { using var process = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput = true }); return process?.StandardOutput.ReadToEnd().Trim() is { Length: > 0 } value ? value : "unknown"; } catch { return "unknown"; } }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record EvidenceDocument(string Schema, string Adapter, string Commit, EvidenceEnvironment Environment, IReadOnlyList<EvidenceResult> Results);
    private sealed record EvidenceEnvironment(string Architecture, string Cpu, string Os, string DotnetSdk, string Runtime, string RuntimeVersion, string GcMode, string TieredPgo, string ReadyToRun, bool NativeAot, string Affinity, string PowerProfile, int WarmupCount, int IterationCount, int CorpusSeed);
    private sealed record EvidenceResult(string Adapter, string Scenario, int Width, int Height, long SetupNs, double MeanNs, long MedianNs, long P95Ns, long AllocatedBytes, long Gen0Collections, long Gen1Collections, long OutputBytes, long CellsCompared, long RowsRasterized, long DisplayCommandsBuilt, long DisplayCommandsReused, string Sink);
}
