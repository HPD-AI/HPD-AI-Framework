using HPD.Agent.GoGenerator;

namespace HPD.Agent.ClientGenerator.Tests;

public sealed class GoSdkGeneratorTests
{
    [Fact]
    public async Task GeneratedGoSdk_ContainsModelsRuntimeServicesAndConformance()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"hpd-go-generator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var contractPath = Path.Combine(root, "contract.json");
            await File.WriteAllTextAsync(contractPath, CSharpSdkGeneratorTests.Contract);
            var output = Path.Combine(root, "go");
            var manifest = await new GoSdkGenerator().GenerateAsync(
                new GoGeneratorOptions(
                    contractPath,
                    output,
                    "example.com/generated-hpd",
                    "hpdagent"));

            Assert.Equal("1.0", manifest.IrVersion);
            Assert.Equal(8, manifest.OperationCount);
            Assert.Equal("example.com/generated-hpd", manifest.ModulePath);
            Assert.True(File.Exists(Path.Combine(output, "go.mod")));
            Assert.True(File.Exists(Path.Combine(output, "models.go")));
            Assert.True(File.Exists(Path.Combine(output, "runtime.go")));
            Assert.True(File.Exists(Path.Combine(output, "sdk.go")));
            Assert.True(File.Exists(Path.Combine(output, "sdk_test.go")));
            Assert.True(File.Exists(Path.Combine(output, "conformance", "main.go")));
            Assert.True(File.Exists(Path.Combine(output, "sdk-ir.json")));
            var models = await File.ReadAllTextAsync(Path.Combine(output, "models.go"));
            Assert.Contains("DecodeHpdAgentEvent", models, StringComparison.Ordinal);
            Assert.Contains("isHpdAgentEvent", models, StringComparison.Ordinal);
            var runtime = await File.ReadAllTextAsync(Path.Combine(output, "runtime.go"));
            Assert.Contains("HpdSseState", runtime, StringComparison.Ordinal);
            Assert.Contains("ClientToolRegistry", runtime, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
