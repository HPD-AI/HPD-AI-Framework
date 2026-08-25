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
                    ApplicationName = "title", WireName = "title",
                    Type = BaseFieldTypes.String,
                    Presence = BaseFieldPresence.Required,
                    Nullability = BaseFieldNullability.NonNullable,
                    Default = new DefaultValueDescriptor
                    {
                        Kind = DefaultValueKind.Literal,
                        Owner = EnforcementOwner.Runtime,
                        Value = Json("untitled")
                    }
                },
                new FieldDefinition { Id = "optional", ApplicationName = "optional", WireName = "optional", Type = BaseFieldTypes.String }
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
                new FieldDefinition { Id = "title", ApplicationName = "title", WireName = "title", Type = BaseFieldTypes.String, Presence = BaseFieldPresence.Required, Nullability = BaseFieldNullability.NonNullable },
                new FieldDefinition
                {
                    Id = "createdAt",
                    ApplicationName = "createdAt", WireName = "createdAt",
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

    [Theory]
    [InlineData("é", 2, true)]
    [InlineData("é", 1, false)]
    [InlineData("e\u0301", 3, false)]
    public async Task ScalarStringConstraintsUseStrictUtf8AndRequireCanonicalNfc(string value, int maximumBytes, bool accepted)
    {
        using var provider = Provider();
        FieldDefinition field = ScalarField(BaseScalarKind.String, new BaseScalarConstraintSet
        {
            MaximumUtf8Bytes = maximumBytes,
            StringNormalization = BaseStringNormalizationRequirement.RequireNfc
        });

        OperationResult<BaseValidatedPayload> result = await provider.GetRequiredService<IBaseSchemaValidator>()
            .ValidateCreateAsync(Request(JsonPayload(JsonSerializer.Serialize(new Dictionary<string, string> { ["value"] = value })), fields: [field]));

        Assert.Equal(accepted ? OperationStatus.Ok : OperationStatus.ValidationFailed, result.Status);
        if (!accepted) Assert.Equal(BaseSchemaErrorCodes.ScalarConstraintViolated, result.Error!.Code);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("10", true)]
    [InlineData("11", false)]
    [InlineData("1.0", false)]
    public async Task Int64ConstraintsRejectCoercionAndOutOfRangeValues(string literal, bool accepted)
    {
        using var provider = Provider();
        FieldDefinition field = ScalarField(BaseScalarKind.Int64, new BaseScalarConstraintSet { MinimumInt64 = 0, MaximumInt64 = 10 });
        OperationResult<BaseValidatedPayload> result = await provider.GetRequiredService<IBaseSchemaValidator>()
            .ValidateCreateAsync(Request(JsonPayload($$"""{"value":{{literal}}}"""), fields: [field]));
        Assert.Equal(accepted ? OperationStatus.Ok : OperationStatus.ValidationFailed, result.Status);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0.1", true)]
    [InlineData("1.0", false)]
    [InlineData("0.10", false)]
    [InlineData("-0", false)]
    public async Task DecimalConstraintsRequireTheReducedCanonicalWireSpelling(string literal, bool accepted)
    {
        using var provider = Provider();
        FieldDefinition field = ScalarField(BaseScalarKind.Decimal, new BaseScalarConstraintSet());
        OperationResult<BaseValidatedPayload> result = await provider.GetRequiredService<IBaseSchemaValidator>()
            .ValidateCreateAsync(Request(JsonPayload($$"""{"value":{{literal}}}"""), fields: [field]));
        Assert.Equal(accepted ? OperationStatus.Ok : OperationStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task CollectionConstraintCountsCanonicalArrayItems()
    {
        using var provider = Provider();
        FieldDefinition field = ScalarField(BaseScalarKind.FrozenArray, new BaseScalarConstraintSet { MaximumCollectionItems = 2 });
        OperationResult<BaseValidatedPayload> result = await provider.GetRequiredService<IBaseSchemaValidator>()
            .ValidateCreateAsync(Request(JsonPayload("""{"value":[1,2,3]}"""), fields: [field]));
        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal(BaseSchemaErrorCodes.ScalarConstraintViolated, result.Error!.Code);
    }

    private static FieldDefinition ScalarField(BaseScalarKind kind, BaseScalarConstraintSet constraints) => new()
    {
        Id = "value", ApplicationName = "value", WireName = "value", Type = kind.ToString(),
        Presence = BaseFieldPresence.Required, Nullability = BaseFieldNullability.NonNullable,
        ScalarKind = kind, ScalarCodec = BaseGeneratedSchemaRegistration.ScalarCodec(kind), ScalarConstraints = constraints
    };

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
                    ApplicationName = "title", WireName = "title",
                    Type = BaseFieldTypes.String,
                    Presence = BaseFieldPresence.Required,
                    Nullability = BaseFieldNullability.NonNullable
                },
                new FieldDefinition
                {
                    Id = "optional",
                    ApplicationName = "optional", WireName = "optional",
                    Type = BaseFieldTypes.String
                },
                new FieldDefinition
                {
                    Id = "locked",
                    ApplicationName = "locked", WireName = "locked",
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
