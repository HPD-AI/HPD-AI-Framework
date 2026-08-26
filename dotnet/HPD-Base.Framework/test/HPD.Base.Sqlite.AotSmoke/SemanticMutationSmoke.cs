using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace HPD.Base.Sqlite.AotSmoke;

[BaseRegisteredModuleMutation("hpd.base.sqlite.aot.semantic.ensure-operation", typeof(SemanticMutationSmokeJsonContext), typeof(SemanticMutationSmokeRequest), typeof(SemanticEnsureSmokeResult), Version = 1, OwningModuleId = "hpd.base.sqlite.aot", GrantId = "hpd.base.sqlite.aot.semantic.ensure-operation")]
public static partial class SemanticEnsureMutationSmoke
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = DefinitionFor(
        "hpd.base.sqlite.aot.semantic.ensure-operation", "hpd.base.sqlite.aot.semantic.ensure-operation", ensure: true,
        System.Security.Cryptography.SHA256.HashData("hpd.base.sqlite.aot.semantic.ensure-operation.v1"u8));

    internal static BaseRegisteredModuleMutationDefinition DefinitionFor(string id, string grant, bool ensure, byte[] checksum) =>
        BaseModuleMutationContract.Seal(new BaseRegisteredModuleMutationDefinition
        {
            Id = id, Version = 1, OwningModuleId = "hpd.base.sqlite.aot", GrantId = grant,
            Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.base.sqlite.aot.semantic.request",
            ResultTypeId = ensure ? "hpd.base.sqlite.aot.semantic.ensure-result" : "hpd.base.sqlite.aot.semantic.retire-result",
            SystemCollectionIds = [], SystemSourceGrants = [], GenerationCellIds = [], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [], Guards = SemanticGuards(), Body = SemanticBody(),
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

    private static BaseModuleObjectExpression EnsureResult() => new()
    {
        Id = "semantic-ensure-result",
        Properties =
        [
            new BaseModuleObjectPropertyExpression { StablePropertyId = "hpd.base.sqlite.aot.semantic.result.activation-id", Value = new BaseModuleConditionalExpression
            {
                Id = "semantic-id-retired", ResultType = ResultProperties.ActivationId.Authority.ValueType, GuardId = "semantic-retired",
                WhenTrue = Null("semantic-id-retired-null"),
                WhenFalse = new BaseModuleConditionalExpression
                {
                    Id = "semantic-id-absent", ResultType = ResultProperties.ActivationId.Authority.ValueType, GuardId = "semantic-absent",
                    WhenTrue = Null("semantic-id-absent-null"),
                    WhenFalse = new BaseModuleSemanticActivationIdExpression { Id = "semantic-id", ResultType = ResultProperties.ActivationId.Authority.ValueType },
                },
            } },
            new BaseModuleObjectPropertyExpression { StablePropertyId = "hpd.base.sqlite.aot.semantic.result.disposition", Value = new BaseModuleSemanticActivationDispositionExpression { Id = "semantic-disposition", ResultType = ResultProperties.Disposition.Authority.ValueType } },
            new BaseModuleObjectPropertyExpression { StablePropertyId = "hpd.base.sqlite.aot.semantic.result.materialized", Value = new BaseModuleSemanticActivationWasMaterializedExpression { Id = "semantic-materialized", ResultType = ResultProperties.WasMaterialized.Authority.ValueType } },
        ],
    };

    private static BaseModuleObjectExpression RetireResult() => new()
    {
        Id = "semantic-retire-result",
        Properties = [new BaseModuleObjectPropertyExpression
        {
            StablePropertyId = "hpd.base.sqlite.aot.semantic.result.retirement-disposition",
            Value = new BaseModuleSemanticActivationRetirementDispositionExpression { Id = "semantic-retirement-disposition", ResultType = SemanticRetirementMutationSmoke.ResultProperties.Disposition.Authority.ValueType },
        }],
    };

    private static BaseModuleConstantExpression Null(string id) => new()
    { Id = id, ResultType = ResultProperties.ActivationId.Authority.ValueType, CanonicalBaseJson = "null"u8.ToArray().ToImmutableArray() };
}

[BaseRegisteredModuleMutation("hpd.base.sqlite.aot.semantic.retire-operation", typeof(SemanticMutationSmokeJsonContext), typeof(SemanticMutationSmokeRequest), typeof(SemanticRetireSmokeResult), Version = 1, OwningModuleId = "hpd.base.sqlite.aot", GrantId = "hpd.base.sqlite.aot.semantic.retire-operation")]
public static partial class SemanticRetirementMutationSmoke
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = SemanticEnsureMutationSmoke.DefinitionFor(
        "hpd.base.sqlite.aot.semantic.retire-operation", "hpd.base.sqlite.aot.semantic.retire-operation", ensure: false,
        System.Security.Cryptography.SHA256.HashData("hpd.base.sqlite.aot.semantic.retire-operation.v1"u8));
}

public sealed record SemanticMutationSmokeRequest
{
    [BaseField("hpd.base.sqlite.aot.semantic.request.marker")] public required string Marker { get; init; }
}
public sealed record SemanticEnsureSmokeResult
{
    [BaseField("hpd.base.sqlite.aot.semantic.result.disposition")] public required string Disposition { get; init; }
    [BaseField("hpd.base.sqlite.aot.semantic.result.activation-id")] public string? ActivationId { get; init; }
    [BaseField("hpd.base.sqlite.aot.semantic.result.materialized")] public required bool WasMaterialized { get; init; }
}
public sealed record SemanticRetireSmokeResult
{
    [BaseField("hpd.base.sqlite.aot.semantic.result.retirement-disposition")] public required string Disposition { get; init; }
}
[JsonSerializable(typeof(SemanticMutationSmokeRequest))]
[JsonSerializable(typeof(SemanticEnsureSmokeResult))]
[JsonSerializable(typeof(SemanticRetireSmokeResult))]
public sealed partial class SemanticMutationSmokeJsonContext : JsonSerializerContext;
