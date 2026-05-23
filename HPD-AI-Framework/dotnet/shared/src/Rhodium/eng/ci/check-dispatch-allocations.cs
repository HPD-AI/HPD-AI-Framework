#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false

using System.Diagnostics;
using System.Globalization;
using System.Text;

const string ProjectPath = "HPD-AI-Framework/dotnet/shared/src/Rhodium/benchmarks/Rhodium.Benchmarks/Rhodium.Benchmarks.csproj";
const string LogPath = "dispatch-benchmark-results.log";
const string CsvPath = "BenchmarkDotNet.Artifacts/results/Rhodium.Benchmarks.DispatchBenchmarks-report.csv";

var exitCode = await DotnetAsync(
    [
        "run",
        "-c",
        "Release",
        "-f",
        "net10.0",
        "--project",
        ProjectPath,
        "--",
        "--filter",
        "*HundredStrategiesParallel*",
        "--job",
        "short",
        "--warmupCount",
        "3",
        "--iterationCount",
        "5"
    ],
    LogPath);

if (exitCode != 0)
    return exitCode;

var result = ReadBenchmarkResult(CsvPath, "HundredStrategiesParallel");
Console.WriteLine($"HundredStrategiesParallel mean: {result.MeanMicroseconds:N3} us");
Console.WriteLine($"HundredStrategiesParallel allocated: {result.Allocated}");

if (result.MeanMicroseconds > 60)
{
    Console.Error.WriteLine($"Dispatch latency gate failed: {result.MeanMicroseconds:N3} us > 60 us.");
    return 1;
}

if (result.Allocated != "0 B" && result.Allocated != "-")
{
    Console.Error.WriteLine($"Dispatch allocation gate failed: expected 0 B, got {result.Allocated}.");
    return 1;
}

return 0;

static BenchmarkResult ReadBenchmarkResult(string csvPath, string method)
{
    var lines = File.ReadAllLines(csvPath);
    if (lines.Length < 2)
        throw new InvalidOperationException($"Benchmark CSV '{csvPath}' is empty.");

    var header = SplitCsv(lines[0]);
    var methodIndex = Array.IndexOf(header, "Method");
    var meanIndex = Array.IndexOf(header, "Mean");
    var allocatedIndex = Array.IndexOf(header, "Allocated");
    if (methodIndex < 0 || meanIndex < 0 || allocatedIndex < 0)
        throw new InvalidOperationException("Benchmark CSV is missing Method, Mean, or Allocated columns.");

    foreach (var line in lines.Skip(1))
    {
        var values = SplitCsv(line);
        if (values.Length <= Math.Max(methodIndex, Math.Max(meanIndex, allocatedIndex)))
            continue;

        if (!string.Equals(values[methodIndex], method, StringComparison.Ordinal))
            continue;

        return new BenchmarkResult(ParseMicroseconds(values[meanIndex]), values[allocatedIndex]);
    }

    throw new InvalidOperationException($"Benchmark method '{method}' was not found in '{csvPath}'.");
}

static decimal ParseMicroseconds(string value)
{
    var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length != 2)
        throw new InvalidOperationException($"Cannot parse benchmark mean '{value}'.");

    var number = decimal.Parse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture);
    return parts[1] switch
    {
        "ns" => number / 1_000m,
        "μs" => number,
        "us" => number,
        "ms" => number * 1_000m,
        _ => throw new InvalidOperationException($"Unsupported benchmark time unit '{parts[1]}'.")
    };
}

static string[] SplitCsv(string line)
    => line.Split(',');

static async Task<int> DotnetAsync(IReadOnlyList<string> arguments, string logPath)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };

    foreach (var argument in arguments)
        startInfo.ArgumentList.Add(argument);

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start dotnet process.");

    var standardOutput = process.StandardOutput.ReadToEndAsync();
    var standardError = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    var output = await standardOutput;
    var error = await standardError;
    var combined = new StringBuilder(output.Length + error.Length + 1)
        .Append(output)
        .Append(error)
        .ToString();

    await File.WriteAllTextAsync(logPath, combined, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    foreach (var line in ReadLines(combined))
    {
        if (line.Contains("warning", StringComparison.OrdinalIgnoreCase))
            continue;

        Console.WriteLine(line);
    }

    return process.ExitCode;
}

static IEnumerable<string> ReadLines(string text)
{
    using var reader = new StringReader(text);
    while (reader.ReadLine() is { } line)
        yield return line;
}

readonly record struct BenchmarkResult(decimal MeanMicroseconds, string Allocated);
