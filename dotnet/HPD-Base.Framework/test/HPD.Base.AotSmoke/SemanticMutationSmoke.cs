using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace HPD.Base.AotSmoke;

[BaseRegisteredModuleMutation("hpd.base.aot.semantic.ensure-operation", typeof(SemanticMutationSmokeJsonContext), typeof(SemanticMutationSmokeRequest), typeof(SemanticEnsureSmokeResult), Version = 1, OwningModuleId = "hpd.base.aot", GrantId = "hpd.base.aot.semantic.ensure-operation")]
public static partial class SemanticEnsureMutationSmoke
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = DefinitionFor(
        "hpd.base.aot.semantic.ensure-operation", "hpd.base.aot.semantic.ensure-operation", ensure: true,
        System.Security.Cryptography.SHA256.HashData("hpd.base.aot.semantic.ensure-operation.v1"u8));

    internal static BaseRegisteredModuleMutationDefinition DefinitionFor(string id, string grant, bool ensure, byte[] checksum) =>
        BaseModuleMutationContract.Seal(new BaseRegisteredModuleMutationDefinition
        {
            Id = id, Version = 1, OwningModuleId = "hpd.base.aot", GrantId = grant,
            Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.base.aot.semantic.request",
            ResultTypeId = ensure ? "hpd.base.aot.semantic.ensure-result" : "hpd.base.aot.semantic.retire-result",
            SystemCollectionIds = [], SystemSourceGrants = [], GenerationCellIds = [], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [], Guards = SemanticGuards(), Preconditions = [], Body = SemanticBody(),
                Result = new BaseModuleResultProjection
                {
                    Value = ensure ? EnsureResult() : RetireResult(),
                },
            },
            Limits = ModuleMutationSmoke.Definition.Limits,
            ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
            Checksum = BaseModuleMutationChecksum.Create(checksum),
        });

    private static ImmutableArray<BaseModuleGuard> SemanticGuards() =>
    [
        new BaseModuleSemanticActivationStateGuard { Id = "semantic-absent", Test = BaseModuleSemanticActivationStateTest.CompactedAbsent },
        new BaseModuleSemanticActivationStateGuard { Id = "semantic-live", Test = BaseModuleSemanticActivationStateTest.Live },
        new BaseModuleSemanticActivationStateGuard { Id = "semantic-missing", Test = BaseModuleSemanticActivationStateTest.Missing },
        new BaseModuleSemanticActivationStateGuard { Id = "semantic-retired", Test = BaseModuleSemanticActivationStateTest.Retired },
    ];

    private static BaseModuleMutationBlock SemanticBody() => new()
    {
        Statements = [new BaseModuleIfStatement
        {
            Id = "semantic-branch-missing", GuardId = "semantic-missing",
            WhenTrue = Require("semantic-require-missing", "semantic-missing"),
            WhenFalse = new BaseModuleMutationBlock { Statements = [new BaseModuleIfStatement
            {
                Id = "semantic-branch-live", GuardId = "semantic-live",
                WhenTrue = Require("semantic-require-live", "semantic-live"),
                WhenFalse = new BaseModuleMutationBlock { Statements = [new BaseModuleIfStatement
                {
                    Id = "semantic-branch-retired", GuardId = "semantic-retired",
                    WhenTrue = Require("semantic-require-retired", "semantic-retired"),
                    WhenFalse = Require("semantic-require-absent", "semantic-absent"),
                }]},
            }]},
        }],
    };

    private static BaseModuleMutationBlock Require(string id, string guard) => new()
    { Statements = [new BaseModuleRequireStatement { Id = id, GuardId = guard, RequirementId = "semantic-state-captured" }] };

    private static BaseModuleObjectExpression EnsureResult()
    {
        BaseModuleValue<string?> missingRetired = BaseModuleMutationTemplateBuilder.Missing(
            "semantic-id-retired-missing", ResultProperties.ActivationId);
        BaseModuleValue<string?> missingAbsent = BaseModuleMutationTemplateBuilder.Missing(
            "semantic-id-absent-missing", ResultProperties.ActivationId);
        BaseModuleValue<string?> activationId = BaseModuleMutationTemplateBuilder.SemanticActivationId(
            "semantic-id", ResultProperties.ActivationId);
        BaseModuleValue<string?> absentChoice = BaseModuleMutationTemplateBuilder.Conditional(
            "semantic-id-absent", "semantic-absent", missingAbsent, activationId);
        BaseModuleValue<string?> resultId = BaseModuleMutationTemplateBuilder.Conditional(
            "semantic-id-retired", "semantic-retired", missingRetired, absentChoice);
        return BaseModuleMutationTemplateBuilder.ResultObject(
            "semantic-ensure-result",
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.ActivationId, resultId),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.Disposition,
                BaseModuleMutationTemplateBuilder.SemanticEnsureDisposition("semantic-disposition", ResultProperties.Disposition)),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.WasMaterialized,
                BaseModuleMutationTemplateBuilder.SemanticActivationWasMaterialized("semantic-materialized", ResultProperties.WasMaterialized))).Value;
    }

    private static BaseModuleObjectExpression RetireResult() =>
        BaseModuleMutationTemplateBuilder.ResultObject(
            "semantic-retire-result",
            BaseModuleMutationTemplateBuilder.Property(SemanticRetirementMutationSmoke.ResultProperties.Disposition,
                BaseModuleMutationTemplateBuilder.SemanticRetirementDisposition(
                    "semantic-retirement-disposition", SemanticRetirementMutationSmoke.ResultProperties.Disposition))).Value;
}

[BaseRegisteredModuleMutation("hpd.base.aot.semantic.retire-operation", typeof(SemanticMutationSmokeJsonContext), typeof(SemanticMutationSmokeRequest), typeof(SemanticRetireSmokeResult), Version = 1, OwningModuleId = "hpd.base.aot", GrantId = "hpd.base.aot.semantic.retire-operation")]
public static partial class SemanticRetirementMutationSmoke
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = SemanticEnsureMutationSmoke.DefinitionFor(
        "hpd.base.aot.semantic.retire-operation", "hpd.base.aot.semantic.retire-operation", ensure: false,
        System.Security.Cryptography.SHA256.HashData("hpd.base.aot.semantic.retire-operation.v1"u8));
}

public sealed record SemanticMutationSmokeRequest
{
    [BaseField("hpd.base.aot.semantic.request.marker")] public required string Marker { get; init; }
}
public sealed record SemanticEnsureSmokeResult
{
    [BaseField("hpd.base.aot.semantic.result.disposition", AllowedEnumLiterals = ["created", "existing", "retired"])]
    [JsonConverter(typeof(BaseClosedEnumJsonConverter<BaseSemanticActivationEnsureDisposition>))]
    public required BaseSemanticActivationEnsureDisposition Disposition { get; init; }
    [BaseField("hpd.base.aot.semantic.result.activation-id", Presence = BaseFieldPresence.Optional,
        Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256)]
    public string? ActivationId { get; init; }
    [BaseField("hpd.base.aot.semantic.result.materialized")] public required bool WasMaterialized { get; init; }
}
public sealed record SemanticRetireSmokeResult
{
    [BaseField("hpd.base.aot.semantic.result.retirement-disposition",
        AllowedEnumLiterals = ["alreadyCompacted", "alreadyRetired", "retiredNow"])]
    [JsonConverter(typeof(BaseClosedEnumJsonConverter<BaseSemanticActivationRetirementDisposition>))]
    public required BaseSemanticActivationRetirementDisposition Disposition { get; init; }
}
[JsonSerializable(typeof(SemanticMutationSmokeRequest))]
[JsonSerializable(typeof(SemanticEnsureSmokeResult))]
[JsonSerializable(typeof(SemanticRetireSmokeResult))]
public sealed partial class SemanticMutationSmokeJsonContext : JsonSerializerContext;
