using System.Text.Json;
using HPD.Agent.SDK.Contract;

namespace HPD.Agent.ClientGenerator.Tests;

public sealed class HpdSdkIrTests
{
    [Fact]
    public void Build_ReportsUnsupportedEnumAndWritesDeterministically()
    {
        var contract = ContractWithSchema(
            "NumericEnum",
            """{ "enum": [1, 2] }""");

        var first = HpdSdkIrBuilder.Build(contract, "test-generator");
        var second = HpdSdkIrBuilder.Build(contract, "test-generator");

        var diagnostic = Assert.Single(first.Diagnostics);
        Assert.Equal("warning", diagnostic.Severity);
        Assert.Equal("HPDIR005", diagnostic.Code);
        Assert.Contains(
            first.Types,
            type => type.ComponentId == "NumericEnum" && type.Schema.Kind == "any-json");
        Assert.Equal(HpdSdkIrWriter.Write(first), HpdSdkIrWriter.Write(second));
    }

    [Fact]
    public void Validate_RejectsUnresolvedSchemaReference()
    {
        var contract = ContractWithSchema(
            "Dangling",
            """{ "$ref": "#/components/schemas/Missing" }""");
        var ir = HpdSdkIrBuilder.Build(contract, "test-generator");

        var error = Assert.Throws<InvalidDataException>(() => HpdSdkIrValidator.Validate(ir));

        Assert.Contains("does not resolve", error.Message, StringComparison.Ordinal);
        Assert.Contains("Missing", error.Message, StringComparison.Ordinal);
    }

    private static OpenApiContract ContractWithSchema(
        string componentId,
        string schemaJson) =>
        new(
            "3.1.1",
            "IR test",
            "1.0",
            "sha256:test-source",
            null,
            null,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [componentId] = Element(schemaJson)
            },
            [],
            [],
            [],
            []);

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
