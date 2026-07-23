using System.Text.Json;

namespace HPD.Agent.Tests.Skills;

public sealed class SkillScriptInputContractTests
{
    [Fact]
    public void CanonicalSchema_BindsAndMaterializesDefaults()
    {
        using var schemaDocument = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "inputFile": { "type": "string" },
                "limit": { "type": "integer", "default": 20 }
              },
              "required": ["inputFile"],
              "additionalProperties": false
            }
            """);
        var contract = SkillScriptInput.FromCanonicalSchema(schemaDocument.RootElement);
        using var argumentsDocument = JsonDocument.Parse("""{"inputFile":"sales.csv"}""");

        var result = contract.Bind(argumentsDocument.RootElement);

        Assert.Empty(result.Errors);
        Assert.Equal("sales.csv", result.EffectiveJson.GetProperty("inputFile").GetString());
        Assert.Equal(20, result.EffectiveJson.GetProperty("limit").GetInt32());
        Assert.Equal(result.EffectiveJson.GetRawText(), Assert.IsType<JsonElement>(result.Value).GetRawText());
    }

    [Fact]
    public void CanonicalSchema_RejectsUnknownProperties()
    {
        using var schemaDocument = JsonDocument.Parse(
            """{"type":"object","properties":{},"required":[],"additionalProperties":false}""");
        var contract = SkillScriptInput.FromCanonicalSchema(schemaDocument.RootElement);
        using var argumentsDocument = JsonDocument.Parse("""{"unexpected":true}""");

        var result = contract.Bind(argumentsDocument.RootElement);

        var error = Assert.Single(result.Errors);
        Assert.Equal("unknown_property", error.ErrorCode);
        Assert.Equal("unexpected", error.Property);
    }

    [Fact]
    public void CanonicalSchema_NormalizesFormattingForStableFingerprint()
    {
        using var first = JsonDocument.Parse(
            """{"type":"object","properties":{"value":{"type":"string"}},"required":["value"],"additionalProperties":false}""");
        using var second = JsonDocument.Parse("""
            {
              "required": [ "value" ],
              "additionalProperties": false,
              "properties": { "value": { "type": "string" } },
              "type": "object"
            }
            """);

        var firstContract = SkillScriptInput.FromCanonicalSchema(first.RootElement);
        var secondContract = SkillScriptInput.FromCanonicalSchema(second.RootElement);

        Assert.Equal(firstContract.JsonSchema.GetRawText(), secondContract.JsonSchema.GetRawText());
        Assert.Equal(firstContract.CanonicalSchemaFingerprint, secondContract.CanonicalSchemaFingerprint);
    }

    [Fact]
    public void CanonicalSchema_EnforcesBoundsAndTypedDictionaryValues()
    {
        using var schema = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "labels": {
                  "type": "object",
                  "properties": {},
                  "required": [],
                  "additionalProperties": { "type": "string", "minLength": 2 }
                },
                "limit": { "type": "integer", "minimum": 1, "maximum": 10 }
              },
              "required": ["labels", "limit"],
              "additionalProperties": false
            }
            """);
        var contract = SkillScriptInput.FromCanonicalSchema(schema.RootElement);
        using var arguments = JsonDocument.Parse("""{"labels":{"z":"ok","a":"x"},"limit":11}""");

        var result = contract.Bind(arguments.RootElement);

        var error = Assert.Single(result.Errors);
        Assert.Contains(error.ErrorCode, new[] { "string_too_short", "number_out_of_range" });
    }

    [Fact]
    public void CanonicalSchema_RejectsUnsupportedKeywordsAtLoadTime()
    {
        using var schema = JsonDocument.Parse(
            """{"type":"object","properties":{},"required":[],"additionalProperties":false,"unevaluatedProperties":false}""");

        var exception = Assert.Throws<InvalidDataException>(
            () => SkillScriptInput.FromCanonicalSchema(schema.RootElement));

        Assert.Contains("unsupported keyword", exception.Message);
    }

    [Fact]
    public void GeneratedContract_BindsTypedValueAndMatchingEffectiveJson()
    {
        var contract = SkillScriptInput.Generated(GeneratedScriptInput.AIContract);
        using var arguments = JsonDocument.Parse("""{"inputFile":"sales.csv"}""");

        var result = contract.Bind(arguments.RootElement);

        Assert.Empty(result.Errors);
        var typed = Assert.IsType<GeneratedScriptInput>(result.Value);
        Assert.Equal("sales.csv", typed.InputFile);
        Assert.Equal(20, typed.Limit);
        Assert.Equal(20, result.EffectiveJson.GetProperty("limit").GetInt32());
    }

    [Fact]
    public void GeneratedContract_DirectScriptOverloadMatchesExplicitAdapter()
    {
        var reference = new FileScriptReference("summarize.py", "python");
        var concise = SkillCapabilities.Script(
            "summarize",
            "Summarizes a file.",
            reference,
            GeneratedScriptInput.AIContract);
        var explicitScript = SkillCapabilities.Script(
            "summarize",
            "Summarizes a file.",
            reference,
            SkillScriptInput.Generated(GeneratedScriptInput.AIContract));

        Assert.Equal(
            explicitScript.InputContract.JsonSchema.GetRawText(),
            concise.InputContract.JsonSchema.GetRawText());
        Assert.Equal(
            explicitScript.InputContract.CanonicalSchemaFingerprint,
            concise.InputContract.CanonicalSchemaFingerprint);
    }

    [Fact]
    public async Task Arguments_WriteHelpersEmitExactEffectiveJson()
    {
        using var document = JsonDocument.Parse("""{"inputFile":"sales.csv","limit":20}""");
        var arguments = new SkillScriptArguments(
            document.RootElement,
            document.RootElement,
            null,
            "fingerprint");
        using var stream = new MemoryStream();

        await arguments.WriteJsonAsync(stream);
        var asynchronous = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            arguments.WriteJson(writer);
        }
        var synchronous = System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);

        Assert.Equal(arguments.Json.GetRawText(), asynchronous);
        Assert.Equal(asynchronous, synchronous);
    }

    [Fact]
    public async Task GeneratedContract_ExportsDeterministicSidecarWithoutDiscovery()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "hpd-contract-export-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(directory, "GeneratedScriptInput.schema.json");
        try
        {
            await AIInputContract.ExportSchemaAsync(GeneratedScriptInput.AIContract, output);
            var first = await File.ReadAllBytesAsync(output);
            await AIInputContract.ExportSchemaAsync(GeneratedScriptInput.AIContract, output);
            var second = await File.ReadAllBytesAsync(output);

            Assert.Equal(first, second);
            Assert.Equal(
                GeneratedScriptInput.AIContract.JsonSchema.GetRawText() + "\n",
                System.Text.Encoding.UTF8.GetString(first));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}

[AIInputContract]
public sealed partial record GeneratedScriptInput(string InputFile, int Limit = 20);
