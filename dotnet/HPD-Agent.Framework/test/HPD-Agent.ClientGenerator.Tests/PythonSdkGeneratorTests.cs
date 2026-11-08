using System.Diagnostics;
using HPD.Agent.PythonGenerator;

namespace HPD.Agent.ClientGenerator.Tests;

public sealed class PythonSdkGeneratorTests
{
    [Fact]
    public async Task GeneratedPythonSdk_CompilesImportsAndPassesConformance()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"hpd-python-generator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var contractPath = Path.Combine(root, "contract.json");
            await File.WriteAllTextAsync(contractPath, CSharpSdkGeneratorTests.Contract);
            var output = Path.Combine(root, "python");
            var manifest = await new PythonSdkGenerator().GenerateAsync(
                new PythonGeneratorOptions(contractPath, output, "generated_hpd"));

            Assert.Equal("1.0", manifest.IrVersion);
            Assert.Equal(8, manifest.OperationCount);
            Assert.True(File.Exists(Path.Combine(output, "sdk-ir.json")));
            Assert.True(File.Exists(Path.Combine(output, "generated_hpd", "models.py")));
            Assert.True(File.Exists(Path.Combine(output, "generated_hpd", "client.py")));

            var compile = await RunAsync(
                "python3",
                "-m",
                "compileall",
                "-q",
                Path.Combine(output, "generated_hpd"),
                Path.Combine(output, "conformance_runner.py"));
            Assert.True(compile.ExitCode == 0, compile.Output);

            var environment = new Dictionary<string, string>
            {
                ["PYTHONPATH"] = output
            };
            var conformance = await RunAsync(
                "python3",
                [
                    Path.Combine(output, "conformance_runner.py"),
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Conformance",
                        "fixtures",
                        "conformance-v1.json")
                ],
                environment);
            Assert.True(conformance.ExitCode == 0, conformance.Output);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<ProcessResult> RunAsync(
        string fileName,
        params string[] arguments) =>
        await RunAsync(fileName, arguments, environment: null);

    private static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        if (environment is not null)
        {
            foreach (var variable in environment)
                process.StartInfo.Environment[variable.Key] = variable.Value;
        }
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        Assert.True(process.Start());
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            await standardOutput + await standardError);
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
