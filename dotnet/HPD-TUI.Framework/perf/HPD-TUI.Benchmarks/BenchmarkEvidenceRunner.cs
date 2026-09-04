using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using HPD.Events;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Observability;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Benchmarks;

/// <summary>Produces repeatable per-operation samples in the framework-neutral comparison schema.</summary>
internal static class BenchmarkEvidenceRunner
{
    private const int Seed = 0x485044;

    public static async Task RunAsync(string[] args)
    {
        var iterations = ReadInt(args, "--iterations", 200);
        var warmups = ReadInt(args, "--warmup", 30);
        var output = ReadString(args, "--output") ?? Path.Combine("BenchmarkDotNet.Artifacts", "hpd-evidence.json");
        var cases = new List<EvidenceResult>();
        foreach (var dimensions in new[] { (80, 24), (120, 40), (240, 80) })
        {
            foreach (var scenario in CoreScenarios(dimensions.Item1, dimensions.Item2))
                cases.Add(Measure(scenario, dimensions.Item1, dimensions.Item2, warmups, iterations));
        }

        foreach (var count in new[] { 10, 100, 1_000 })
            foreach (var scenario in ComponentScenarios(count))
                cases.Add(Measure(scenario, 120, 40, warmups, iterations));

        cases.AddRange(await TransportScenariosAsync(warmups, iterations));
        if (PtyTransport.TryCreate(out var pty))
        {
            using (pty)
            using (var state = new Scene(120, 40, transport: pty))
            {
                var samples = new long[iterations];
                for (var i = 0; i < warmups; i++) { state.ToggleFirst(i); state.Render(); }
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < iterations; i++) { state.ToggleFirst(i); pty.Reset(); var start = Stopwatch.GetTimestamp(); state.Render(); samples[i] = Ns(start); }
                cases.Add(Summarize("hpd-tui", "transport-pty", 120, 40, 0, samples,
                    GC.GetAllocatedBytesForCurrentThread() - allocated, pty.TotalBytes, 0, 0, 0, 0, "pty"));
            }
        }
        cases.AddRange(CellRepresentationExperiment.Run(warmups, iterations));

        var payload = new EvidenceDocument(
            Schema: "hpd.tui.framework-comparison.v1",
            Adapter: "hpd-tui",
            Commit: Git("rev-parse", "HEAD"),
            Environment: new(RuntimeInformation.ProcessArchitecture.ToString(), RuntimeInformation.OSDescription,
                RuntimeInformation.FrameworkDescription, Environment.Version.ToString(), GCSettings(),
                Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "runtime-default",
                Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") ?? "runtime-default", warmups, iterations, Seed),
            Results: cases);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(payload, JsonOptions));
        Console.WriteLine(Path.GetFullPath(output));
    }

    private static IEnumerable<Scenario> CoreScenarios(int width, int height)
    {
        yield return Scenario.Create("cold-start", () => new Scene(width, height), static (s, i) => s.ToggleFirst(i));
        yield return Scenario.Create("warm-noop", () => new Scene(width, height), static (_, _) => { });
        yield return Scenario.Create("one-cell", () => new Scene(width, height), static (s, i) => s.ToggleFirst(i));
        yield return Scenario.Create("one-row", () => new Scene(width, height), static (s, i) => s.ToggleRow(i));
        yield return Scenario.Create("two-disjoint-rows", () => new Scene(width, height), static (s, i) => s.ToggleDistant(i));
        yield return Scenario.Create("full-screen", () => new Scene(width, height), static (s, i) => s.ToggleFull(i));
        yield return Scenario.Create("cursor-only", () => new Scene(width, height), static (s, i) => s.ToggleCursor(i));
        yield return Scenario.Create("style-only", () => new Scene(width, height), static (s, i) => s.ToggleStyle(i));
        yield return Scenario.Create("wide-grapheme", () => new Scene(width, height), static (s, i) => s.ToggleWide(i));
        yield return Scenario.Create("hyperlink", () => new Scene(width, height), static (s, i) => s.ToggleLink(i));
        yield return Scenario.Create("resize", () => new Scene(width, height), static (s, i) => s.Resize(i % 2 == 0 ? 80 : 120, i % 2 == 0 ? 24 : 40));
    }

    private static IEnumerable<Scenario> ComponentScenarios(int count)
    {
        yield return Scenario.Create($"component-stack-{count}-stable", () => new Scene(120, 40, count), static (_, _) => { });
        yield return Scenario.Create($"component-stack-{count}-paint", () => new Scene(120, 40, count), static (s, i) => s.ToggleStyle(i));
        yield return Scenario.Create($"component-stack-{count}-layout", () => new Scene(120, 40, count), static (s, i) => s.ToggleRow(i));
        yield return Scenario.Create($"component-stack-{count}-visibility", () => new Scene(120, 40, count), static (s, i) => s.ToggleVisibility(i));
    }

    private static EvidenceResult Measure(Scenario scenario, int width, int height, int warmups, int iterations)
    {
        var setupStart = Stopwatch.GetTimestamp();
        using var state = scenario.Factory();
        state.Render();
        var setupNs = Ns(setupStart);
        for (var i = 0; i < warmups; i++) { scenario.Mutate(state, i); state.Render(); }
        var samples = new long[iterations];
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        long bytes = 0, cells = 0, rows = 0, built = 0, reused = 0;
        for (var i = 0; i < iterations; i++)
        {
            scenario.Mutate(state, i);
            state.Transport.Reset();
            var start = Stopwatch.GetTimestamp();
            state.Render();
            samples[i] = Ns(start);
            bytes += state.Transport.Bytes;
        }
        using (var metricState = scenario.Factory())
        {
            metricState.EnableDiagnostics(); metricState.Render();
            for (var i = 0; i < iterations; i++)
            {
                scenario.Mutate(metricState, i); metricState.Render(); var d = metricState.Sink.Last;
                cells += d?.CellsChanged ?? 0; rows += d?.RowsDamaged ?? 0;
                built += d?.DisplayCommandsBuilt ?? 0; reused += d?.DisplayCommandsReused ?? 0;
            }
        }
        return Summarize("hpd-tui", scenario.Name, width, height, setupNs, samples,
            GC.GetAllocatedBytesForCurrentThread() - allocated, bytes, cells, rows, built, reused, "memory");
    }

    private static async Task<IEnumerable<EvidenceResult>> TransportScenariosAsync(int warmups, int iterations)
    {
        var results = new List<EvidenceResult>();
        foreach (var mode in new[] { TransportMode.Synchronous, TransportMode.Delayed, TransportMode.Backpressured, TransportMode.FailRecover })
        {
            var samples = new long[iterations]; long bytes = 0;
            using var transport = new ExercisedTransport(mode);
            using var state = new Scene(120, 40, transport: transport);
            for (var i = 0; i < warmups; i++) { state.ToggleFirst(i); await RenderWithRecovery(state, transport); }
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < iterations; i++)
            {
                state.ToggleFirst(i); transport.Arm(i); var start = Stopwatch.GetTimestamp();
                await RenderWithRecovery(state, transport); samples[i] = Ns(start); bytes += transport.Bytes;
            }
            results.Add(Summarize("hpd-tui", $"transport-{mode.ToString().ToLowerInvariant()}", 120, 40, 0,
                samples, GC.GetAllocatedBytesForCurrentThread() - allocated, bytes, 0, 0, 0, 0, transport.SinkKind));
        }
        return results;
    }

    private static async ValueTask RenderWithRecovery(Scene state, ExercisedTransport transport)
    {
        try { state.Render(); }
        catch (Exception) when (transport.Mode == TransportMode.Backpressured) { await transport.WaitUntilWritableAsync(); state.Render(); }
        catch (InvalidOperationException) when (transport.Mode == TransportMode.FailRecover) { state.Render(); }
    }

    private static EvidenceResult Summarize(string adapter, string scenario, int width, int height, long setupNs,
        long[] samples, long allocated, long bytes, long cells, long rows, long built, long reused, string sink)
    {
        Array.Sort(samples);
        return new(adapter, scenario, width, height, setupNs, samples.Average(), samples[samples.Length / 2],
            samples[Math.Min(samples.Length - 1, (int)Math.Ceiling(samples.Length * .95) - 1)], allocated,
            bytes, cells, rows, built, reused, sink);
    }

    private static long Ns(long start) => (long)(Stopwatch.GetElapsedTime(start).TotalNanoseconds);
    private static int ReadInt(string[] args, string name, int fallback) => int.TryParse(ReadString(args, name), out var value) ? value : fallback;
    private static string? ReadString(string[] args, string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    private static string Git(params string[] args) { try { return Process.Start(new ProcessStartInfo("git", args) { RedirectStandardOutput = true })!.StandardOutput.ReadToEnd().Trim(); } catch { return "unknown"; } }
    private static string GCSettings() => System.Runtime.GCSettings.IsServerGC ? "server" : "workstation";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record Scenario(string Name, Func<Scene> Factory, Action<Scene, int> Mutate)
    { public static Scenario Create(string name, Func<Scene> factory, Action<Scene, int> mutate) => new(name, factory, mutate); }

    private sealed class Scene : IDisposable
    {
        private readonly Stack _root = new(); private readonly Text _first; private readonly Text _last; private readonly Text _wide;
        private readonly SceneSurface? _surface; private readonly IComponent _renderRoot;
        private readonly List<Text> _rows = []; private readonly TuiRenderer _renderer; private bool _visible = true;
        public CountingTransport Transport { get; } public CaptureSink Sink { get; } = new();
        public Scene(int width, int height, int components = 2, CountingTransport? transport = null)
        {
            Transport = transport ?? new CountingTransport(); Terminal = new MutableTerminal(width, height);
            for (var i = 0; i < Math.Max(2, components); i++) { var row = new Text($"row-{i:D4}"); _rows.Add(row); _root.Add(row); }
            _first = _rows[0]; _last = _rows[^1]; _wide = new Text("界"); _root.Add(_wide);
            _renderRoot = components == 2 ? _surface = new SceneSurface(width, height) : _root;
            _renderer = new TuiRenderer(Terminal, Transport);
        }
        public MutableTerminal Terminal { get; }
        public void Render() => _renderer.Render(_renderRoot);
        public void EnableDiagnostics() => _renderer.PerformanceSink = Sink;
        public void ToggleFirst(int i) { if (_surface is not null) _surface.Mutate(SceneMutation.OneCell, i); else _first.SetText(i % 2 == 0 ? "alpha" : "Alpha"); }
        public void ToggleRow(int i) { if (_surface is not null) _surface.Mutate(SceneMutation.OneRow, i); else _first.SetText(i % 2 == 0 ? new string('r', 100) : new string('R', 100)); }
        public void ToggleDistant(int i) { if (_surface is not null) _surface.Mutate(SceneMutation.Distant, i); else { ToggleFirst(i); _last.SetText(i % 2 == 0 ? "omega" : "Omega"); } }
        public void ToggleFull(int i) { if (_surface is not null) _surface.Mutate(SceneMutation.Full, i); else foreach (var row in _rows) row.SetText(new string(i % 2 == 0 ? 'x' : 'X', Math.Max(1, Terminal.Width))); }
        public void ToggleCursor(int i) => (_surface ?? throw new InvalidOperationException()).Mutate(SceneMutation.Cursor, i);
        public void ToggleStyle(int i) { if (_surface is not null) _surface.Mutate(SceneMutation.Style, i); else _first.SetStyle(i % 2 == 0 ? Theme.Default.Accent : Style.Default); }
        public void ToggleWide(int i) => (_surface ?? throw new InvalidOperationException()).Mutate(SceneMutation.Wide, i);
        public void ToggleLink(int i) => (_surface ?? throw new InvalidOperationException()).Mutate(SceneMutation.Link, i);
        public void ToggleVisibility(int i) { _visible = !_visible; _last.SetText(_visible ? "visible" : string.Empty); }
        public void Resize(int width, int height) => Terminal.Resize(width, height);
        public void Dispose() => _renderer.Dispose();
    }

    private enum SceneMutation { OneCell, OneRow, Distant, Full, Cursor, Style, Wide, Link }
    private sealed class SceneSurface(int width, int height) : Component
    {
        private SceneMutation _mutation; private int _version;
        private readonly TerminalHyperlink _linkA = CreateLink("https://a.example");
        private readonly TerminalHyperlink _linkB = CreateLink("https://b.example");
        public override ComponentDependencies Dependencies => new(RenderContextFields.None, RenderContextFields.None);
        public void Mutate(SceneMutation mutation, int version) { _mutation = mutation; _version = version; InvalidatePaint(); }
        public override Measurement Measure(in RenderContext context, LayoutConstraints constraints) => new(width, width, height);
        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            var upper = (_version & 1) != 0;
            if (_mutation == SceneMutation.Full)
            {
                for (var y = 0; y < height; y++) { output.MoveTo(0, y); output.WriteRepeated(upper ? 'X' : 'x', width, context.Theme.Text); }
                return;
            }
            output.MoveTo(0, 0);
            if (_mutation == SceneMutation.OneRow) output.Write(new string(upper ? 'R' : 'r', width), context.Theme.Text);
            else if (_mutation == SceneMutation.Wide) output.Write(upper ? "語" : "界", context.Theme.Text);
            else if (_mutation == SceneMutation.Link) output.Write("link", context.Theme.Text, new TerminalRunMetadata(upper ? _linkB : _linkA));
            else output.Write(upper ? "Alpha" : "alpha", _mutation == SceneMutation.Style && upper ? context.Theme.Accent : context.Theme.Text);
            if (_mutation == SceneMutation.Distant) { output.MoveTo(0, height - 1); output.Write(upper ? "Omega" : "omega", context.Theme.Text); }
            if (_mutation == SceneMutation.Cursor) output.SetTerminalCursor(upper ? 1 : 0, 0);
        }
        private static TerminalHyperlink CreateLink(string destination) { TerminalHyperlinkPolicy.TryCreate(destination, out var link); return link!; }
    }

    private sealed class CaptureSink : IHpdTuiPerformanceEventSink { public TuiFrameDiagnostics? Last { get; private set; } public void Publish(Event evt) { if (evt is TuiFrameDiagnostics d) Last = d; } }
    private sealed class MutableTerminal(int width, int height) : ITerminal, ITerminalInput
    { public int Width { get; private set; } = width; public int Height { get; private set; } = height; public ITerminalInput Input => this; public TerminalSize GetSize() => new(Width, Height); public void Resize(int w, int h) { Width = w; Height = h; } public void Write(ReadOnlySpan<char> text) { } public void Flush() { } public void HideCursor() { } public void ShowCursor() { } public ValueTask<TerminalInputEvent> ReadAsync(CancellationToken c = default) => ValueTask.FromResult(TerminalInputEvent.Stop); public void Dispose() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }

    private class CountingTransport : ITerminalOutputTransport, IDisposable
    { public long Bytes { get; protected set; } public long TotalBytes { get; protected set; } public virtual string SinkKind => "memory"; public void Reset() => Bytes = 0; public virtual ValueTask<TerminalWriteResult> TryWriteFrameAsync(TerminalFrameLease frame, CancellationToken cancellationToken = default) { var count = System.Text.Encoding.UTF8.GetByteCount(frame.Payload.Span); Bytes += count; TotalBytes += count; return ValueTask.FromResult(TerminalWriteResult.Written); } public virtual ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask; public virtual void Dispose() { } }
    private enum TransportMode { Synchronous, Delayed, Backpressured, FailRecover }
    private sealed class ExercisedTransport(TransportMode mode) : CountingTransport
    { private bool _armed; public TransportMode Mode => mode; public override string SinkKind => "memory"; public void Arm(int i) => _armed = i % 8 == 0; public override async ValueTask<TerminalWriteResult> TryWriteFrameAsync(TerminalFrameLease frame, CancellationToken token = default) { if (mode == TransportMode.Delayed) await Task.Yield(); if (_armed && mode == TransportMode.Backpressured) { _armed = false; return TerminalWriteResult.Backpressured; } if (_armed && mode == TransportMode.FailRecover) { _armed = false; return new(TerminalWriteStatus.Failed, new IOException("injected")); } return await base.TryWriteFrameAsync(frame, token); } }

    private sealed class PtyTransport : CountingTransport
    {
        private int _master = -1, _slave = -1;
        private Thread? _drain;
        public override string SinkKind => "pty";
        public static bool TryCreate(out PtyTransport transport)
        {
            transport = new PtyTransport();
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()) return false;
            try
            {
                if (openpty(out transport._master, out transport._slave, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != 0) return false;
                transport._drain = new Thread(transport.Drain) { IsBackground = true, Name = "hpd-benchmark-pty-drain" };
                transport._drain.Start();
                return true;
            }
            catch (DllNotFoundException) { return false; }
        }
        public override ValueTask<TerminalWriteResult> TryWriteFrameAsync(TerminalFrameLease frame, CancellationToken token = default)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(frame.Payload.ToString());
            var written = write(_slave, bytes, (nuint)bytes.Length);
            if (written < 0) return ValueTask.FromResult(new TerminalWriteResult(TerminalWriteStatus.Failed, new IOException("PTY write failed")));
            Bytes += written; TotalBytes += written;
            return ValueTask.FromResult(TerminalWriteResult.Written);
        }
        private void Drain() { var buffer = new byte[4096]; while (_master >= 0 && read(_master, buffer, (nuint)buffer.Length) > 0) { } }
        public override void Dispose() { var master = _master; var slave = _slave; _master = _slave = -1; if (slave >= 0) close(slave); if (master >= 0) close(master); _drain?.Join(500); }
        [DllImport("libutil", EntryPoint = "openpty", SetLastError = true)] private static extern int openpty(out int master, out int slave, IntPtr name, IntPtr termp, IntPtr winp);
        [DllImport("libc", SetLastError = true)] private static extern int write(int fd, byte[] buffer, nuint count);
        [DllImport("libc", SetLastError = true)] private static extern int read(int fd, byte[] buffer, nuint count);
        [DllImport("libc")] private static extern int close(int fd);
    }

    private sealed record EvidenceDocument(string Schema, string Adapter, string Commit, EvidenceEnvironment Environment, IReadOnlyList<EvidenceResult> Results);
    private sealed record EvidenceEnvironment(string Architecture, string Os, string Runtime, string RuntimeVersion, string GcMode, string TieredPgo, string ReadyToRun, int WarmupCount, int IterationCount, int CorpusSeed);
    internal sealed record EvidenceResult(string Adapter, string Scenario, int Width, int Height, long SetupNs, double MeanNs, long MedianNs, long P95Ns, long AllocatedBytes, long OutputBytes, long CellsCompared, long RowsRasterized, long DisplayCommandsBuilt, long DisplayCommandsReused, string Sink);
}

internal static class CellRepresentationExperiment
{
    public static IEnumerable<BenchmarkEvidenceRunner.EvidenceResult> Run(int warmups, int iterations)
    {
        const int cells = 240 * 80; var aos = new Cell[cells]; var glyph = new int[cells]; var style = new int[cells];
        for (var i = 0; i < warmups; i++) { ScanAos(aos); ScanSoa(glyph, style); }
        yield return Measure("cell-layout-aos", iterations, () => ScanAos(aos), Marshal.SizeOf<Cell>() * (long)cells);
        yield return Measure("cell-layout-soa", iterations, () => ScanSoa(glyph, style), (sizeof(int) * 2L) * cells);
    }
    private static BenchmarkEvidenceRunner.EvidenceResult Measure(string name, int iterations, Action action, long bytes)
    { var samples = new long[iterations]; for (var i = 0; i < iterations; i++) { var s = Stopwatch.GetTimestamp(); action(); samples[i] = (long)Stopwatch.GetElapsedTime(s).TotalNanoseconds; } Array.Sort(samples); return new("hpd-tui", name, 240, 80, 0, samples.Average(), samples[iterations / 2], samples[Math.Min(iterations - 1, (int)Math.Ceiling(iterations * .95) - 1)], bytes, 0, 19_200L * iterations, 80L * iterations, 0, 0, "memory"); }
    private static long ScanAos(Cell[] cells) { long n = 0; foreach (var c in cells) n += c.Glyph + c.Style; return n; }
    private static long ScanSoa(int[] glyph, int[] style) { long n = 0; for (var i = 0; i < glyph.Length; i++) n += glyph[i] + style[i]; return n; }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Cell(int Glyph, int Style, int Link, byte Width, byte Flags);
}
