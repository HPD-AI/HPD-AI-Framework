using HPD.Agent.ClientGenerator;
using HPD.Agent.GoGenerator;
using HPD.Agent.PythonGenerator;
using HPD.Agent.TypeScriptGenerator;

namespace HPD.Agent.ClientGenerator.Tests;

public sealed class AgentPatchContractGeneratorTests
{
    [Fact]
    public async Task EveryGeneratedSdk_UsesTypedConfigPatchAndPatchTransport()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"hpd-agent-patch-generators-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var contractPath = Path.Combine(root, "contract.json");
            await File.WriteAllTextAsync(contractPath, PatchContract);

            var csharpPath = Path.Combine(root, "csharp");
            await new CSharpSdkGenerator().GenerateAsync(
                new GeneratorOptions(
                    contractPath,
                    csharpPath,
                    "HPD.Agent.PatchContract"));
            var csharpModels = await File.ReadAllTextAsync(
                Path.Combine(csharpPath, "generated", "Models.g.cs"));
            var csharpServices = await File.ReadAllTextAsync(
                Path.Combine(csharpPath, "generated", "Services.g.cs"));
            Assert.Contains("AgentConfigUpdate? Config", csharpModels, StringComparison.Ordinal);
            Assert.Contains("HttpMethod.Patch", csharpServices, StringComparison.Ordinal);

            var pythonPath = Path.Combine(root, "python");
            await new PythonSdkGenerator().GenerateAsync(
                new PythonGeneratorOptions(contractPath, pythonPath, "patch_contract"));
            var pythonModels = await File.ReadAllTextAsync(
                Path.Combine(pythonPath, "patch_contract", "models.py"));
            var pythonClient = await File.ReadAllTextAsync(
                Path.Combine(pythonPath, "patch_contract", "client.py"));
            Assert.Contains(
                "config: AgentConfigUpdate | None",
                pythonModels,
                StringComparison.Ordinal);
            Assert.Contains("\"PATCH\"", pythonClient, StringComparison.Ordinal);

            var goPath = Path.Combine(root, "go");
            await new GoSdkGenerator().GenerateAsync(
                new GoGeneratorOptions(
                    contractPath,
                    goPath,
                    "example.com/patch-contract",
                    "patchcontract"));
            var goModels = await File.ReadAllTextAsync(Path.Combine(goPath, "models.go"));
            var goServices = await File.ReadAllTextAsync(Path.Combine(goPath, "sdk.go"));
            Assert.Contains("Config *AgentConfigUpdate", goModels, StringComparison.Ordinal);
            Assert.Contains("\"PATCH\"", goServices, StringComparison.Ordinal);

            var typescriptPath = Path.Combine(root, "typescript");
            await new TypeScriptSdkGenerator().GenerateAsync(
                new TypeScriptGeneratorOptions(
                    contractPath,
                    typescriptPath,
                    "@example/patch-contract"));
            var typescriptModels = await File.ReadAllTextAsync(
                Path.Combine(typescriptPath, "src", "generated", "models.ts"));
            var typescriptServices = await File.ReadAllTextAsync(
                Path.Combine(typescriptPath, "src", "generated", "services.ts"));
            Assert.Contains(
                "config?: AgentConfigUpdate | null",
                typescriptModels,
                StringComparison.Ordinal);
            Assert.Contains("'PATCH'", typescriptServices, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string PatchContract
        => CSharpSdkGeneratorTests.Contract
            .Replace(
                "\"paths\": {",
                "\"paths\": {\n" + PatchPath,
                StringComparison.Ordinal)
            .Replace(
                "\"schemas\": {",
                "\"schemas\": {\n" + PatchSchemas,
                StringComparison.Ordinal);

    private const string PatchPath =
        """
            "/agents/{agentId}": {
              "patch": {
                "operationId": "UpdateAgent",
                "tags": ["Agents"],
                "parameters": [
                  {
                    "name": "agentId",
                    "in": "path",
                    "required": true,
                    "schema": { "type": "string" }
                  }
                ],
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": {
                      "schema": { "$ref": "#/components/schemas/UpdateAgentRequest" }
                    }
                  }
                },
                "responses": {
                  "200": {
                    "description": "Updated agent",
                    "content": {
                      "application/json": {
                        "schema": { "$ref": "#/components/schemas/SessionDto" }
                      }
                    }
                  }
                }
              }
            },
        """;

    private const string PatchSchemas =
        """
            "AgentConfigUpdate": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "name": { "type": "string" },
                "maxAgenticIterations": { "type": "integer" },
                "toolHarnesses": {
                  "type": "array",
                  "items": { "type": "string" }
                },
                "validation": { "$ref": "#/components/schemas/ValidationConfig" },
                "clear": {
                  "type": "array",
                  "items": { "type": "string" }
                }
              }
            },
            "ValidationConfig": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "timeoutMs": { "type": "integer" }
              }
            },
            "UpdateAgentRequest": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "config": { "$ref": "#/components/schemas/AgentConfigUpdate" },
                "metadata": { "type": "object" },
                "clearMetadata": { "type": "boolean" }
              }
            },
        """;
}
