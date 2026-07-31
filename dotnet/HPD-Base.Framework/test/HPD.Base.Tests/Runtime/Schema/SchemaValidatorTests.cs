using System.Text.Json;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Schema;

public sealed class SchemaValidatorTests
{
    [Fact]
    public async Task CreateRejectsMissingRequiredField()
    {
        using var provider = Provider();

        var result = await provider.GetRequiredService<IBaseSchemaValidator>().ValidateCreateAsync(Request(JsonPayload("""{"optional":"x"}""")));

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal("base.runtime.payload.requiredField", result.Error!.Code);
    }

    [Fact]
    public async Task CreateRejectsUnknownFieldsWhenPolicyRejects()
    {
        using var provider = Provider();

        var result = await provider.GetRequiredService<IBaseSchemaValidator>().ValidateCreateAsync(Request(JsonPayload("""{"title":"ok","extra":"x"}""")));

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal("base.runtime.payload.unknownField", result.Error!.Code);
    }

    [Fact]
    public async Task CreateAppliesRuntimeOwnedLiteralDefaultBeforeRequiredValidation()
    {
        using var provider = Provider();

        var result = await provider.GetRequiredService<IBaseSchemaValidator>().ValidateCreateAsync(Request(
            JsonPayload("""{"optional":"x"}"""),
            fields:
            [
                new FieldDefinition
                {
                    Id = "title",
                    Name = "title",
                    Type = BaseFieldTypes.String,
                    Required = true,
                    Nullable = false,
                    Default = new DefaultValueDescriptor
                    {
                        Kind = DefaultValueKind.Literal,
                        Owner = EnforcementOwner.Runtime,
                        Value = Json("untitled")
                    }
                },
                new FieldDefinition { Id = "optional", Name = "optional", Type = BaseFieldTypes.String }
            ]));

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal("untitled", result.Value!.Payload.Fields!["title"].GetString());
    }

    [Fact]
    public async Task CreateAppliesRuntimeTimestampGenerationForSystemFields()
    {
        using var provider = Provider();

        var result = await provider.GetRequiredService<IBaseSchemaValidator>().ValidateCreateAsync(Request(
            JsonPayload("""{"title":"ok"}"""),
            fields:
            [
                new FieldDefinition { Id = "title", Name = "title", Type = BaseFieldTypes.String, Required = true, Nullable = false },
                new FieldDefinition
                {
                    Id = "createdAt",
                    Name = "createdAt",
                    Type = BaseFieldTypes.DateTime,
                    System = true,
                    ReadOnly = true,
                    Generated = new GenerationDescriptor
                    {
                        Kind = GenerationKind.Timestamp,
                        Owner = EnforcementOwner.Runtime,
                        OnCreate = true
                    }
                }
            ]));

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(DateTimeOffset.UnixEpoch.ToString("O", System.Globalization.CultureInfo.InvariantCulture), result.Value!.Payload.Fields!["createdAt"].GetString());
    }

    [Fact]
    public async Task PatchRejectsReadOnlyField()
    {
        using var provider = Provider();

        var result = await provider.GetRequiredService<IBaseSchemaValidator>().ValidatePatchAsync(Request(
            payload: null,
            patch: FieldMapPayload("locked", "x")));

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal("base.runtime.payload.readOnlyField", result.Error!.Code);
    }

    [Fact]
    public async Task ReplaceRejectsNullForNonNullableField()
    {
        using var provider = Provider();

        var result = await provider.GetRequiredService<IBaseSchemaValidator>().ValidateReplaceAsync(Request(JsonPayload("""{"title":null}""")));

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal("base.runtime.payload.nonNullable", result.Error!.Code);
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseRuntime();
        return services.BuildServiceProvider();
    }

    private static BasePayloadValidationRequest Request(
        RecordPayload? payload,
        RecordPayload? patch = null,
        FieldDefinition[]? fields = null) => new()
    {
        Collection = new CollectionDefinition
        {
            Id = "items",
            Name = "items",
            Kind = BaseCollectionKinds.Document,
            SchemaMode = SchemaMode.Loose,
            UnknownFields = UnknownFieldPolicy.Reject,
            Fields = fields ??
            [
                new FieldDefinition
                {
                    Id = "title",
                    Name = "title",
                    Type = BaseFieldTypes.String,
                    Required = true,
                    Nullable = false
                },
                new FieldDefinition
                {
                    Id = "optional",
                    Name = "optional",
                    Type = BaseFieldTypes.String
                },
                new FieldDefinition
                {
                    Id = "locked",
                    Name = "locked",
                    Type = BaseFieldTypes.String,
                    ReadOnly = true
                }
            ]
        },
        Principal = RuntimeTestData.AnonymousPrincipal,
        Operation = RuntimeTestData.Operation(BaseOperationKind.Create),
        Payload = payload,
        Patch = patch
    };

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse($"\"{value}\"");
        return document.RootElement.Clone();
    }

    private static RecordPayload JsonPayload(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = document.RootElement.Clone()
        };
    }

    private static RecordPayload FieldMapPayload(string name, string value)
    {
        using var document = JsonDocument.Parse($$"""{"{{name}}":"{{value}}"}""");
        return new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = new Dictionary<string, JsonElement>
            {
                [name] = document.RootElement.GetProperty(name).Clone()
            }
        };
    }
}
