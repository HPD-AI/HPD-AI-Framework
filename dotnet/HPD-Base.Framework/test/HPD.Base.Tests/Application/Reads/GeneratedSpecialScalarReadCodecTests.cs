using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;

namespace HPD.Base.Tests;

public sealed class GeneratedSpecialScalarReadCodecTests
{
    [Fact]
    public void OrdinaryReadClosedEnumLiteralUsesGeneratedWireAuthority()
    {
        BaseRelationalPredicate predicate = ClosedEnumLiteralRead.Definition.Plan.Predicate!;

        predicate.Right!.Literal!.Kind.Should().Be(QueryValueKind.String);
        predicate.Right.Literal.String.Should().Be("enabled-wire");
    }

    [Fact]
    public void OrdinaryReadClosedEnumLiteralRejectsUndeclaredValue()
    {
        var builder = new BaseReadDefinitionBuilder<SpecialScalarRead, SpecialScalarRead.Row>(
            "closed-enum-hostile", SpecialScalarRead.Definition.Plan.Parameters);

        Action action = () => builder.ClosedEnumLiteral((SpecialScalarMode)999);

        action.Should().Throw<InvalidOperationException>().WithMessage(BaseSchemaErrorCodes.ContractInvalid);
    }

    [Fact]
    public void OrdinaryGenericLiteralRejectsClosedEnumScalar()
    {
        var builder = new BaseReadDefinitionBuilder<SpecialScalarRead, SpecialScalarRead.Row>(
            "closed-enum-generic-scalar", SpecialScalarRead.Definition.Plan.Parameters);

        Action action = () => builder.Literal(SpecialScalarMode.Enabled);

        action.Should().Throw<InvalidOperationException>().WithMessage(BaseSchemaErrorCodes.ContractInvalid);
    }

    [Fact]
    public void OrdinaryGenericLiteralRejectsClosedEnumArraysIncludingEmptyArrays()
    {
        var builder = new BaseReadDefinitionBuilder<SpecialScalarRead, SpecialScalarRead.Row>(
            "closed-enum-generic-array", SpecialScalarRead.Definition.Plan.Parameters);

        Action populated = () => builder.Literal(new[] { SpecialScalarMode.Enabled });
        Action empty = () => builder.Literal(Array.Empty<SpecialScalarMode>());

        populated.Should().Throw<InvalidOperationException>().WithMessage(BaseSchemaErrorCodes.ContractInvalid);
        empty.Should().Throw<InvalidOperationException>().WithMessage(BaseSchemaErrorCodes.ContractInvalid);
    }

    [Fact]
    public void CanonicalJsonReadBindsExactInstalledFieldAuthority()
    {
        BaseRelationalReadParameter parameter = CanonicalJsonRead.Definition.Plan.Parameters.Single();
        BaseRelationalReadProjection projection = CanonicalJsonRead.Definition.Plan.Projection.Single();

        parameter.Kind.Should().Be(QueryValueKind.CanonicalJson);
        parameter.CanonicalJsonAuthority.Should().NotBeNull();
        projection.CanonicalJsonAuthority.Should().Be(parameter.CanonicalJsonAuthority);
        parameter.CanonicalJsonAuthority!.CollectionId.Should().Be(SpecialScalarRecord.Collection.Id);
        parameter.CanonicalJsonAuthority.FieldId.Should().Be("json");
        parameter.CanonicalJsonAuthority.MaximumCanonicalJsonBytes.Should().Be(128);
    }

    [Fact]
    public void ManualAndGeneratedCanonicalJsonReadsDeriveIdenticalAuthority()
    {
        var metadata = ManualCanonicalJsonContext.Default.ManualCanonicalJsonRecord;
        BaseCollection<ManualCanonicalJsonRecord> collection = BaseCollection.Define(
            SpecialScalarRecord.Collection.Id,
            metadata,
            schema => schema.CanonicalJson(
                    "json", nameof(ManualCanonicalJsonRecord.Json),
                    BaseJsonProperty<ManualCanonicalJsonRecord, BaseCanonicalJson>.Bind(metadata, nameof(ManualCanonicalJsonRecord.Json)))
                .Required()
                .Constraints(value => value.CanonicalJson(128, BaseJsonShape.Object, 4, 8, 8, 16, 128, 128)));
        var parameter = new BaseReadParameter<ManualCanonicalJsonParameters, BaseCanonicalJson>("canonical-json.parameter");
        var output = new BaseReadField<ManualCanonicalJsonRow, BaseCanonicalJson>("canonical-json.row.json");
        var builder = new BaseReadDefinitionBuilder<ManualCanonicalJsonParameters, ManualCanonicalJsonRow>(
            "manual-canonical-json",
            [new BaseRelationalReadParameter { Id = parameter.Id, Kind = QueryValueKind.CanonicalJson }]);
        builder.From(collection, "record", out BaseReadSource<ManualCanonicalJsonRecord> source)
            .BindCanonicalJsonParameter(parameter, (BaseField<ManualCanonicalJsonRecord, BaseCanonicalJson>)collection.Fields["json"])
            .Where(source.Field((BaseField<ManualCanonicalJsonRecord, BaseCanonicalJson>)collection.Fields["json"])
                .Equal(builder.Parameter(parameter)))
            .Project(output, source.Field((BaseField<ManualCanonicalJsonRecord, BaseCanonicalJson>)collection.Fields["json"]));

        BaseRelationalReadPlan manual = builder.Build();
        manual.Parameters.Single().CanonicalJsonAuthority.Should()
            .Be(CanonicalJsonRead.Definition.Plan.Parameters.Single().CanonicalJsonAuthority);
        manual.Projection.Single().CanonicalJsonAuthority.Should()
            .Be(CanonicalJsonRead.Definition.Plan.Projection.Single().CanonicalJsonAuthority);
    }

    [Fact]
    public void CanonicalJsonGeneratedCodecOwnsBytes()
    {
        BaseCanonicalJson json = BaseCanonicalJson.ParseAndValidate("{\"enabled\":true}"u8, new BaseCanonicalJsonLimits
        {
            MaximumCanonicalBytes = 128, MaximumDepth = 4, MaximumArrayItemsPerContainer = 8,
            MaximumObjectPropertiesPerContainer = 8, MaximumTotalNodes = 16,
            MaximumTotalStringUtf8Bytes = 128, MaximumTotalNameUtf8Bytes = 128,
        });
        QueryValue encoded = CanonicalJsonRead.Definition.ParameterCodec.Encode(new() { Json = json }).Single().Value;
        encoded.Kind.Should().Be(QueryValueKind.CanonicalJson);
        encoded.CanonicalJsonUtf8.AsSpan().SequenceEqual("{\"enabled\":true}"u8).Should().BeTrue();
    }

    [Fact]
    public async Task InMemoryComparesAndProjectsCanonicalJsonByExactBytes()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddTestStaticGrant("special.read")
            .AddCollection(SpecialScalarRecord.Collection)
            .AddRead(CanonicalJsonRead.Definition));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "canonical-json-test",
        });
        BaseCanonicalJson json = Json("{\"enabled\":true}");
        (await session.Collection(SpecialScalarRecord.Collection).CreateAsync(RecordId.Create("json-1"), new SpecialScalarRecord
        {
            Binary = BaseBinary.From([1]), Json = json, Generation = BaseModuleGeneration.Create(1), Mode = SpecialScalarMode.Enabled,
        })).Should().BeOfType<BaseSuccess<BaseRecord<SpecialScalarRecord>>>();

        CanonicalJsonRead.Row[] rows = (await session.Reads.ToArrayAsync(
            CanonicalJsonRead.Handle, new CanonicalJsonRead { Json = json })).RequireValue();
        rows.Should().ContainSingle();
        rows[0].Json.Should().Be(json);
    }

    [Fact]
    public async Task HostileCanonicalJsonProviderOutputRejectsTheCompletePage()
    {
        var store = new HostileCanonicalJsonReadStore("{\"b\":1,\"a\":2}"u8.ToArray());
        await using ServiceProvider provider = CanonicalJsonProvider(store);
        BaseResult<BasePage<CanonicalJsonRead.Row>> result = await CanonicalJsonSession(provider).Reads.ExecuteAsync(
            CanonicalJsonRead.Handle, new CanonicalJsonRead { Json = Json("{\"enabled\":true}") }, BaseReadPageRequest.Create(1, 1));

        result.Should().BeOfType<BaseFailure<BasePage<CanonicalJsonRead.Row>>>()
            .Which.Error.Code.Should().Be("base.relational.read.resultInvalid");
    }

    [Fact]
    public async Task CanonicalJsonParameterBoundsFailBeforeProviderInfluence()
    {
        var store = new HostileCanonicalJsonReadStore("{\"enabled\":true}"u8.ToArray());
        await using ServiceProvider provider = CanonicalJsonProvider(store);
        BaseCanonicalJson oversized = BaseCanonicalJson.ParseAndValidate(
            "{\"a\":1,\"b\":2,\"c\":3,\"d\":4,\"e\":5,\"f\":6,\"g\":7,\"h\":8,\"i\":9}"u8,
            new BaseCanonicalJsonLimits
            {
                MaximumCanonicalBytes = 1024, MaximumDepth = 8, MaximumArrayItemsPerContainer = 16,
                MaximumObjectPropertiesPerContainer = 16, MaximumTotalNodes = 64,
                MaximumTotalStringUtf8Bytes = 256, MaximumTotalNameUtf8Bytes = 256,
            });
        BaseResult<BasePage<CanonicalJsonRead.Row>> result = await CanonicalJsonSession(provider).Reads.ExecuteAsync(
            CanonicalJsonRead.Handle, new CanonicalJsonRead { Json = oversized }, BaseReadPageRequest.Create(1, 1));

        result.Should().BeOfType<BaseFailure<BasePage<CanonicalJsonRead.Row>>>()
            .Which.Error.Code.Should().Be("base.relational.read.invalid");
        store.Calls.Should().Be(0);
    }

    [Fact]
    public async Task MissingCanonicalJsonCapabilityFailsWithoutStringFallback()
    {
        var store = new HostileCanonicalJsonReadStore("{\"enabled\":true}"u8.ToArray(), canonicalJsonValues: false);
        await using ServiceProvider provider = CanonicalJsonProvider(store);
        BaseResult<BasePage<CanonicalJsonRead.Row>> result = await CanonicalJsonSession(provider).Reads.ExecuteAsync(
            CanonicalJsonRead.Handle, new CanonicalJsonRead { Json = Json("{\"enabled\":true}") }, BaseReadPageRequest.Create(1, 1));

        result.Should().BeOfType<BaseFailure<BasePage<CanonicalJsonRead.Row>>>()
            .Which.Error.Code.Should().Be("base.relational.read.unsupported");
        store.Calls.Should().Be(0);
    }

    [Fact]
    public async Task CanonicalJsonRootNullCannotMasqueradeAsPresentContent()
    {
        var store = new HostileCanonicalJsonReadStore("{\"enabled\":true}"u8.ToArray());
        await using ServiceProvider provider = CanonicalJsonProvider(store);
        BaseCanonicalJson rootNull = BaseCanonicalJson.ParseAndValidate("null"u8, new BaseCanonicalJsonLimits
        {
            MaximumCanonicalBytes = 128, MaximumDepth = 4, MaximumArrayItemsPerContainer = 8,
            MaximumObjectPropertiesPerContainer = 8, MaximumTotalNodes = 16,
            MaximumTotalStringUtf8Bytes = 128, MaximumTotalNameUtf8Bytes = 128,
        });

        BaseResult<BasePage<CanonicalJsonRead.Row>> result = await CanonicalJsonSession(provider).Reads.ExecuteAsync(
            CanonicalJsonRead.Handle, new CanonicalJsonRead { Json = rootNull }, BaseReadPageRequest.Create(1, 1));

        result.Should().BeOfType<BaseFailure<BasePage<CanonicalJsonRead.Row>>>()
            .Which.Error.Code.Should().Be("base.relational.read.invalid");
        store.Calls.Should().Be(0);
    }

    [Fact]
    public async Task OptionalNullableCanonicalJsonKeepsContainerNullSeparateFromPresentValues()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddTestStaticGrant("special.read")
            .AddCollection(OptionalCanonicalJsonRecord.Collection)
            .AddRead(NullableCanonicalJsonRead.Definition)
            .AddRead(PresentOptionalCanonicalJsonRead.Definition));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = CanonicalJsonSession(provider);
        BaseCollectionSession<OptionalCanonicalJsonRecord> records = session.Collection(OptionalCanonicalJsonRecord.Collection);
        (await records.CreateAsync(RecordId.Create("null-container"), new OptionalCanonicalJsonRecord { Json = null }))
            .Should().BeOfType<BaseSuccess<BaseRecord<OptionalCanonicalJsonRecord>>>();
        (await records.CreateAsync(RecordId.Create("present-container"), new OptionalCanonicalJsonRecord { Json = Json("{\"enabled\":true}") }))
            .Should().BeOfType<BaseSuccess<BaseRecord<OptionalCanonicalJsonRecord>>>();

        NullableCanonicalJsonRead.Row[] absent = (await session.Reads.ToArrayAsync(
            NullableCanonicalJsonRead.Handle, new NullableCanonicalJsonRead { Json = null })).RequireValue();
        absent.Should().ContainSingle().Which.Json.Should().BeNull();
        PresentOptionalCanonicalJsonRead.Row[] present = (await session.Reads.ToArrayAsync(
            PresentOptionalCanonicalJsonRead.Handle, new PresentOptionalCanonicalJsonRead { Json = Json("{\"enabled\":true}") })).RequireValue();
        present.Should().ContainSingle().Which.Json.Should().NotBeNull();

        NullableCanonicalJsonRead.Definition.ParameterCodec.Encode(new NullableCanonicalJsonRead { Json = null })
            .Single().Value.Kind.Should().Be(QueryValueKind.Null);
        PresentOptionalCanonicalJsonRead.Definition.Plan.Parameters.Single().Nullable.Should().BeFalse();
        NullableCanonicalJsonRead.Definition.Plan.Parameters.Single().Nullable.Should().BeTrue();
    }

    [Fact]
    public async Task OptionalNonNullableCanonicalJsonAdmitsMissingButRejectsStoredNull()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddTestStaticGrant("special.read")
            .AddCollection(OptionalNonNullCanonicalJsonRecord.Collection)
            .AddRead(OptionalNonNullCanonicalJsonRead.Definition));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = CanonicalJsonSession(provider);
        BaseResult<BaseRecord<OptionalNonNullCanonicalJsonRecord>> created = await session
            .Collection(OptionalNonNullCanonicalJsonRecord.Collection)
            .CreateAsync(RecordId.Create("missing"), new OptionalNonNullCanonicalJsonRecord());
        created.Should().BeOfType<BaseSuccess<BaseRecord<OptionalNonNullCanonicalJsonRecord>>>(
            created is BaseFailure<BaseRecord<OptionalNonNullCanonicalJsonRecord>> failure
                ? $"{failure.Error.Code}: {failure.Error.Message}"
                : string.Empty);
        OptionalNonNullCanonicalJsonRead.Row[] rows = (await session.Reads.ToArrayAsync(
            OptionalNonNullCanonicalJsonRead.Handle, new OptionalNonNullCanonicalJsonRead())).RequireValue();
        rows.Should().ContainSingle().Which.Json.Should().BeNull();

        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse("{\"Json\":null}");
        OperationResult<BaseValidatedPayload> invalid = await new DefaultBaseSchemaValidator().ValidateCreateAsync(new BasePayloadValidationRequest
        {
            Collection = OptionalNonNullCanonicalJsonRecord.Collection.Definition,
            Principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System },
            Operation = new OperationContext { Operation = BaseOperationKind.Create, CollectionId = OptionalNonNullCanonicalJsonRecord.Collection.Id },
            Payload = new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() },
        });
        invalid.Error?.Code.Should().Be("base.runtime.payload.nonNullable");
    }

    [Fact]
    public void CanonicalJsonGraphRejectsEveryInvalidNullabilityPairing()
    {
        BaseReadDefinition<CanonicalJsonRead, CanonicalJsonRead.Row> requiredSourceNullableParameter = CloneRead(
            CanonicalJsonRead.Definition,
            CanonicalJsonRead.Definition.Plan with
            {
                Parameters = [CanonicalJsonRead.Definition.Plan.Parameters.Single() with { Nullable = true }],
            },
            CanonicalJsonRead.Definition.ClientContract with
            {
                Parameters = [CanonicalJsonRead.Definition.ClientContract.Parameters.Single() with { Nullable = true }],
            });
        BaseReadDefinition<CanonicalJsonRead, CanonicalJsonRead.Row> requiredSourceNullableOutput = CloneRead(
            CanonicalJsonRead.Definition,
            CanonicalJsonRead.Definition.Plan,
            CanonicalJsonRead.Definition.ClientContract with
            {
                Row = [CanonicalJsonRead.Definition.ClientContract.Row.Single() with { Nullable = true }],
            });
        BaseReadDefinition<NullableCanonicalJsonRead, NullableCanonicalJsonRead.Row> optionalSourceRequiredOutput = CloneRead(
            NullableCanonicalJsonRead.Definition,
            NullableCanonicalJsonRead.Definition.Plan,
            NullableCanonicalJsonRead.Definition.ClientContract with
            {
                Row = [NullableCanonicalJsonRead.Definition.ClientContract.Row.Single() with { Nullable = false }],
            });

        Action validateParameter = () => ValidateGraph(SpecialScalarRecord.Collection.Definition, requiredSourceNullableParameter);
        Action validateRequiredOutput = () => ValidateGraph(SpecialScalarRecord.Collection.Definition, requiredSourceNullableOutput);
        Action validateOptionalOutput = () => ValidateGraph(OptionalCanonicalJsonRecord.Collection.Definition, optionalSourceRequiredOutput);
        validateParameter.Should().Throw<InvalidOperationException>();
        validateRequiredOutput.Should().Throw<InvalidOperationException>();
        validateOptionalOutput.Should().Throw<InvalidOperationException>();
    }

    private static BaseReadDefinition<TParameters, TRow> CloneRead<TParameters, TRow>(
        BaseReadDefinition<TParameters, TRow> original,
        BaseRelationalReadPlan plan,
        BaseReadClientContract client) => new(
            plan, null, null, original.ParameterCodec, original.RowCodec, client)
        {
            Exposure = original.Exposure,
            Authorization = original.Authorization,
            Disclosure = original.Disclosure,
            SourceAuthority = original.SourceAuthority,
            Audience = original.Audience,
            RequiredGrantId = original.RequiredGrantId,
            ConfidentialOutputFieldIds = original.ConfidentialOutputFieldIds,
            SecretOutputFieldIds = original.SecretOutputFieldIds,
            SystemSourceIds = original.SystemSourceIds,
        };

    private static void ValidateGraph(CollectionDefinition collection, IBaseReadRegistration read) =>
        BaseApplicationGraphValidator.Validate(
            [collection], [read], new BaseSubjectContractRegistry([]),
            new HPDBaseRelationalOptions(), new HPDBaseSchemaOptions());

    private static ServiceProvider CanonicalJsonProvider(HostileCanonicalJsonReadStore store)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddTestStaticGrant("special.read").AddCollection(SpecialScalarRecord.Collection)
            .AddRead(CanonicalJsonRead.Definition).UseStore(TestStoreProvider.Create(store, relational: true)));
        ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync().AsTask().GetAwaiter().GetResult().IsSuccess().Should().BeTrue();
        return provider;
    }

    private static BaseSession CanonicalJsonSession(ServiceProvider provider) =>
        provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "canonical-json-hostile-test",
        });

    private static BaseCanonicalJson Json(string value) => BaseCanonicalJson.ParseAndValidate(
        System.Text.Encoding.UTF8.GetBytes(value), new BaseCanonicalJsonLimits
        {
            MaximumCanonicalBytes = 128, MaximumDepth = 4, MaximumArrayItemsPerContainer = 8,
            MaximumObjectPropertiesPerContainer = 8, MaximumTotalNodes = 16,
            MaximumTotalStringUtf8Bytes = 128, MaximumTotalNameUtf8Bytes = 128,
        });

    [Fact]
    public void GeneratedCollectionOwnsExactModuleGenerationScalarAuthority()
    {
        FieldDefinition field = SpecialScalarRecord.Collection.Definition.Fields!
            .Single(value => value.Id == "generation");

        field.ScalarKind.Should().Be(BaseScalarKind.ModuleGeneration);
        field.Format.Should().Be("base-module-generation");
        field.ScalarConstraints!.MinimumUtf8Bytes.Should().Be(1);
        field.ScalarConstraints.MaximumUtf8Bytes.Should().Be(19);
        field.ScalarConstraints.StringNormalization.Should().BeNull();
        field.ScalarCodec!.OrderingVersion.Should().BeNull();
    }

    [Fact]
    public void ManualAndGeneratedModuleGenerationFieldsOwnIdenticalAuthority()
    {
        BaseCollection<ManualGenerationRecord> manual = BaseCollection.Define(
            "special-scalar-records",
            ManualGenerationJsonContext.Default.ManualGenerationRecord,
            schema => schema.ModuleGeneration(
                "generation",
                nameof(ManualGenerationRecord.Generation),
                BaseJsonProperty<ManualGenerationRecord, BaseModuleGeneration>.Bind(
                    ManualGenerationJsonContext.Default.ManualGenerationRecord,
                    nameof(ManualGenerationRecord.Generation))).Required());
        FieldDefinition generatedField = SpecialScalarRecord.Collection.Definition.Fields!
            .Single(value => value.Id == "generation");
        FieldDefinition manualField = manual.Definition.Fields!.Single();

        manualField.ScalarKind.Should().Be(generatedField.ScalarKind);
        manualField.ScalarCodec.Should().BeEquivalentTo(generatedField.ScalarCodec);
        manualField.ScalarConstraints.Should().BeEquivalentTo(generatedField.ScalarConstraints);
        manualField.ScalarConstraintChecksum.Should().Be(generatedField.ScalarConstraintChecksum);
    }

    [Fact]
    public void ModuleGenerationAdmitsUniqueEqualityIndexButRejectsNonuniqueOrderingIndex()
    {
        var metadata = ManualGenerationJsonContext.Default.ManualGenerationRecord;
        var generation = BaseJsonProperty<ManualGenerationRecord, BaseModuleGeneration>.Bind(
            metadata, nameof(ManualGenerationRecord.Generation));

        Action nonunique = () => BaseCollection.Define(
            "manual-generation-nonunique",
            metadata,
            schema =>
            {
                schema.ModuleGeneration("generation", nameof(ManualGenerationRecord.Generation), generation).Required();
                schema.Index("manual-generation-nonunique.idx", 1, index => index.Part(generation));
            });
        Action unique = () => BaseCollection.Define(
            "manual-generation-unique",
            metadata,
            schema =>
            {
                schema.ModuleGeneration("generation", nameof(ManualGenerationRecord.Generation), generation).Required();
                schema.Index("manual-generation-unique.idx", 1, index => index
                    .Part(generation)
                    .Unique()
                    .Predicate(predicate => predicate.Root(predicate.Equal(
                        "generation-equal", generation, BaseModuleGeneration.Create(7)))));
            });

        nonunique.Should().Throw<InvalidOperationException>()
            .WithMessage(BaseSchemaErrorCodes.ContractInvalid);
        unique.Should().NotThrow();
        Action noncanonicalLiteral = () => BaseGeneratedSchemaRegistration.ScalarLiteral(
            BaseScalarKind.ModuleGeneration,
            BaseGeneratedSchemaRegistration.ScalarCodec(BaseScalarKind.ModuleGeneration),
            "\"01\"");
        noncanonicalLiteral.Should().Throw<FormatException>();
    }

    [Fact]
    public void GeneratedCodecOwnsPresentAndAbsentSpecialScalars()
    {
        BaseModuleGeneration generation = BaseModuleGeneration.Create(7);
        BaseBinary binary = BaseBinary.From([1, 2, 3]);

        BaseRelationalParameterValue[] present = SpecialScalarRead.Definition.ParameterCodec.Encode(new()
        {
            Binary = binary,
            Generation = generation,
            Mode = SpecialScalarMode.Enabled,
        });

        present.Single(value => value.ParameterId == "special.binary").Value.String.Should().Be("AQID");
        present.Single(value => value.ParameterId == "special.generation").Value.String.Should().Be("7");
        present.Single(value => value.ParameterId == "special.mode").Value.String.Should().Be("enabled-wire");

        BaseRelationalParameterValue[] absent = SpecialScalarRead.Definition.ParameterCodec.Encode(new());
        absent.Should().OnlyContain(value => value.Value.Kind == QueryValueKind.Null);
    }

    [Fact]
    public void GeneratedCodecRejectsHostileNoncanonicalBase64()
    {
        var row = new BaseRelationalRow
        {
            Fields =
            [
                Field("special.row.binary", "AQI"),
                Field("special.row.generation", "7"),
                Field("special.row.mode", "enabled-wire"),
                Field("special.row.revision", "test:1"),
            ],
        };

        Action decode = () => SpecialScalarRead.Definition.RowCodec.Decode(row);
        decode.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void GeneratedCodecRejectsBinaryOutsideExactDecodedRange(int length)
    {
        var row = new BaseRelationalRow
        {
            Fields =
            [
                Field("special.row.binary", Convert.ToBase64String(new byte[length])),
                Field("special.row.generation", "7"),
                Field("special.row.mode", "enabled-wire"),
                Field("special.row.revision", "test:1"),
            ],
        };

        ((Action)(() => SpecialScalarRead.Definition.RowCodec.Decode(row)))
            .Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public async Task HostileProviderBinaryOutsideExactDecodedRangeFailsBeforeApplicationMaterialization(int length)
    {
        var store = new HostileScalarReadStore("hostile-binary.row.binary", Convert.ToBase64String(new byte[length]));
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddTestStaticGrant("special.read")
            .AddCollection(SpecialScalarRecord.Collection)
            .AddRead(HostileBinaryRead.Definition)
            .UseStore(TestStoreProvider.Create(store, relational: true)));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "hostile-binary-test",
        });

        BaseResult<BasePage<HostileBinaryRead.Row>> result = await session.Reads.ExecuteAsync(
            HostileBinaryRead.Handle, new HostileBinaryRead(), BaseReadPageRequest.Create(1, 1));

        result.Should().BeOfType<BaseFailure<BasePage<HostileBinaryRead.Row>>>()
            .Which.Error.Code.Should().Be("base.relational.read.resultInvalid");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void GeneratedParameterCodecRejectsBinaryOutsideExactDecodedRange(int length)
    {
        Action encode = () => SpecialScalarRead.Definition.ParameterCodec.Encode(new SpecialScalarRead
        {
            Binary = BaseBinary.From(new byte[length]),
        });

        encode.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public async Task BinaryParameterBoundsFailBeforeProviderInfluence(int length)
    {
        var store = new HostileScalarReadStore("binary-equality.row.binary", "AQID");
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddTestStaticGrant("special.read")
            .AddCollection(SpecialScalarRecord.Collection)
            .AddRead(BinaryEqualityRead.Definition)
            .UseStore(TestStoreProvider.Create(store, relational: true)));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "binary-parameter-test",
        });

        BaseResult<BasePage<BinaryEqualityRead.Row>> result = await session.Reads.ExecuteAsync(
            BinaryEqualityRead.Handle,
            new BinaryEqualityRead { Binary = BaseBinary.From(new byte[length]) },
            BaseReadPageRequest.Create(1, 1));

        result.Should().BeOfType<BaseFailure<BasePage<BinaryEqualityRead.Row>>>()
            .Which.Error.Code.Should().Be("base.relational.read.invalid");
        store.Calls.Should().Be(0);
    }

    [Fact]
    public void BinaryBoundsParticipateInRegisteredReadScalarChecksum()
    {
        const string serializer = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        string first = BaseReadGeneratedContract.BindScalarConstraints(serializer,
        [
            new BaseReadClientProperty
            {
                Id = "binary", GeneratedName = "Binary", WireName = "binary", Kind = QueryValueKind.String,
                Array = false, Nullable = false, MinimumBinaryBytes = 1, MaximumBinaryBytes = 16,
            },
        ]);
        string second = BaseReadGeneratedContract.BindScalarConstraints(serializer,
        [
            new BaseReadClientProperty
            {
                Id = "binary", GeneratedName = "Binary", WireName = "binary", Kind = QueryValueKind.String,
                Array = false, Nullable = false, MinimumBinaryBytes = 2, MaximumBinaryBytes = 16,
            },
        ]);

        first.Should().NotBe(second);
        first.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void GeneratedCodecRejectsHostileUnknownEnumLiteral()
    {
        var row = new BaseRelationalRow
        {
            Fields =
            [
                Field("special.row.binary", "AQID"),
                Field("special.row.generation", "7"),
                Field("special.row.mode", "unknown-wire"),
                Field("special.row.revision", "test:1"),
            ],
        };

        ((Action)(() => SpecialScalarRead.Definition.RowCodec.Decode(row))).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("+1")]
    [InlineData("9223372036854775808")]
    public void GeneratedCodecRejectsHostileModuleGeneration(string generation)
    {
        var row = new BaseRelationalRow
        {
            Fields =
            [
                Field("special.row.binary", "AQID"),
                Field("special.row.generation", generation),
                Field("special.row.mode", "enabled-wire"),
                Field("special.row.revision", "test:1"),
            ],
        };

        ((Action)(() => SpecialScalarRead.Definition.RowCodec.Decode(row))).Should().Throw<FormatException>();
    }

    [Fact]
    public async Task HostileRelationalProviderUnknownEnumFailsBeforeApplicationMaterialization()
    {
        var store = new HostileScalarReadStore("hostile-enum.row.mode", "unknown-wire");
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddCollection(SpecialScalarRecord.Collection)
            .AddRead(HostileEnumRead.Definition)
            .UseStore(TestStoreProvider.Create(store, relational: true)));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectId = "hostile-scalar-test",
        });

        BaseResult<BasePage<HostileEnumRead.Row>> result = await session.Reads.ExecuteAsync(
            HostileEnumRead.Handle, new HostileEnumRead(), BaseReadPageRequest.Create(1, 1));

        result.Should().BeOfType<BaseFailure<BasePage<HostileEnumRead.Row>>>()
            .Which.Error.Code.Should().Be("base.relational.read.resultInvalid");
    }

    [Fact]
    public async Task HostileRelationalProviderNoncanonicalModuleGenerationFailsBeforeApplicationMaterialization()
    {
        var store = new HostileScalarReadStore("hostile-generation.row.generation", "01");
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddCollection(SpecialScalarRecord.Collection)
            .AddRead(HostileGenerationRead.Definition)
            .UseStore(TestStoreProvider.Create(store, relational: true)));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectId = "hostile-generation-test",
        });

        BaseResult<BasePage<HostileGenerationRead.Row>> result = await session.Reads.ExecuteAsync(
            HostileGenerationRead.Handle, new HostileGenerationRead(), BaseReadPageRequest.Create(1, 1));

        result.Should().BeOfType<BaseFailure<BasePage<HostileGenerationRead.Row>>>()
            .Which.Error.Code.Should().Be("base.relational.read.resultInvalid");
    }

    [Fact]
    public async Task SqliteRoundTripsStoredModuleGenerationThroughRegisteredRead()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-base-generation-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddHPDBase(builder => builder
                .ConfigureSchema(options =>
                {
                    options.ApplicationId = "special-scalar-test";
                    options.PlanProtectionKey = Enumerable.Repeat((byte)0x61, 32).ToArray();
                })
                .AddTestPolicyAuthority<AllowPolicyEvaluator>()
                .AddTestStaticGrant("special.read")
                .AddCollection(SpecialScalarRecord.Collection)
                .AddRead(ModuleGenerationOnlyRead.Definition)
                .AddRead(CanonicalJsonRead.Definition)
                .AddRead(BinaryEqualityRead.Definition)
                .UseStore(SqliteStore.Configure(options =>
                {
                    options.StoreId = "special-scalar-sqlite";
                    options.DataSource = database;
                })));
            await using ServiceProvider provider = services.BuildServiceProvider();
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            OperationResult<BaseSchemaPlan> plan = await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "special-scalar-sqlite" });
            plan.IsSuccess().Should().BeTrue($"{plan.Error?.Code}: {plan.Error?.Message}; target={plan.Error?.Target}");
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.Value!.ProtectedArtifact }))
                .IsSuccess().Should().BeTrue();
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();

            PrincipalContext principal = new()
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "special-scalar-test",
            };
            OperationContext operation = new()
            {
                ApplicationId = "special-scalar-test",
                Audience = HPDBaseEndpointAudience.Application,
                Operation = BaseOperationKind.Create,
                CollectionId = SpecialScalarRecord.Collection.Id,
                Now = DateTimeOffset.UtcNow,
            };
            OperationResult<RecordEnvelope> created = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
                SpecialScalarRecord.Collection.Id,
                new RecordCreateRequest
                {
                    RequestedId = RecordId.Create("generation-1"),
                    Payload = new RecordPayload
                    {
                        Kind = RecordPayloadKind.FieldMap,
                        Fields = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
                        {
                            ["Binary"] = System.Text.Json.JsonSerializer.SerializeToElement("AQID"),
                            ["Json"] = System.Text.Json.JsonSerializer.SerializeToElement(new { value = true }),
                            ["Generation"] = System.Text.Json.JsonSerializer.SerializeToElement("9223372036854775807"),
                            ["Mode"] = System.Text.Json.JsonSerializer.SerializeToElement("enabled-wire"),
                        },
                    },
                }, principal, operation);
            created.IsSuccess().Should().BeTrue($"{created.Error?.Code}: {created.Error?.Message}; fields={string.Join(',', SpecialScalarRecord.Collection.Definition.Fields!.Select(field => field.Id + '/' + field.WireName))}");

            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal);
            ModuleGenerationOnlyRead.Row row = (await session.Reads.ToArrayAsync(
                ModuleGenerationOnlyRead.Handle, new ModuleGenerationOnlyRead())).RequireValue().Single();
            row.Generation.ToCanonicalString().Should().Be("9223372036854775807");
            CanonicalJsonRead.Row jsonRow = (await session.Reads.ToArrayAsync(
                CanonicalJsonRead.Handle, new CanonicalJsonRead { Json = Json("{\"value\":true}") })).RequireValue().Single();
            jsonRow.Json.Should().Be(Json("{\"value\":true}"));
            BinaryEqualityRead.Row binaryRow = (await session.Reads.ToArrayAsync(
                BinaryEqualityRead.Handle, new BinaryEqualityRead { Binary = BaseBinary.From([1, 2, 3]) }))
                .RequireValue().Single();
            binaryRow.Binary.Should().Be(BaseBinary.From([1, 2, 3]));
        }
        finally
        {
            foreach (string candidate in new[] { database, database + "-wal", database + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task InMemoryProjectsPresentAndMissingOptionalModuleGenerations()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder
            .AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddTestStaticGrant("optional-generation.read")
            .AddCollection(OptionalGenerationRecord.Collection)
            .AddRead(OptionalGenerationRead.Definition));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();

        PrincipalContext principal = new()
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "optional-generation-test",
        };
        IBaseRecordRuntime runtime = provider.GetRequiredService<IBaseRecordRuntime>();
        OperationContext operation = new()
        {
            ApplicationId = "hpd.base.application",
            Audience = HPDBaseEndpointAudience.Application,
            Operation = BaseOperationKind.Create,
            CollectionId = OptionalGenerationRecord.Collection.Id,
            Now = DateTimeOffset.UtcNow,
        };
        OperationResult<RecordEnvelope> missing = await runtime.CreateAsync(
            OptionalGenerationRecord.Collection.Id,
            new RecordCreateRequest
            {
                RequestedId = RecordId.Create("missing"),
                Payload = new RecordPayload
                {
                    Kind = RecordPayloadKind.FieldMap,
                    Fields = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
                    {
                        ["Name"] = System.Text.Json.JsonSerializer.SerializeToElement("missing"),
                    },
                },
            }, principal, operation);
        missing.IsSuccess().Should().BeTrue($"{missing.Error?.Code}: {missing.Error?.Message}");
        OperationResult<RecordEnvelope> present = await runtime.CreateAsync(
            OptionalGenerationRecord.Collection.Id,
            new RecordCreateRequest
            {
                RequestedId = RecordId.Create("present"),
                Payload = new RecordPayload
                {
                    Kind = RecordPayloadKind.FieldMap,
                    Fields = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
                    {
                        ["Name"] = System.Text.Json.JsonSerializer.SerializeToElement("present"),
                        ["Generation"] = System.Text.Json.JsonSerializer.SerializeToElement("7"),
                    },
                },
            }, principal, operation);
        present.IsSuccess().Should().BeTrue($"{present.Error?.Code}: {present.Error?.Message}");

        OptionalGenerationRead.Row[] rows = (await provider.GetRequiredService<IBaseSessionFactory>()
            .For(principal).Reads.ToArrayAsync(OptionalGenerationRead.Handle, new OptionalGenerationRead()))
            .RequireValue();

        rows.Should().HaveCount(2);
        rows.Single(row => row.Name == "missing").Generation.Should().BeNull();
        rows.Single(row => row.Name == "present").Generation!
            .ToCanonicalString().Should().Be("7");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad token")]
    [InlineData("bad\nrevision")]
    public void GeneratedCodecRejectsHostileRevisionRows(string revision)
    {
        var row = new BaseRelationalRow
        {
            Fields =
            [
                Field("special.row.binary", "AQID"),
                Field("special.row.generation", "7"),
                Field("special.row.mode", "enabled-wire"),
                Field("special.row.revision", revision),
            ],
        };

        ((Action)(() => SpecialScalarRead.Definition.RowCodec.Decode(row))).Should().Throw<ArgumentException>();
    }

    private static BaseRelationalFieldValue Field(string id, string value) => new()
    {
        FieldId = id,
        Value = new QueryValue { Kind = QueryValueKind.String, String = value },
    };

    private sealed class HostileScalarReadStore(string fieldId, string value)
        : FakeRecordStore("hostile-special-scalars"), IRelationalReadStore
    {
        public int Calls { get; private set; }
        public RelationalReadCapability RelationalReads { get; } = new()
        {
            Supported = true,
            JoinKinds = [BaseJoinKind.Inner], AggregateKinds = [BaseAggregateKind.Count], ComparisonOperators = [FilterOperator.Equal],
            ValueKinds = [QueryValueKind.String], MaxSources = 1, MaxJoins = 0, MaxPredicateNodes = 8, MaxGroupKeys = 1,
            MaxAggregates = 8, MaxProjectionFields = 16, MaxSortFields = 8, MaxResultRows = 1_000, MaxResultBytes = 4 * 1024 * 1024,
            SnapshotConsistency = true, CompleteDependencyEvidence = true,
        };
        public ValueTask<OperationResult<BaseRelationalReadExecutionResult>> ExecuteReadAsync(BaseRelationalReadExecutionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(OperationResults.Ok(new BaseRelationalReadExecutionResult
            {
                Result = new BaseRelationalReadResult
                {
                    Rows = [new BaseRelationalRow { Fields = [Field(fieldId, value)] }],
                    Page = new PageInfo { Page = 1, PerPage = 1, Limit = 1 }, Count = 1,
                },
                DependencyEvidence = [new BaseReadDependencyEvidence { CollectionId = SpecialScalarRecord.Collection.Id }],
                SnapshotAuthority = TestRelationalReadAuthority.Create(request),
            }));
        }
    }

    private sealed class HostileCanonicalJsonReadStore(byte[] bytes, bool canonicalJsonValues = true)
        : FakeRecordStore("hostile-canonical-json"), IRelationalReadStore
    {
        public int Calls { get; private set; }
        public RelationalReadCapability RelationalReads { get; } = new()
        {
            Supported = true, JoinKinds = [BaseJoinKind.Inner], AggregateKinds = [BaseAggregateKind.Count],
            ComparisonOperators = [FilterOperator.Equal, FilterOperator.NotEqual],
            ValueKinds = [QueryValueKind.CanonicalJson], CanonicalJsonValues = canonicalJsonValues,
            MaxSources = 1, MaxJoins = 0, MaxPredicateNodes = 8, MaxGroupKeys = 1, MaxAggregates = 8,
            MaxProjectionFields = 16, MaxSortFields = 8, MaxResultRows = 1_000, MaxResultBytes = 4 * 1024 * 1024,
            SnapshotConsistency = true, CompleteDependencyEvidence = true,
        };

        public ValueTask<OperationResult<BaseRelationalReadExecutionResult>> ExecuteReadAsync(
            BaseRelationalReadExecutionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(OperationResults.Ok(new BaseRelationalReadExecutionResult
            {
                Result = new BaseRelationalReadResult
                {
                    Rows = [new BaseRelationalRow { Fields = [new BaseRelationalFieldValue
                    {
                        FieldId = "canonical-json.row.json",
                        Value = new QueryValue { Kind = QueryValueKind.CanonicalJson, CanonicalJsonUtf8 = ImmutableArray.Create(bytes) },
                    }] }],
                    Page = new PageInfo { Page = 1, PerPage = 1, Limit = 1 }, Count = 1,
                },
                DependencyEvidence = [new BaseReadDependencyEvidence { CollectionId = SpecialScalarRecord.Collection.Id }],
                SnapshotAuthority = TestRelationalReadAuthority.Create(request),
            }));
        }
    }

}

[BaseRead("hostile-generation", typeof(SpecialScalarReadJsonContext), RequiredGrantId = "special.read")]
internal sealed partial record HostileGenerationRead
{
    public sealed partial record Row
    {
        [BaseReadField("hostile-generation.row.generation")] public required BaseModuleGeneration Generation { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<HostileGenerationRead, Row> read) =>
        read.From(SpecialScalarRecord.Collection, "record", out BaseReadSource<SpecialScalarRecord> record)
            .Project(Row.Fields.Generation, record.Field(SpecialScalarRecord.Fields.Generation));
}

[BaseRead("optional-generations", typeof(SpecialScalarReadJsonContext), RequiredGrantId = "optional-generation.read")]
internal sealed partial record OptionalGenerationRead
{
    public sealed partial record Row
    {
        [BaseReadField("optional-generations.row.name")] public required string Name { get; init; }
        [BaseReadField("optional-generations.row.generation")] public BaseModuleGeneration? Generation { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<OptionalGenerationRead, Row> read) =>
        read.From(OptionalGenerationRecord.Collection, "record", out BaseReadSource<OptionalGenerationRecord> record)
            .Project(Row.Fields.Name, record.Field(OptionalGenerationRecord.Fields.Name))
            .Project(Row.Fields.Generation, record.Field(OptionalGenerationRecord.Fields.Generation))
            .OrderBy(record.RecordId);
}

[BaseRead("module-generation-only", typeof(SpecialScalarReadJsonContext), RequiredGrantId = "special.read")]
internal sealed partial record ModuleGenerationOnlyRead
{
    public sealed partial record Row
    {
        [BaseReadField("module-generation-only.row.generation")]
        public required BaseModuleGeneration Generation { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<ModuleGenerationOnlyRead, Row> read) =>
        read.From(SpecialScalarRecord.Collection, "record", out BaseReadSource<SpecialScalarRecord> record)
            .Project(Row.Fields.Generation, record.Field(SpecialScalarRecord.Fields.Generation));
}

[BaseRead("hostile-enum", typeof(SpecialScalarReadJsonContext), RequiredGrantId = "special.read")]
internal sealed partial record HostileEnumRead
{
    public sealed partial record Row
    {
        [BaseReadField("hostile-enum.row.mode")] public required SpecialScalarMode Mode { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<HostileEnumRead, Row> read) =>
        read.From(SpecialScalarRecord.Collection, "record", out BaseReadSource<SpecialScalarRecord> record)
            .Project(Row.Fields.Mode, record.Field(SpecialScalarRecord.Fields.Mode));
}

[BaseRead("hostile-binary", typeof(SpecialScalarReadJsonContext), RequiredGrantId = "special.read")]
internal sealed partial record HostileBinaryRead
{
    public sealed partial record Row
    {
        [BaseReadField("hostile-binary.row.binary", MinimumBytes = 1, MaximumBytes = 16)]
        public required BaseBinary Binary { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<HostileBinaryRead, Row> read) =>
        read.From(SpecialScalarRecord.Collection, "record", out BaseReadSource<SpecialScalarRecord> record)
            .Project(Row.Fields.Binary, record.Field(SpecialScalarRecord.Fields.Binary));
}

[BaseRead("closed-enum-literal", typeof(SpecialScalarReadJsonContext), RequiredGrantId = "special.read")]
internal sealed partial record ClosedEnumLiteralRead
{
    public sealed partial record Row
    {
        [BaseReadField("closed-enum-literal.row.mode")] public required SpecialScalarMode Mode { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<ClosedEnumLiteralRead, Row> read) =>
        read.From(SpecialScalarRecord.Collection, "record", out BaseReadSource<SpecialScalarRecord> record)
            .Where(record.Field(SpecialScalarRecord.Fields.Mode).Equal(read.ClosedEnumLiteral(SpecialScalarMode.Enabled)))
            .Project(Row.Fields.Mode, record.Field(SpecialScalarRecord.Fields.Mode));
}

[BaseRead("special-scalars", typeof(SpecialScalarReadJsonContext), RequiredGrantId = "special.read")]
internal sealed partial record SpecialScalarRead
{
    [BaseReadParameter("special.binary", MinimumBytes = 1, MaximumBytes = 16)] public BaseBinary? Binary { get; init; }
    [BaseReadParameter("special.generation")] public BaseModuleGeneration? Generation { get; init; }
    [BaseReadParameter("special.mode")] public SpecialScalarMode? Mode { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("special.row.binary", MinimumBytes = 1, MaximumBytes = 16)] public required BaseBinary Binary { get; init; }
        [BaseReadField("special.row.generation")] public required BaseModuleGeneration Generation { get; init; }
        [BaseReadField("special.row.mode")] public required SpecialScalarMode Mode { get; init; }
        [BaseReadField("special.row.revision")] public required RevisionToken Revision { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<SpecialScalarRead, Row> read)
    {
        read.From(SpecialScalarRecord.Collection, "record", out BaseReadSource<SpecialScalarRecord> record)
            .Project(Row.Fields.Binary, record.Field(SpecialScalarRecord.Fields.Binary))
            .Project(Row.Fields.Generation, record.Field(SpecialScalarRecord.Fields.Generation))
            .Project(Row.Fields.Mode, record.Field(SpecialScalarRecord.Fields.Mode))
            .Project(Row.Fields.Revision, record.Revision);
    }
}

[BaseRead("binary-equality", typeof(SpecialScalarReadJsonContext), RequiredGrantId = "special.read")]
internal sealed partial record BinaryEqualityRead
{
    [BaseReadParameter("binary-equality.parameter", MinimumBytes = 1, MaximumBytes = 16)]
    public required BaseBinary Binary { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("binary-equality.row.binary", MinimumBytes = 1, MaximumBytes = 16)]
        public required BaseBinary Binary { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<BinaryEqualityRead, Row> read) => read
        .From(SpecialScalarRecord.Collection, "record", out BaseReadSource<SpecialScalarRecord> record)
        .Where(record.Field(SpecialScalarRecord.Fields.Binary).Equal(read.Parameter(Parameters.Binary)))
        .Project(Row.Fields.Binary, record.Field(SpecialScalarRecord.Fields.Binary));
}

[BaseRead("canonical-json", typeof(SpecialScalarReadJsonContext), RequiredGrantId = "special.read")]
internal sealed partial record CanonicalJsonRead
{
    [BaseReadParameter("canonical-json.parameter")] public required BaseCanonicalJson Json { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("canonical-json.row.json")] public required BaseCanonicalJson Json { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<CanonicalJsonRead, Row> read)
    {
        read.From(SpecialScalarRecord.Collection, "record", out BaseReadSource<SpecialScalarRecord> record)
            .BindCanonicalJsonParameter(Parameters.Json, SpecialScalarRecord.Fields.Json)
            .Where(record.Field(SpecialScalarRecord.Fields.Json).Equal(read.Parameter(Parameters.Json)))
            .Project(Row.Fields.Json, record.Field(SpecialScalarRecord.Fields.Json));
    }
}

[BaseCollection("special-scalar-records", typeof(SpecialScalarReadJsonContext))]
internal sealed partial record SpecialScalarRecord
{
    [BaseField("binary", MinimumBytes = 1, MaximumBytes = 16)] public required BaseBinary Binary { get; init; }
    [BaseField("json", MaximumCanonicalJsonBytes = 128, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 4, MaximumJsonArrayItems = 8,
        MaximumJsonObjectProperties = 8, MaximumJsonTotalNodes = 16, MaximumJsonTotalStringUtf8Bytes = 128,
        MaximumJsonTotalNameUtf8Bytes = 128)] public required BaseCanonicalJson Json { get; init; }
    [BaseField("generation")] public required BaseModuleGeneration Generation { get; init; }
    [BaseField("mode", AllowedEnumLiterals = ["enabled-wire"], Operators = BaseFieldOperator.Equal), JsonConverter(typeof(BaseClosedEnumJsonConverter<SpecialScalarMode>))]
    public required SpecialScalarMode Mode { get; init; }
}

[BaseCollection("optional-canonical-json-records", typeof(SpecialScalarReadJsonContext))]
internal sealed partial record OptionalCanonicalJsonRecord
{
    [BaseField("json", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.Nullable,
        MaximumCanonicalJsonBytes = 128, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 4, MaximumJsonArrayItems = 8,
        MaximumJsonObjectProperties = 8, MaximumJsonTotalNodes = 16, MaximumJsonTotalStringUtf8Bytes = 128,
        MaximumJsonTotalNameUtf8Bytes = 128)]
    public BaseCanonicalJson? Json { get; init; }
}

[BaseCollection("optional-nonnull-canonical-json-records", typeof(OptionalNonNullCanonicalJsonContext))]
internal sealed partial record OptionalNonNullCanonicalJsonRecord
{
    [BaseField("json", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable,
        MaximumCanonicalJsonBytes = 128, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 4, MaximumJsonArrayItems = 8,
        MaximumJsonObjectProperties = 8, MaximumJsonTotalNodes = 16, MaximumJsonTotalStringUtf8Bytes = 128,
        MaximumJsonTotalNameUtf8Bytes = 128)]
    public BaseCanonicalJson? Json { get; init; }
}

[BaseRead("optional-nonnull-canonical-json", typeof(OptionalNonNullCanonicalJsonReadContext), RequiredGrantId = "special.read")]
internal sealed partial record OptionalNonNullCanonicalJsonRead
{
    public sealed partial record Row
    {
        [BaseReadField("optional-nonnull-canonical-json.row.json")] public BaseCanonicalJson? Json { get; init; }
    }
    public static void Configure(BaseReadDefinitionBuilder<OptionalNonNullCanonicalJsonRead, Row> read) =>
        read.From(OptionalNonNullCanonicalJsonRecord.Collection, "record", out BaseReadSource<OptionalNonNullCanonicalJsonRecord> record)
            .Project(Row.Fields.Json, record.Field(OptionalNonNullCanonicalJsonRecord.Fields.Json));
}

[BaseRead("nullable-canonical-json", typeof(SpecialScalarReadJsonContext), RequiredGrantId = "special.read")]
internal sealed partial record NullableCanonicalJsonRead
{
    [BaseReadParameter("nullable-canonical-json.parameter")] public BaseCanonicalJson? Json { get; init; }
    public sealed partial record Row
    {
        [BaseReadField("nullable-canonical-json.row.json")] public BaseCanonicalJson? Json { get; init; }
    }
    public static void Configure(BaseReadDefinitionBuilder<NullableCanonicalJsonRead, Row> read) =>
        read.From(OptionalCanonicalJsonRecord.Collection, "record", out BaseReadSource<OptionalCanonicalJsonRecord> record)
            .BindCanonicalJsonParameter(Parameters.Json, OptionalCanonicalJsonRecord.Fields.Json)
            .Where(record.Field(OptionalCanonicalJsonRecord.Fields.Json).Equal(read.Parameter(Parameters.Json)))
            .Project(Row.Fields.Json, record.Field(OptionalCanonicalJsonRecord.Fields.Json));
}

[BaseRead("present-optional-canonical-json", typeof(SpecialScalarReadJsonContext), RequiredGrantId = "special.read")]
internal sealed partial record PresentOptionalCanonicalJsonRead
{
    [BaseReadParameter("present-optional-canonical-json.parameter")] public required BaseCanonicalJson Json { get; init; }
    public sealed partial record Row
    {
        [BaseReadField("present-optional-canonical-json.row.json")] public BaseCanonicalJson? Json { get; init; }
    }
    public static void Configure(BaseReadDefinitionBuilder<PresentOptionalCanonicalJsonRead, Row> read) =>
        read.From(OptionalCanonicalJsonRecord.Collection, "record", out BaseReadSource<OptionalCanonicalJsonRecord> record)
            .BindCanonicalJsonParameter(Parameters.Json, OptionalCanonicalJsonRecord.Fields.Json)
            .Where(record.OptionalField(OptionalCanonicalJsonRecord.Fields.Json).Equal(read.Parameter(Parameters.Json)))
            .Project(Row.Fields.Json, record.Field(OptionalCanonicalJsonRecord.Fields.Json));
}

internal enum SpecialScalarMode
{
    [JsonStringEnumMemberName("enabled-wire")]
    Enabled,
}

internal sealed record ManualGenerationRecord
{
    public required BaseModuleGeneration Generation { get; init; }
}

internal sealed record ManualCanonicalJsonRecord
{
    public required BaseCanonicalJson Json { get; init; }
}

internal sealed record ManualCanonicalJsonParameters
{
    public required BaseCanonicalJson Json { get; init; }
}

internal sealed record ManualCanonicalJsonRow
{
    public required BaseCanonicalJson Json { get; init; }
}

[BaseCollection("optional-generation-records", typeof(SpecialScalarReadJsonContext))]
internal sealed partial record OptionalGenerationRecord
{
    [BaseField("name")] public required string Name { get; init; }
    [BaseField("generation", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable)]
    public BaseModuleGeneration? Generation { get; init; }
}

[JsonSerializable(typeof(ManualGenerationRecord))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    PropertyNameCaseInsensitive = false,
    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
    UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    MaxDepth = 64,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    IgnoreReadOnlyProperties = false,
    IgnoreReadOnlyFields = false,
    IncludeFields = false,
    WriteIndented = false,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    AllowTrailingCommas = false,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Disallow,
    PreferredObjectCreationHandling = System.Text.Json.Serialization.JsonObjectCreationHandling.Replace,
    AllowDuplicateProperties = false,
    AllowOutOfOrderMetadataProperties = false,
    DefaultBufferSize = 16384)]
internal sealed partial class ManualGenerationJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(ManualCanonicalJsonRecord))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    PropertyNameCaseInsensitive = false,
    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
    UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    MaxDepth = 64,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    IgnoreReadOnlyProperties = false,
    IgnoreReadOnlyFields = false,
    IncludeFields = false,
    WriteIndented = false,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    AllowTrailingCommas = false,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Disallow,
    PreferredObjectCreationHandling = System.Text.Json.Serialization.JsonObjectCreationHandling.Replace,
    AllowDuplicateProperties = false,
    AllowOutOfOrderMetadataProperties = false,
    DefaultBufferSize = 16384)]
internal sealed partial class ManualCanonicalJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(SpecialScalarRead))]
[JsonSerializable(typeof(SpecialScalarRead.Row), TypeInfoPropertyName = "SpecialScalarReadRow")]
[JsonSerializable(typeof(BinaryEqualityRead))]
[JsonSerializable(typeof(BinaryEqualityRead.Row), TypeInfoPropertyName = "BinaryEqualityReadRow")]
[JsonSerializable(typeof(CanonicalJsonRead))]
[JsonSerializable(typeof(CanonicalJsonRead.Row), TypeInfoPropertyName = "CanonicalJsonReadRow")]
[JsonSerializable(typeof(HostileEnumRead))]
[JsonSerializable(typeof(HostileEnumRead.Row), TypeInfoPropertyName = "HostileEnumReadRow")]
[JsonSerializable(typeof(HostileBinaryRead))]
[JsonSerializable(typeof(HostileBinaryRead.Row), TypeInfoPropertyName = "HostileBinaryReadRow")]
[JsonSerializable(typeof(ClosedEnumLiteralRead))]
[JsonSerializable(typeof(ClosedEnumLiteralRead.Row), TypeInfoPropertyName = "ClosedEnumLiteralReadRow")]
[JsonSerializable(typeof(ModuleGenerationOnlyRead))]
[JsonSerializable(typeof(ModuleGenerationOnlyRead.Row), TypeInfoPropertyName = "ModuleGenerationOnlyReadRow")]
[JsonSerializable(typeof(HostileGenerationRead))]
[JsonSerializable(typeof(HostileGenerationRead.Row), TypeInfoPropertyName = "HostileGenerationReadRow")]
[JsonSerializable(typeof(OptionalGenerationRecord))]
[JsonSerializable(typeof(OptionalGenerationRead))]
[JsonSerializable(typeof(OptionalGenerationRead.Row), TypeInfoPropertyName = "OptionalGenerationReadRow")]
[JsonSerializable(typeof(SpecialScalarRecord))]
[JsonSerializable(typeof(OptionalCanonicalJsonRecord))]
[JsonSerializable(typeof(NullableCanonicalJsonRead))]
[JsonSerializable(typeof(NullableCanonicalJsonRead.Row), TypeInfoPropertyName = "NullableCanonicalJsonReadRow")]
[JsonSerializable(typeof(PresentOptionalCanonicalJsonRead))]
[JsonSerializable(typeof(PresentOptionalCanonicalJsonRead.Row), TypeInfoPropertyName = "PresentOptionalCanonicalJsonReadRow")]
internal sealed partial class SpecialScalarReadJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(OptionalNonNullCanonicalJsonRecord))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    PropertyNameCaseInsensitive = false,
    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
    UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    MaxDepth = 64,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    IgnoreReadOnlyProperties = false,
    IgnoreReadOnlyFields = false,
    IncludeFields = false,
    WriteIndented = false,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    AllowTrailingCommas = false,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Disallow,
    PreferredObjectCreationHandling = System.Text.Json.Serialization.JsonObjectCreationHandling.Replace,
    AllowDuplicateProperties = false,
    AllowOutOfOrderMetadataProperties = false,
    DefaultBufferSize = 16384)]
internal sealed partial class OptionalNonNullCanonicalJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(OptionalNonNullCanonicalJsonRead))]
[JsonSerializable(typeof(OptionalNonNullCanonicalJsonRead.Row), TypeInfoPropertyName = "OptionalNonNullCanonicalJsonReadRow")]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    PropertyNameCaseInsensitive = false,
    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
    UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    MaxDepth = 64,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    IgnoreReadOnlyProperties = false,
    IgnoreReadOnlyFields = false,
    IncludeFields = false,
    WriteIndented = false,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    AllowTrailingCommas = false,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Disallow,
    PreferredObjectCreationHandling = System.Text.Json.Serialization.JsonObjectCreationHandling.Replace,
    AllowDuplicateProperties = false,
    AllowOutOfOrderMetadataProperties = false,
    DefaultBufferSize = 16384)]
internal sealed partial class OptionalNonNullCanonicalJsonReadContext : JsonSerializerContext;
