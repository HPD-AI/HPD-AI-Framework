using BenchmarkDotNet.Running;
using Helium.Finance.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(OptionPricingBenchmarks).Assembly).Run(args);
