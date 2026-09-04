using BenchmarkDotNet.Attributes;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Rendering;

namespace HPD.TUI.Benchmarks;

/// <summary>Measures retained component-tree scaling and mutation locality.</summary>
[MemoryDiagnoser]
public sealed class ComponentScalingBenchmark
{
    [Params(10, 100, 1000)] public int ComponentCount { get; set; }
    private Stack _root = null!;
    private Text _tail = null!;
    private RenderContext _context;

    [GlobalSetup]
    public void Setup()
    {
        _root = new Stack();
        for (var i = 0; i < ComponentCount; i++)
        {
            _tail = new Text($"row-{i:D4}");
            _root.Add(_tail);
        }
        _context = new RenderContext(120, 40, Theme.Default);
        using var initial = TuiCapture.RenderToGrid(_root, 120, 40);
    }

    [Benchmark(Baseline = true)]
    public int StableTree() => TuiCapture.RenderToString(_root, 120, 40).Length;

    [Benchmark]
    public int TailPaintMutation()
    {
        _tail.SetStyle(_tail.Style == Style.Default ? Theme.Default.Accent : Style.Default);
        return TuiCapture.RenderToString(_root, 120, 40).Length;
    }

    [Benchmark]
    public Measurement LayoutMeasure() => _root.Measure(in _context, LayoutConstraints.Loose(120, 40));
}
