using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using HPD.TUI.Components;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

var iterations = int.TryParse(Environment.GetEnvironmentVariable("BENCHMARK_ITERATIONS"), out var n) ? n : 200;
var warmup = int.TryParse(Environment.GetEnvironmentVariable("BENCHMARK_WARMUP"), out var w) ? w : 30;
var commit = Environment.GetEnvironmentVariable("BASELINE_COMMIT") ?? "unknown";
var results = new List<object>();
foreach (var (width, height) in new[] { (80, 24), (120, 40), (240, 80) })
foreach (var scenario in new[] { "warm-noop", "one-cell", "one-row", "full-screen", "resize" })
{
    var terminal = new CaptureTerminal(width, height);
    using var renderer = new TuiRenderer(terminal);
    var text = new Text("alpha"); renderer.Render(text);
    void Mutate(int i) { if (scenario == "one-cell") text.SetText(i % 2 == 0 ? "alpha" : "Alpha"); else if (scenario == "one-row") text.SetText(new string(i % 2 == 0 ? 'x' : 'X', width)); else if (scenario == "full-screen") text.SetText(((i % 2 == 0 ? new string('x', width) : new string('X', width)) + "\n").Repeat(height)); else if (scenario == "resize") terminal.Resize(i % 2 == 0 ? width : Math.Max(40, width - 1), i % 2 == 0 ? height : Math.Max(12, height - 1)); }
    for (var i = 0; i < warmup; i++) { Mutate(i); renderer.Render(text); }
    var samples = new long[iterations]; long outputBytes = 0; var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < iterations; i++) { Mutate(i); terminal.Reset(); var start = Stopwatch.GetTimestamp(); renderer.Render(text); samples[i] = (long)Stopwatch.GetElapsedTime(start).TotalNanoseconds; outputBytes += terminal.Bytes; }
    Array.Sort(samples);
    results.Add(new { adapter = "hpd-tui-baseline", scenario, width, height, setupNs = 0, meanNs = samples.Average(), medianNs = samples[iterations / 2], p95Ns = samples[Math.Min(iterations - 1, (int)Math.Ceiling(iterations * .95) - 1)], allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before, outputBytes, cellsCompared = 0, rowsRasterized = (long)height * iterations, displayCommandsBuilt = 0, displayCommandsReused = 0, sink = "memory" });
}
var json = JsonSerializer.Serialize(new { schema = "hpd.tui.framework-comparison.v1", adapter = "hpd-tui-baseline", commit, environment = new { architecture = RuntimeInformation.ProcessArchitecture.ToString(), os = RuntimeInformation.OSDescription, runtime = RuntimeInformation.FrameworkDescription, warmupCount = warmup, iterationCount = iterations, corpusSeed = 0x485044 }, results }, new JsonSerializerOptions { WriteIndented = true });
var output = Environment.GetEnvironmentVariable("BENCHMARK_OUTPUT");
if (string.IsNullOrWhiteSpace(output)) Console.WriteLine(json); else File.WriteAllText(output, json);

sealed class CaptureTerminal(int width, int height) : ITerminal
{
    private int _width = width, _height = height; public long Bytes { get; private set; }
    public TerminalSize GetSize() => new(_width, _height); public void Resize(int w, int h) { _width = w; _height = h; } public void Reset() => Bytes = 0;
    public void Write(ReadOnlySpan<char> text) => Bytes += Encoding.UTF8.GetByteCount(text); public void Flush() { } public void HideCursor() { } public void ShowCursor() { }
    public bool TryReadKey(out HPD.TUI.Core.KeyEvent key) { key = default; return false; }
    public void Dispose() { }
}

static class StringExtensions { public static string Repeat(this string value, int count) => string.Concat(Enumerable.Repeat(value, count)); }
