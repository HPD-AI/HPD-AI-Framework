using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;
using HPD.Agent.ToolHarness.Coding.TUI.Benchmarks;

if (args.Contains("--evidence", StringComparer.Ordinal))
{
    await AgentBenchmarkEvidenceRunner.RunAsync(args);
    return;
}

BenchmarkSwitcher
    .FromAssembly(typeof(CodingCommandStreamingBenchmark).Assembly)
    .Run(args, BenchmarkConfig.Instance);

internal sealed class BenchmarkConfig : ManualConfig
{
    public static readonly IConfig Instance = CreateBenchmarkConfig(DefaultConfig.Instance);

    private static IConfig CreateBenchmarkConfig(IConfig config)
        => ManualConfig.Create(config)
            .AddExporter(MarkdownExporter.GitHub)
            .AddExporter(JsonExporter.FullCompressed);
}
