using HPD.Agent.TypeScriptGenerator;

namespace HPD.Agent.ClientGenerator.Tests;

public sealed class TypeScriptSdkGeneratorTests
{
    [Fact]
    public async Task GeneratedTypeScriptSdk_ContainsGeneratedContractAndRuntime()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"hpd-typescript-generator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var contractPath = Path.Combine(root, "contract.json");
            await File.WriteAllTextAsync(contractPath, CSharpSdkGeneratorTests.Contract);
            var output = Path.Combine(root, "typescript");
            var manifest = await new TypeScriptSdkGenerator().GenerateAsync(
                new TypeScriptGeneratorOptions(
                    contractPath,
                    output,
                    "@example/generated-hpd"));

            Assert.Equal("1.0", manifest.IrVersion);
            Assert.Equal(8, manifest.OperationCount);
            Assert.True(File.Exists(Path.Combine(output, "package.json")));
            Assert.True(File.Exists(Path.Combine(output, "tsconfig.json")));
            Assert.True(File.Exists(Path.Combine(output, "src", "runtime.ts")));
            Assert.True(File.Exists(Path.Combine(output, "src", "generated", "models.ts")));
            Assert.True(File.Exists(Path.Combine(output, "src", "generated", "events.ts")));
            Assert.True(File.Exists(Path.Combine(output, "src", "generated", "services.ts")));
            Assert.True(File.Exists(Path.Combine(output, "conformance.mjs")));
            Assert.True(File.Exists(Path.Combine(output, "sdk-ir.json")));
            var services = await File.ReadAllTextAsync(
                Path.Combine(output, "src", "generated", "services.ts"));
            Assert.Contains("export class AgentClient", services, StringComparison.Ordinal);
            Assert.Contains("HpdTransport", services, StringComparison.Ordinal);
            var runtime = await File.ReadAllTextAsync(
                Path.Combine(output, "src", "runtime.ts"));
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
