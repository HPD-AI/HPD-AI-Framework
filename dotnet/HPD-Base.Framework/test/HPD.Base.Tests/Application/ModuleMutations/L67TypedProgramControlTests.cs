using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace HPD.Base.Tests.Application.ModuleMutations;

public sealed class L67TypedProgramControlTests
{
    [Fact]
    public void Static_sets_accept_distinct_request_paths_with_exact_underlying_authority()
    {
        var manifest = new BaseGeneratedModuleScalarManifest(
            BaseModuleValueKind.String, BaseFieldPresence.Required,
            BaseFieldNullability.NonNullable,
            new BaseScalarConstraintSet { MaximumUtf8Bytes = 64 }, null, null);
        BaseModuleRequestProperty<L70StaticSetRequest, string> first =
            manifest.RequestProperty<L70StaticSetRequest, string>("l70.slot00", "l70.id");
        BaseModuleRequestProperty<L70StaticSetRequest, string> second =
            manifest.RequestProperty<L70StaticSetRequest, string>("l70.slot01", "l70.id");

        Action build = () => BaseModuleMutationTemplateBuilder.Disjoint(
            "l70.disjoint",
            BaseModuleMutationTemplateBuilder.ValueSet("l70.left",
                BaseModuleMutationTemplateBuilder.ValueMember("l70.left.00",
                    BaseModuleMutationTemplateBuilder.Request("l70.value.00", first))),
            BaseModuleMutationTemplateBuilder.ValueSet("l70.right",
                BaseModuleMutationTemplateBuilder.ValueMember("l70.right.00",
                    BaseModuleMutationTemplateBuilder.Request("l70.value.01", second))));

        build.Should().NotThrow();

        BaseModuleRequestProperty<L70StaticSetRequest, string> mismatched =
            new BaseGeneratedModuleScalarManifest(
                BaseModuleValueKind.String, BaseFieldPresence.Required,
                BaseFieldNullability.NonNullable,
                new BaseScalarConstraintSet { MaximumUtf8Bytes = 65 }, null, null)
            .RequestProperty<L70StaticSetRequest, string>("l70.slot02", "l70.id");
        Action weaken = () => BaseModuleMutationTemplateBuilder.ValueSet("l70.mismatch",
            BaseModuleMutationTemplateBuilder.ValueMember("l70.mismatch.00",
                BaseModuleMutationTemplateBuilder.Request("l70.mismatch.value.00", first)),
            BaseModuleMutationTemplateBuilder.ValueMember("l70.mismatch.01",
                BaseModuleMutationTemplateBuilder.Request("l70.mismatch.value.01", mismatched)));
        weaken.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Optional_non_null_value_wrappers_finalize_against_generated_authority()
    {
        Action validate = () => BaseModuleMutationContractValidator.ValidateDefinition(
            L68OptionalValueOperation.Definition,
            new Dictionary<string, CollectionDefinition>(),
            new Dictionary<string, BaseModuleGenerationCellDefinition>(),
            new BaseModuleMutationRegistration<L68OptionalValueRequest, L68OptionalValueResult>(
                L68OptionalValueOperation.Definition, L68OptionalValueOperation.Identity));

        validate.Should().NotThrow();
    }

    [Fact]
    public void Required_values_lift_into_exact_optional_request_authority()
    {
        BaseModuleValue<DateTimeOffset> required = BaseModuleMutationTemplateBuilder.Request(
            "l71.request-lift.required", L68OptionalValueOperation.RequestProperties.EventAt);

        BaseModuleValue<DateTimeOffset?> optional = BaseModuleMutationTemplateBuilder.LiftOptional(
            "l71.request-lift.optional", L68OptionalValueOperation.RequestProperties.Instant, required);

        optional.Authority.Presence.Should().Be(BaseFieldPresence.Optional);
        optional.Authority.Nullability.Should().Be(BaseFieldNullability.NonNullable);
        optional.Expression.Should().BeOfType<BaseModulePresenceLiftExpression>();
    }

    [Fact]
    public void Value_type_wrapper_matches_presence_or_nullability_without_conflating_them()
    {
        BaseClosedEnumGeneratedContract.Register<L68ExpectedMode>([L68ExpectedMode.Ready], ["ready"]);
        BaseClosedEnumGeneratedContract.Register<L68WrongMode>([L68WrongMode.Other], ["other"]);
        BaseGeneratedRecordTypeContract.Register<L68WrongOptionalTarget>("l68.wrong-optional-targets");
        BaseModuleValueType optionalInstant = L68OptionalValueOperation.RequestProperties.Instant.Authority.ValueType;
        BaseModuleValueType optionalTarget = L68OptionalValueOperation.RequestProperties.Target.Authority.ValueType;
        BaseModuleValueType requiredInstant = BaseModuleValueAuthorityContract.Primitive<DateTimeOffset>();
        BaseModuleValueType nullableInstant = new BaseGeneratedModuleScalarManifest(
            BaseModuleValueKind.UtcDateTime, BaseFieldPresence.Required, BaseFieldNullability.Nullable,
            new BaseScalarConstraintSet(), null, null).Seal(["l68.nullable.instant"]).ValueType;
        BaseModuleValueType optionalMode = new(
            BaseModuleValueKind.ClosedEnum, BaseFieldPresence.Optional, BaseFieldNullability.NonNullable,
            BaseGeneratedSchemaRegistration.ScalarCodec(BaseScalarKind.ClosedEnum),
            new BaseScalarConstraintSet { AllowedEnumLiterals = ["ready"] }, null, null);

        BaseModuleMutationContractValidator.ClrTypeMatches(typeof(DateTimeOffset?), optionalInstant).Should().BeTrue();
        BaseModuleMutationContractValidator.ClrTypeMatches(typeof(BaseRecordId<L68OptionalTarget>?), optionalTarget).Should().BeTrue();
        BaseModuleMutationContractValidator.ClrTypeMatches(typeof(DateTimeOffset), optionalInstant).Should().BeFalse();
        BaseModuleMutationContractValidator.ClrTypeMatches(typeof(DateTimeOffset?), requiredInstant).Should().BeFalse();
        BaseModuleMutationContractValidator.ClrTypeMatches(typeof(DateTimeOffset?), nullableInstant).Should().BeTrue();
        BaseModuleMutationContractValidator.ClrTypeMatches(typeof(L68ExpectedMode?), optionalMode).Should().BeTrue();
        BaseModuleMutationContractValidator.ClrTypeMatches(typeof(L68WrongMode?), optionalMode).Should().BeFalse();
        BaseModuleMutationContractValidator.ClrTypeMatches(typeof(BaseRecordId<L68WrongOptionalTarget>?), optionalTarget).Should().BeFalse();
        Action liftValueType = () => BaseModuleMutationTemplateBuilder.LiftOptional(
            "l69.optional-instant-lift", L68OptionalTarget.Fields.ProcessedAt.ModuleMutation,
            BaseModuleMutationTemplateBuilder.Request("l69.required-instant", L68OptionalValueOperation.RequestProperties.EventAt));
        liftValueType.Should().NotThrow();
    }

    [Fact]
    public void Wire_validation_preserves_missing_and_rejects_explicit_null_for_optional_non_null_values()
    {
        Action missing = () => BaseModuleProgramEvaluator<L68OptionalValueRequest, L68OptionalValueResult>.ValidateDto(
            "{\"EventAt\":\"2026-08-27T00:00:00.0000000Z\"}"u8, L68OptionalValueOperation.Identity.RequestBindings, providerInfluenced: false);
        Action value = () => BaseModuleProgramEvaluator<L68OptionalValueRequest, L68OptionalValueResult>.ValidateDto(
            "{\"EventAt\":\"2026-08-27T00:00:00.0000000Z\",\"Instant\":\"2026-08-27T00:00:00.0000000Z\",\"Target\":\"target-1\"}"u8,
            L68OptionalValueOperation.Identity.RequestBindings, providerInfluenced: false);
        Action explicitNull = () => BaseModuleProgramEvaluator<L68OptionalValueRequest, L68OptionalValueResult>.ValidateDto(
            "{\"EventAt\":\"2026-08-27T00:00:00.0000000Z\",\"Instant\":null}"u8, L68OptionalValueOperation.Identity.RequestBindings, providerInfluenced: false);
        Action unknown = () => BaseModuleProgramEvaluator<L68OptionalValueRequest, L68OptionalValueResult>.ValidateDto(
            "{\"EventAt\":\"2026-08-27T00:00:00.0000000Z\",\"Unexpected\":1}"u8, L68OptionalValueOperation.Identity.RequestBindings, providerInfluenced: false);

        missing.Should().NotThrow();
        value.Should().NotThrow();
        explicitNull.Should().Throw<BaseModuleScalarContractException>();
        unknown.Should().Throw<BaseModuleScalarContractException>();
    }

    [Fact]
    public void Canonical_request_freeze_preserves_presence_and_normalizes_property_order()
    {
        byte[] left = DefaultBaseModuleMutationRuntime.CanonicalRequest(
            "{\"Target\":\"target-1\",\"Instant\":\"2026-08-27T00:00:00.0000000Z\"}"u8, 4096);
        byte[] right = DefaultBaseModuleMutationRuntime.CanonicalRequest(
            "{ \"Instant\" : \"2026-08-27T00:00:00.0000000Z\", \"Target\" : \"target-1\" }"u8, 4096);

        left.Should().Equal(right);
        System.Text.Encoding.UTF8.GetString(left).Should().Be(
            "{\"Instant\":\"2026-08-27T00:00:00.0000000Z\",\"Target\":\"target-1\"}");
        Action duplicate = () => DefaultBaseModuleMutationRuntime.CanonicalRequest(
            "{\"Target\":\"one\",\"Target\":\"two\"}"u8, 4096);
        duplicate.Should().Throw<FormatException>();
        DefaultBaseModuleMutationRuntime.CanonicalRequest("{}"u8, 2).Should().Equal("{}"u8.ToArray());
        Action overRequestLimit = () => DefaultBaseModuleMutationRuntime.CanonicalRequest("{}"u8, 1);
        overRequestLimit.Should().Throw<FormatException>();
    }

    [Fact]
    public void Typed_optional_non_null_null_is_omitted_by_the_graph_owned_context()
    {
        BaseRecordIdJsonConverterFactory.Register<L68OptionalTarget>();
        byte[] missing = JsonSerializer.SerializeToUtf8Bytes(
            new L68OptionalValueRequest { EventAt = DateTimeOffset.UnixEpoch }, L68OptionalValueJsonContext.Default.L68OptionalValueRequest);
        byte[] present = JsonSerializer.SerializeToUtf8Bytes(
            new L68OptionalValueRequest
            {
                EventAt = DateTimeOffset.UnixEpoch,
                Instant = new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero),
                Target = BaseRecordId<L68OptionalTarget>.Create("target-1"),
            },
            L68OptionalValueJsonContext.Default.L68OptionalValueRequest);

        missing.Should().Equal("{\"EventAt\":\"1970-01-01T00:00:00.0000000Z\"}"u8.ToArray());
        present.Should().Contain((byte)'I');
        Action validateMissing = () => BaseModuleProgramEvaluator<L68OptionalValueRequest, L68OptionalValueResult>.ValidateDto(
            missing, L68OptionalValueOperation.Identity.RequestBindings, providerInfluenced: false);
        Action validatePresent = () => BaseModuleProgramEvaluator<L68OptionalValueRequest, L68OptionalValueResult>.ValidateDto(
            present, L68OptionalValueOperation.Identity.RequestBindings, providerInfluenced: false);
        validateMissing.Should().NotThrow();
        validatePresent.Should().NotThrow();
    }
    [Fact]
    public void Branch_ceiling_accepts_128_and_rejects_129()
    {
        Action accepted = () => Validate(BranchDefinition(NestedBranchChain(128), 128));
        Action rejected = () => Validate(BranchDefinition(NestedBranchChain(129), 128));

        accepted.Should().NotThrow();
        rejected.Should().Throw<InvalidOperationException>()
            .WithMessage(BaseModuleMutationErrorCodes.Invalid);
    }

    [Fact]
    public void Execution_path_ceiling_accepts_8192_and_rejects_8193()
    {
        BaseModuleMutationBlock binaryThirteen = IndependentBranches(13, "accepted");
        Action accepted = () => Validate(BranchDefinition(binaryThirteen, 128));
        BaseModuleMutationBlock plusOne = new()
        {
            Statements = [Branch("outer", binaryThirteen, RequiredBlock("outer-false"))],
        };
        Action rejected = () => Validate(BranchDefinition(plusOne, 128));

        accepted.Should().NotThrow();
        rejected.Should().Throw<InvalidOperationException>()
            .WithMessage(BaseModuleMutationErrorCodes.Invalid);
    }

    [Fact]
    public void Guid_generation_key_uses_exact_canonical_D_wire()
    {
        BaseModuleValue<Guid> source = Value<Guid>("guid", "\"00112233-4455-6677-8899-aabbccddeeff\""u8);
        BaseModuleGenerationKey key = BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid("key", source);
        var evaluator = Evaluator(Definition([], []), Limits());

        BaseModuleProgramValue actual = evaluator.Evaluate(key.Expression);

        actual.Present.Should().BeTrue();
        actual.Value.GetString().Should().Be("00112233-4455-6677-8899-aabbccddeeff");
        key.Expression.ResultType!.Constraints!.MinimumUtf8Bytes.Should().Be(36);
        key.Expression.ResultType.Constraints.MaximumUtf8Bytes.Should().Be(36);
    }

    [Theory]
    [InlineData("\"00112233-4455-6677-8899-AABBCCDDEEFF\"")]
    [InlineData("\"{00112233-4455-6677-8899-aabbccddeeff}\"")]
    [InlineData("\"00112233445566778899aabbccddeeff\"")]
    public void Guid_generation_key_rejects_every_alternate_spelling(string json)
    {
        BaseModuleValue<Guid> source = Value<Guid>("guid", System.Text.Encoding.UTF8.GetBytes(json));
        BaseModuleGenerationKey key = BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid("key", source);
        var evaluator = Evaluator(Definition([], []), Limits());

        Action act = () => evaluator.Evaluate(key.Expression);

        act.Should().Throw<BaseModuleScalarContractException>()
            .Which.ProviderInfluenced.Should().BeFalse();
    }

    [Fact]
    public void Generation_capture_validates_key_kind_cell_scope_and_cell_bound()
    {
        BaseModuleValue<Guid> guid = Value<Guid>("guid", "\"00112233-4455-6677-8899-aabbccddeeff\""u8);
        BaseModuleGenerationKey key = BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid("key", guid);
        BaseModuleGenerationCapture capture = BaseModuleMutationTemplateBuilder.CaptureGeneration(
            "capture", "cell", key, BaseModuleGenerationAbsenceBehavior.AllowEither);
        BaseRegisteredModuleMutationDefinition definition = SealGeneration(capture);

        Action valid = () => Validate(definition, Cell(BaseModuleGenerationScope.TenantAndKey, 36));
        Action tooNarrow = () => Validate(definition, Cell(BaseModuleGenerationScope.TenantAndKey, 35));
        Action unkeyed = () => Validate(definition, Cell(BaseModuleGenerationScope.Tenant, 36));

        valid.Should().NotThrow();
        tooNarrow.Should().Throw<InvalidOperationException>().WithMessage(BaseModuleMutationErrorCodes.Invalid);
        unkeyed.Should().Throw<InvalidOperationException>().WithMessage(BaseModuleMutationErrorCodes.Invalid);
    }

    [Theory]
    [InlineData(BaseModuleValueKind.Int32, "1", "2")]
    [InlineData(BaseModuleValueKind.Int64, "1", "2")]
    [InlineData(BaseModuleValueKind.UInt32, "1", "2")]
    [InlineData(BaseModuleValueKind.UInt64, "1", "2")]
    [InlineData(BaseModuleValueKind.Decimal, "1", "2")]
    [InlineData(BaseModuleValueKind.Guid, "\"00112233-4455-6677-8899-aabbccddeeff\"", "\"10112233-4455-6677-8899-aabbccddeeff\"")]
    [InlineData(BaseModuleValueKind.UtcDateTime, "\"2026-01-01T00:00:00.0000000Z\"", "\"2026-01-02T00:00:00.0000000Z\"")]
    [InlineData(BaseModuleValueKind.String, "\"a\"", "\"b\"")]
    public void Every_admitted_ordered_kind_has_exact_truth_table(
        BaseModuleValueKind kind, string lowerJson, string upperJson)
    {
        BaseModuleValueType type = Type(kind);
        BaseModuleConstantExpression lower = Constant("lower", type, lowerJson);
        BaseModuleConstantExpression upper = Constant("upper", type, upperJson);
        BaseModuleGuard[] guards =
        [
            Compare("lt", lower, BaseModuleOrderedComparisonKind.LessThan, upper),
            Compare("lte", lower, BaseModuleOrderedComparisonKind.LessThanOrEqual, upper),
            Compare("gt", upper, BaseModuleOrderedComparisonKind.GreaterThan, lower),
            Compare("gte", upper, BaseModuleOrderedComparisonKind.GreaterThanOrEqual, lower),
            Equal("same", lower, lower),
            Equal("different", lower, upper),
        ];
        var evaluator = Evaluator(Definition(guards, []), Limits());

        evaluator.Guard("lt").Should().BeTrue();
        evaluator.Guard("lte").Should().BeTrue();
        evaluator.Guard("gt").Should().BeTrue();
        evaluator.Guard("gte").Should().BeTrue();
        evaluator.Guard("same").Should().BeTrue();
        evaluator.Guard("different").Should().BeFalse();
    }

    [Fact]
    public void Missing_null_and_present_value_are_three_distinct_states()
    {
        BaseModuleValueType type = BaseModuleValueAuthorityContract.Primitive<string>(
            presence: BaseFieldPresence.Optional, nullability: BaseFieldNullability.Nullable);
        var missing = new BaseModuleMissingExpression { Id = "missing", ResultType = type };
        BaseModuleConstantExpression nullValue = Constant("null", type, "null");
        BaseModuleConstantExpression present = Constant("present", type, "\"value\"");
        BaseModuleGuard[] guards =
        [
            Presence("missing-test", missing, BaseModuleFieldPresenceTest.Missing),
            Presence("null-test", nullValue, BaseModuleFieldPresenceTest.Null),
            Presence("value-test", present, BaseModuleFieldPresenceTest.PresentValue),
            Equal("missing-null", missing, nullValue),
        ];
        var evaluator = Evaluator(Definition(guards, []), Limits());

        evaluator.Guard("missing-test").Should().BeTrue();
        evaluator.Guard("null-test").Should().BeTrue();
        evaluator.Guard("value-test").Should().BeTrue();
        evaluator.Guard("missing-null").Should().BeFalse();
    }

    [Fact]
    public void Missing_null_and_value_result_bytes_remain_distinct_through_receipt_replay()
    {
        BaseModuleValueType type = BaseModuleValueAuthorityContract.Primitive<string>(
            presence: BaseFieldPresence.Optional, nullability: BaseFieldNullability.Nullable);
        BaseModuleValueExpression[] values =
        [
            new BaseModuleMissingExpression { Id = "missing", ResultType = type },
            Constant("null", type, "null"),
            Constant("value", type, "\"present\""),
        ];
        byte[][] expected = ["{}"u8.ToArray(), "{\"Value\":null}"u8.ToArray(), "{\"Value\":\"present\"}"u8.ToArray()];

        for (int index = 0; index < values.Length; index++)
        {
            BaseRegisteredModuleMutationDefinition definition = Definition([], []) with
            {
                Template = Definition([], []).Template with
                {
                    Result = BaseModuleMutationTemplateBuilder.ResultRaw(
                        BaseModuleMutationTemplateBuilder.Object($"result-{index}",
                            BaseModuleMutationTemplateBuilder.Property("result.value", values[index]))),
                },
            };
            var identity = new BaseGeneratedModuleMutationIdentity<L67ControlRequest, L67OptionalResult>(
                definition.Id,
                definition.Version,
                definition.Checksum.ToArray(),
                L67ControlJsonContext.Default.L67ControlRequest,
                L67ControlJsonContext.Default.L67OptionalResult,
                [],
                [BaseModuleDtoPropertyBinding.Create<L67OptionalResult, string?>(
                    "result.value",
                    nameof(L67OptionalResult.Value),
                    BaseGeneratedModuleScalarManifest.Primitive<string>(
                        presence: BaseFieldPresence.Optional,
                        nullability: BaseFieldNullability.Nullable))]);
            var evaluator = new BaseModuleProgramEvaluator<L67ControlRequest, L67OptionalResult>(
                definition,
                identity,
                new L67ControlRequest(),
                null,
                new Dictionary<string, CollectionDefinition>(),
                Limits());

            L67OptionalResult result = evaluator.ProjectResult(
                definition.Template.Result,
                new Dictionary<string, BaseRecordMutationFact>(),
                new Dictionary<string, BaseModuleCommittedGeneration>(),
                out ImmutableArray<byte> canonicalBytes);
            var receipt = new BaseAtomicReceiptResult
            {
                Kind = BaseAtomicReceiptResultKind.ModuleMutation,
                Mutations = [],
                ModuleMutation = new BaseModuleMutationReceiptResult
                {
                    OperationId = definition.Id,
                    OperationVersion = definition.Version,
                    Disposition = BaseMutationRequestDisposition.Committed,
                    Outcome = BaseModuleMutationOutcome.Committed,
                    Generations = [],
                    CanonicalResultBytes = canonicalBytes,
                },
            };
            byte[] wire = JsonSerializer.SerializeToUtf8Bytes(
                BaseAtomicReceiptWire.From(receipt),
                HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
            BaseAtomicReceiptResult replayed = JsonSerializer.Deserialize(
                wire,
                HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire)!.Materialize();

            canonicalBytes.Should().Equal(expected[index]);
            replayed.ModuleMutation!.CanonicalResultBytes.Should().Equal(expected[index]);
            result.Value.Should().Be(index == 2 ? "present" : null);
        }
    }

    [Fact]
    public void Coalesce_consumes_missing_but_never_consumes_explicit_null()
    {
        BaseModuleValueType type = BaseModuleValueAuthorityContract.Primitive<string>(
            presence: BaseFieldPresence.Optional, nullability: BaseFieldNullability.Nullable);
        var missing = new BaseModuleMissingExpression { Id = "missing", ResultType = type };
        BaseModuleConstantExpression nullValue = Constant("null", type, "null");
        BaseModuleConstantExpression fallback = Constant("fallback", type, "\"fallback\"");
        var evaluator = Evaluator(Definition([], []), Limits());

        BaseModuleProgramValue fromMissing = evaluator.Evaluate(new BaseModuleCoalesceExpression
        {
            Id = "from-missing", ResultType = type, Values = [missing, fallback],
        });
        BaseModuleProgramValue fromNull = evaluator.Evaluate(new BaseModuleCoalesceExpression
        {
            Id = "from-null", ResultType = type, Values = [nullValue, fallback],
        });

        fromMissing.Value.GetString().Should().Be("fallback");
        fromNull.Present.Should().BeTrue();
        fromNull.IsNull.Should().BeTrue();
    }

    [Fact]
    public void Module_capability_identity_covers_every_L67A_limit_and_direct_removal_maximum()
    {
        var source = new BaseModuleMutationCapability
        {
            Supported = true,
            SerializableExecution = true,
            DurableReceipts = true,
            GenerationCells = true,
            AtomicRecordAndGenerationCommit = true,
            MaximumRemovedFieldsPerMutation = 256,
            MaximumLimits = Limits(),
        };
        ImmutableArray<byte> baseline = BaseSemanticActivationCertificationContract
            .ModuleMutationCapabilityChecksum(source);
        BaseModuleMutationLimits limits = source.MaximumLimits;
        BaseModuleMutationLimits[] variants =
        [
            limits with { MaximumPreconditions = limits.MaximumPreconditions - 1 },
            limits with { MaximumRequestGuardEvaluations = limits.MaximumRequestGuardEvaluations - 1 },
            limits with { MaximumStaticSetMembers = limits.MaximumStaticSetMembers - 1 },
            limits with { MaximumStaticSetComparisons = limits.MaximumStaticSetComparisons - 1 },
            limits with { MaximumDisabledCaptures = limits.MaximumDisabledCaptures - 1 },
            limits with { MaximumRemovedFields = limits.MaximumRemovedFields - 1 },
        ];

        foreach (BaseModuleMutationLimits variant in variants)
            BaseSemanticActivationCertificationContract.ModuleMutationCapabilityChecksum(
                source with { MaximumLimits = variant }).Should().NotEqual(baseline);
        BaseSemanticActivationCertificationContract.ModuleMutationCapabilityChecksum(
            source with { MaximumRemovedFieldsPerMutation = 255 }).Should().NotEqual(baseline);
    }

    [Fact]
    public void Static_set_predicates_evaluate_all_comparisons_in_the_declared_order()
    {
        BaseModuleValueType type = Type(BaseModuleValueKind.Int64);
        BaseModuleStaticSet set = Set("set", type, "1", "1", "2");
        BaseModuleSetGuard distinct = new()
        {
            Id = "distinct", Predicate = BaseModuleStaticSetPredicateKind.AllDistinct, Left = set,
        };
        BaseModuleMutationLimits limits = Limits() with { MaximumStaticSetComparisons = 3 };
        var evaluator = Evaluator(Definition([distinct], []), limits);

        evaluator.Guard("distinct").Should().BeFalse();

        var limited = Evaluator(Definition([distinct], []), limits with { MaximumStaticSetComparisons = 2 });
        Action act = () => limited.Guard("distinct");
        act.Should().Throw<BaseModuleRequestLimitException>();
    }

    [Fact]
    public void Static_set_empty_singleton_and_disjoint_boundaries_are_exact()
    {
        BaseModuleValueType type = Type(BaseModuleValueKind.Int64);
        BaseModuleSetGuard empty = new()
        {
            Id = "empty", Predicate = BaseModuleStaticSetPredicateKind.AllDistinct,
            Left = Set("empty-set", type),
        };
        BaseModuleSetGuard singleton = new()
        {
            Id = "singleton", Predicate = BaseModuleStaticSetPredicateKind.StrictlyIncreasing,
            Left = Set("singleton-set", type, "1"),
        };
        BaseModuleSetGuard disjoint = new()
        {
            Id = "disjoint", Predicate = BaseModuleStaticSetPredicateKind.Disjoint,
            Left = Set("left", type, "1", "2"), Right = Set("right", type, "3", "4"),
        };
        var evaluator = Evaluator(Definition([empty, singleton, disjoint], []), Limits());

        evaluator.Guard("empty").Should().BeTrue();
        evaluator.Guard("singleton").Should().BeTrue();
        evaluator.Guard("disjoint").Should().BeTrue();
    }

    [Fact]
    public void Guard_cycles_through_conditional_expressions_are_rejected()
    {
        BaseModuleValueType type = Type(BaseModuleValueKind.Int64);
        BaseModuleConstantExpression one = Constant("one", type, "1");
        var conditional = new BaseModuleConditionalExpression
        {
            Id = "conditional", ResultType = type, GuardId = "cycle-b",
            WhenTrue = one, WhenFalse = Constant("zero", type, "0"),
        };
        BaseModuleGuard[] guards =
        [
            Equal("cycle-a", conditional, one),
            new BaseModuleLogicalGuard
            {
                Id = "cycle-b", Kind = BaseModuleLogicalGuardKind.Not, ChildGuardIds = ["cycle-a"],
            },
        ];
        BaseRegisteredModuleMutationDefinition definition = BaseModuleMutationContract.Seal(
            Definition(guards, [new BaseModulePrecondition
            {
                Id = "precondition", GuardId = "cycle-a", RequirementId = "cycle",
            }]));

        Action act = () => Validate(definition);

        act.Should().Throw<InvalidOperationException>().WithMessage(BaseModuleMutationErrorCodes.Invalid);
    }

    [Fact]
    public void Static_set_maximum_plus_one_is_rejected_during_graph_finalization()
    {
        BaseModuleValueType type = Type(BaseModuleValueKind.Int64);
        string[] values = Enumerable.Range(0, 257).Select(static value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        BaseModuleSetGuard guard = new()
        {
            Id = "set-guard", Predicate = BaseModuleStaticSetPredicateKind.AllDistinct,
            Left = Set("set", type, values),
        };
        BaseRegisteredModuleMutationDefinition definition = BaseModuleMutationContract.Seal(Definition([guard], []));

        Action act = () => Validate(definition);

        act.Should().Throw<InvalidOperationException>().WithMessage(BaseModuleMutationErrorCodes.Invalid);
    }

    [Fact]
    public void Static_set_exact_platform_maximum_is_admitted_and_evaluated_once()
    {
        BaseModuleValueType type = Type(BaseModuleValueKind.Int64);
        string[] values = Enumerable.Range(0, 256)
            .Select(static value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        BaseModuleSetGuard guard = new()
        {
            Id = "set-guard", Predicate = BaseModuleStaticSetPredicateKind.AllDistinct,
            Left = Set("set", type, values),
        };
        BaseRegisteredModuleMutationDefinition definition = BaseModuleMutationContract.Seal(Definition([guard], []));

        Action validate = () => Validate(definition);
        var evaluator = Evaluator(definition, Limits());

        validate.Should().NotThrow();
        evaluator.Guard("set-guard").Should().BeTrue();
    }

    private static BaseModuleValue<T> Value<T>(string id, ReadOnlySpan<byte> json) => new(
        new BaseModuleConstantExpression
        {
            Id = id,
            ResultType = BaseModuleValueAuthorityContract.Primitive<T>(),
            CanonicalBaseJson = json.ToArray().ToImmutableArray(),
        });

    private static BaseModuleProgramEvaluator<L67ControlRequest, L67ControlResult> Evaluator(
        BaseRegisteredModuleMutationDefinition definition, BaseModuleMutationLimits limits) => new(
            definition,
            Identity(definition),
            new L67ControlRequest(),
            null,
            new Dictionary<string, CollectionDefinition>(),
            limits);

    private static BaseGeneratedModuleMutationIdentity<L67ControlRequest, L67ControlResult> Identity(
        BaseRegisteredModuleMutationDefinition definition) => new(
            definition.Id,
            definition.Version,
            definition.Checksum.ToArray(),
            L67ControlJsonContext.Default.L67ControlRequest,
            L67ControlJsonContext.Default.L67ControlResult,
            [],
            [BaseModuleDtoPropertyBinding.Create<L67ControlResult, long>(
                "result.value", nameof(L67ControlResult.Value), BaseGeneratedModuleScalarManifest.Primitive<long>())]);

    private static BaseRegisteredModuleMutationDefinition Definition(
        BaseModuleGuard[] guards,
        BaseModulePrecondition[] preconditions) => new()
        {
            Id = "module.l67-control",
            Version = 1,
            OwningModuleId = "module",
            GrantId = "module.l67-control",
            Audience = BaseModuleMutationAudience.System,
            RequestTypeId = "module.l67-control.request.v1",
            ResultTypeId = "module.l67-control.result.v1",
            SystemCollectionIds = [],
            SystemSourceGrants = [],
            GenerationCellIds = [],
            ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [],
                Guards = [.. guards.OrderBy(static value => value.Id, StringComparer.Ordinal)],
                Preconditions = [.. preconditions.OrderBy(static value => value.Id, StringComparer.Ordinal)],
                Body = BaseModuleMutationTemplateBuilder.Block(
                    BaseModuleMutationTemplateBuilder.Require("require", guards.FirstOrDefault()?.Id ?? "unused", "requirement")),
                Result = BaseModuleMutationTemplateBuilder.ResultRaw(
                    BaseModuleMutationTemplateBuilder.Object("result",
                        BaseModuleMutationTemplateBuilder.Property("result.value",
                            Constant("result-value", Type(BaseModuleValueKind.Int64), "1")))),
            },
            Limits = Limits(),
            ReceiptPolicy = new BaseModuleMutationReceiptPolicy
            {
                FormatVersion = 1,
                Lifetime = TimeSpan.FromHours(24),
            },
            Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
        };

    private static BaseRegisteredModuleMutationDefinition BranchDefinition(
        BaseModuleMutationBlock body, int maximumBranches)
    {
        BaseModuleStatement[] statements = Flatten(body).ToArray();
        BaseModuleIfStatement[] branches = statements.OfType<BaseModuleIfStatement>().ToArray();
        BaseModuleGuard[] guards =
        [
            .. branches.Select(branch => EqualityGuard(branch.GuardId)),
            .. statements.OfType<BaseModuleRequireStatement>().Select(require => EqualityGuard(require.GuardId)),
        ];
        BaseRegisteredModuleMutationDefinition definition = Definition(guards, []);
        return BaseModuleMutationContract.Seal(definition with
        {
            Template = definition.Template with { Body = body },
            Limits = definition.Limits with
            {
                MaximumBranches = maximumBranches,
                MaximumStatements = 512,
                MaximumGuardNodes = 512,
                MaximumExpressionNodes = 1024,
            },
        });
    }

    private static BaseModuleMutationBlock NestedBranchChain(int count)
    {
        BaseModuleMutationBlock current = RequiredBlock($"terminal-{count:D3}");
        for (int index = count - 1; index >= 0; index--)
            current = new BaseModuleMutationBlock
            {
                Statements = [Branch($"nested-{index:D3}", current, RequiredBlock($"nested-false-{index:D3}"))],
            };
        return current;
    }

    private static BaseModuleMutationBlock IndependentBranches(int count, string prefix) => new()
    {
        Statements = [.. Enumerable.Range(0, count).Select(index => Branch(
            $"{prefix}-{index:D3}", RequiredBlock($"{prefix}-true-{index:D3}"),
            RequiredBlock($"{prefix}-false-{index:D3}")))],
    };

    private static BaseModuleIfStatement Branch(
        string id, BaseModuleMutationBlock whenTrue, BaseModuleMutationBlock whenFalse) => new()
    {
        Id = id, GuardId = id + "-guard", WhenTrue = whenTrue, WhenFalse = whenFalse,
    };

    private static BaseModuleMutationBlock RequiredBlock(string id) => new()
    {
        Statements = [new BaseModuleRequireStatement
        {
            Id = id, GuardId = id + "-require-guard", RequirementId = id + "-requirement",
        }],
    };

    private static BaseModuleValueEqualsGuard EqualityGuard(string id) => new()
    {
        Id = id,
        Left = Constant(id + "-left", Type(BaseModuleValueKind.Boolean), "true"),
        Right = Constant(id + "-right", Type(BaseModuleValueKind.Boolean), "true"),
    };

    private static IEnumerable<BaseModuleStatement> Flatten(BaseModuleMutationBlock block)
    {
        foreach (BaseModuleStatement statement in block.Statements)
        {
            yield return statement;
            if (statement is not BaseModuleIfStatement branch) continue;
            foreach (BaseModuleStatement nested in Flatten(branch.WhenTrue)) yield return nested;
            foreach (BaseModuleStatement nested in Flatten(branch.WhenFalse)) yield return nested;
        }
    }

    private static BaseRegisteredModuleMutationDefinition SealGeneration(BaseModuleGenerationCapture capture)
    {
        BaseModuleValueType generationType = BaseModuleValueAuthorityContract.Primitive<BaseModuleGeneration>();
        return BaseModuleMutationContract.Seal(new BaseRegisteredModuleMutationDefinition
        {
            Id = "module.l67-generation",
            Version = 1,
            OwningModuleId = "module",
            GrantId = "module.l67-generation",
            Audience = BaseModuleMutationAudience.System,
            RequestTypeId = "module.l67-generation.request.v1",
            ResultTypeId = "module.l67-generation.result.v1",
            SystemCollectionIds = [],
            SystemSourceGrants = [],
            GenerationCellIds = ["cell"],
            ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [capture],
                Guards = [],
                Preconditions = [],
                Body = BaseModuleMutationTemplateBuilder.Block(
                    BaseModuleMutationTemplateBuilder.IncrementGeneration("increment", capture.Id, true)),
                Result = BaseModuleMutationTemplateBuilder.ResultRaw(
                    BaseModuleMutationTemplateBuilder.Object("result",
                        BaseModuleMutationTemplateBuilder.Property("result.value",
                            BaseModuleMutationTemplateBuilder.ResultingGenerationRaw(
                                "result-value", capture.Id)))),
            },
            Limits = Limits(),
            ReceiptPolicy = new BaseModuleMutationReceiptPolicy
            {
                FormatVersion = 1,
                Lifetime = TimeSpan.FromHours(24),
            },
            Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
        });
    }

    private static BaseModuleGenerationCellDefinition Cell(BaseModuleGenerationScope scope, int maximumBytes) => new()
    {
        Id = "cell",
        Version = 1,
        OwningModuleId = "module",
        Scope = scope,
        MaximumKeyUtf8Bytes = maximumBytes,
        MaximumCellsPerOperation = 1,
    };

    private static void Validate(
        BaseRegisteredModuleMutationDefinition definition,
        BaseModuleGenerationCellDefinition? cell = null) =>
        BaseModuleMutationContractValidator.ValidateDefinition(
            definition,
            new Dictionary<string, CollectionDefinition>(),
            cell is null
                ? new Dictionary<string, BaseModuleGenerationCellDefinition>()
                : new Dictionary<string, BaseModuleGenerationCellDefinition> { [cell.Id] = cell });

    private static BaseModuleValueType Type(BaseModuleValueKind kind) => kind switch
    {
        BaseModuleValueKind.Int32 => BaseModuleValueAuthorityContract.Primitive<int>(),
        BaseModuleValueKind.Int64 => BaseModuleValueAuthorityContract.Primitive<long>(),
        BaseModuleValueKind.UInt32 => BaseModuleValueAuthorityContract.Primitive<uint>(),
        BaseModuleValueKind.UInt64 => BaseModuleValueAuthorityContract.Primitive<ulong>(),
        BaseModuleValueKind.Decimal => BaseModuleValueAuthorityContract.Primitive<decimal>(),
        BaseModuleValueKind.Guid => BaseModuleValueAuthorityContract.Primitive<Guid>(),
        BaseModuleValueKind.UtcDateTime => BaseModuleValueAuthorityContract.Primitive<DateTimeOffset>(),
        BaseModuleValueKind.String => BaseModuleValueAuthorityContract.Primitive<string>(),
        BaseModuleValueKind.Boolean => BaseModuleValueAuthorityContract.Primitive<bool>(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static BaseModuleConstantExpression Constant(
        string id, BaseModuleValueType type, string json) => new()
        {
            Id = id,
            ResultType = type,
            CanonicalBaseJson = System.Text.Encoding.UTF8.GetBytes(json).ToImmutableArray(),
        };

    private static BaseModuleValueEqualsGuard Equal(
        string id, BaseModuleValueExpression left, BaseModuleValueExpression right) => new()
        {
            Id = id,
            Left = left,
            Right = right,
        };

    private static BaseModuleValueComparisonGuard Compare(
        string id,
        BaseModuleValueExpression left,
        BaseModuleOrderedComparisonKind comparison,
        BaseModuleValueExpression right) => new()
        {
            Id = id,
            Left = left,
            Comparison = comparison,
            Right = right,
        };

    private static BaseModuleValuePresenceGuard Presence(
        string id, BaseModuleValueExpression value, BaseModuleFieldPresenceTest test) => new()
        {
            Id = id,
            Value = value,
            Test = test,
        };

    private static BaseModuleStaticSet Set(
        string id, BaseModuleValueType type, params string[] values) => new()
        {
            Id = id,
            ElementType = type,
            Members = [.. values.Select((value, index) => new BaseModuleStaticSetMember
            {
                Id = $"{id}.member-{index:D3}",
                Value = Constant($"{id}.value-{index:D3}", type, value),
            })],
        };

    private static BaseModuleMutationLimits Limits() => new()
    {
        MaximumCaptures = 256,
        MaximumRecordCaptures = 256,
        MaximumRelationTargetCaptures = 512,
        MaximumGenerationCaptures = 128,
        MaximumRecordMutations = 256,
        MaximumGenerationReads = 128,
        MaximumGenerationComparisons = 128,
        MaximumGenerationIncrements = 128,
        MaximumGuardNodes = 1024,
        MaximumGuardDepth = 32,
        MaximumStatements = 512,
        MaximumBranches = 64,
        MaximumExpressionNodes = 2048,
        MaximumPreconditions = 256,
        MaximumRequestGuardEvaluations = 8192,
        MaximumStaticSetMembers = 512,
        MaximumStaticSetComparisons = 131072,
        MaximumDisabledCaptures = 512,
        MaximumRemovedFields = 256,
        MaximumReadIntervals = 1024,
        MaximumSubjectValidations = 512,
        MaximumAuthorityReads = 2048,
        MaximumRelationChecks = 4096,
        MaximumUniqueConstraintChecks = 4096,
        MaximumRequestBytes = 1_048_576,
        MaximumSelectedBytes = 16_777_216,
        MaximumGenerationBytes = 1_048_576,
        MaximumEvidenceBytes = 8_388_608,
        MaximumWrittenBytes = 16_777_216,
        MaximumFactBytes = 16_777_216,
        MaximumJournalBytes = 16_777_216,
        MaximumReceiptBytes = 16_777_216,
        MaximumResultBytes = 1_048_576,
        MaximumTransientBytes = 32_000_000,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5),
            TransactionTimeout = TimeSpan.FromSeconds(30),
            CommitObservationTimeout = TimeSpan.FromSeconds(30),
            ReceiptResolutionTimeout = TimeSpan.FromSeconds(30),
        },
    };
}

internal sealed record L70StaticSetRequest;

public sealed record L67ControlRequest;

public sealed record L67ControlResult
{
    public required long Value { get; init; }
}

public sealed record L67OptionalResult
{
    public string? Value { get; init; }
}

[JsonSerializable(typeof(L67ControlRequest))]
[JsonSerializable(typeof(L67ControlResult))]
[JsonSerializable(typeof(L67OptionalResult))]
internal sealed partial class L67ControlJsonContext : JsonSerializerContext;

[BaseCollection("l68.optional-targets", typeof(L68OptionalValueJsonContext), Name = "optionalTargets")]
internal sealed partial record L68OptionalTarget
{
    [BaseField("l68.optional-targets.id", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 64)]
    public required string Id { get; init; }

    [BaseField("l68.optional-targets.processedAt", Presence = BaseFieldPresence.Optional,
        Nullability = BaseFieldNullability.NonNullable)]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public DateTimeOffset? ProcessedAt { get; init; }
}

internal sealed record L68WrongOptionalTarget;

internal enum L68ExpectedMode { Ready }
internal enum L68WrongMode { Other }

internal sealed record L68OptionalValueRequest
{
    [BaseField("l68.optional.request.eventAt")]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset EventAt { get; init; }
    [BaseField("l68.optional.request.instant", Presence = BaseFieldPresence.Optional,
        Nullability = BaseFieldNullability.NonNullable)]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public DateTimeOffset? Instant { get; init; }

    [BaseField("l68.optional.request.target", Presence = BaseFieldPresence.Optional,
        Nullability = BaseFieldNullability.NonNullable)]
    public BaseRecordId<L68OptionalTarget>? Target { get; init; }
}

internal sealed record L68OptionalValueResult
{
    [BaseField("l68.optional.result.accepted")]
    public required bool Accepted { get; init; }
}

[BaseRegisteredModuleMutation("l68.optional-values.v1", typeof(L68OptionalValueJsonContext),
    typeof(L68OptionalValueRequest), typeof(L68OptionalValueResult), Version = 1,
    OwningModuleId = "l68.optional", GrantId = "l68.optional.execute")]
internal static partial class L68OptionalValueOperation
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } =
        BaseModuleMutationContract.Seal(new BaseRegisteredModuleMutationDefinition
        {
            Id = "l68.optional-values.v1", Version = 1, OwningModuleId = "l68.optional",
            GrantId = "l68.optional.execute", Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "l68.optional.request.v1", ResultTypeId = "l68.optional.result.v1",
            SystemCollectionIds = [], SystemSourceGrants = [], GenerationCellIds = [],
            ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [],
                Guards =
                [
                    BaseModuleMutationTemplateBuilder.ValuePresence("l68.guard.instant",
                        BaseModuleMutationTemplateBuilder.Request("l68.expression.instant", RequestProperties.Instant),
                        BaseModuleFieldPresenceTest.PresentValue),
                    BaseModuleMutationTemplateBuilder.ValuePresence("l68.guard.target",
                        BaseModuleMutationTemplateBuilder.Request("l68.expression.target", RequestProperties.Target),
                        BaseModuleFieldPresenceTest.PresentValue),
                ],
                Preconditions =
                [
                    BaseModuleMutationTemplateBuilder.Precondition("l68.precondition.instant", "l68.guard.instant", "l68.optional.instantRequired"),
                    BaseModuleMutationTemplateBuilder.Precondition("l68.precondition.target", "l68.guard.target", "l68.optional.targetRequired"),
                ],
                Body = BaseModuleMutationTemplateBuilder.Block(
                    BaseModuleMutationTemplateBuilder.Require("l68.statement.require", "l68.guard.target", "l68.optional.targetRequired")),
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.ResultObject("l68.expression.result",
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.Accepted,
                            BaseModuleMutationTemplateBuilder.Constant("l68.expression.accepted",
                                ResultProperties.Accepted.ConstantAuthority, true)))),
            },
            Limits = new BaseModuleMutationLimits
            {
                MaximumCaptures = 1, MaximumRecordCaptures = 1, MaximumRelationTargetCaptures = 1,
                MaximumGenerationCaptures = 1, MaximumRecordMutations = 1, MaximumGenerationReads = 1,
                MaximumGenerationComparisons = 1, MaximumGenerationIncrements = 1, MaximumGuardNodes = 4,
                MaximumGuardDepth = 4, MaximumPreconditions = 2, MaximumRequestGuardEvaluations = 4,
                MaximumStaticSetMembers = 1, MaximumStaticSetComparisons = 1, MaximumDisabledCaptures = 1,
                MaximumRemovedFields = 1, MaximumStatements = 1, MaximumBranches = 1,
                MaximumExpressionNodes = 16, MaximumReadIntervals = 1, MaximumSubjectValidations = 1,
                MaximumAuthorityReads = 1, MaximumRelationChecks = 1, MaximumUniqueConstraintChecks = 1,
                MaximumRequestBytes = 4096, MaximumSelectedBytes = 4096, MaximumGenerationBytes = 4096,
                MaximumEvidenceBytes = 4096, MaximumWrittenBytes = 4096, MaximumFactBytes = 4096,
                MaximumJournalBytes = 4096, MaximumReceiptBytes = 4096, MaximumResultBytes = 4096,
                MaximumTransientBytes = 16384,
                Deadlines = new BaseAtomicMutationDeadlines
                {
                    AcquisitionTimeout = TimeSpan.FromSeconds(1), TransactionTimeout = TimeSpan.FromSeconds(1),
                    CommitObservationTimeout = TimeSpan.FromSeconds(1), ReceiptResolutionTimeout = TimeSpan.FromSeconds(1),
                },
            },
            ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromHours(1) },
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });
}

[JsonSerializable(typeof(L68OptionalTarget))]
[JsonSerializable(typeof(BaseRecordId<L68OptionalTarget>))]
[JsonSerializable(typeof(L68OptionalValueRequest))]
[JsonSerializable(typeof(L68OptionalValueResult))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class L68OptionalValueJsonContext : JsonSerializerContext;
