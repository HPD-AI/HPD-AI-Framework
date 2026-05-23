using BenchmarkDotNet.Running;
using Rhodium.Benchmarks;

if (args.Contains("--vector-smoke", StringComparer.OrdinalIgnoreCase))
    return VectorSimulationSmokeGate.Run(args);

if (args.Contains("--replay-certification-smoke", StringComparer.OrdinalIgnoreCase))
    return await ReplayCertificationSmokeGate.RunAsync(args);

BenchmarkSwitcher.FromAssembly(typeof(DispatchBenchmarks).Assembly).Run(args);
return 0;
