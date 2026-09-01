using System.Text.Json.Serialization;

namespace HPD.Base.AotSmoke;

[BaseRegisteredModuleMutation("hpd.base.aot.subject.verify", typeof(SubjectModuleMutationSmokeJsonContext),
    typeof(SubjectModuleMutationSmokeRequest), typeof(SubjectModuleMutationSmokeResult), Version = 1,
    OwningModuleId = "hpd.base.aot", GrantId = "hpd.base.aot.subject.verify")]
internal static partial class SubjectModuleMutationSmoke
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
        Id = "hpd.base.aot.subject.verify", Version = 1, OwningModuleId = "hpd.base.aot",
        GrantId = "hpd.base.aot.subject.verify", Audience = BaseModuleMutationAudience.Service,
        RequestTypeId = "hpd.base.aot.subject.verify.request",
        ResultTypeId = "hpd.base.aot.subject.verify.result",
        SystemCollectionIds = [], SystemSourceGrants = [], GenerationCellIds = [],
        ImportedSubjectContractIds = ["hpd.base.aot.subject"],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [],
            Guards = [BaseModuleMutationTemplateBuilder.ValueEquals("subject-equal",
                BaseModuleMutationTemplateBuilder.Request("subject-left", RequestProperties.Subject),
                BaseModuleMutationTemplateBuilder.Request("subject-right", RequestProperties.Subject))],
            Preconditions = [BaseModuleMutationTemplateBuilder.Precondition(
                "subject-valid", "subject-equal", "subject-valid")],
            Body = BaseModuleMutationTemplateBuilder.Block(
                BaseModuleMutationTemplateBuilder.Require("subject-require", "subject-equal", "subject-valid")),
            Result = BaseModuleMutationTemplateBuilder.Result(BaseModuleMutationTemplateBuilder.ResultObject(
                "subject-result", BaseModuleMutationTemplateBuilder.Property(ResultProperties.Subject,
                    BaseModuleMutationTemplateBuilder.Request("result-subject", RequestProperties.Subject)))),
        },
        Limits = ModuleMutationSmoke.Limits(),
        ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
        Checksum = BaseModuleMutationChecksum.Create(
            System.Security.Cryptography.SHA256.HashData("hpd.base.aot.subject.verify.v1"u8)),
    });
}

internal sealed record SubjectModuleMutationSmokeRequest
{
    [BaseField("hpd.base.aot.subject.verify.request.subject")]
    [BaseSubjectReference(typeof(AotSubject), Requirement = BaseSubjectReferenceRequirement.Active)]
    public required BaseSubjectReference<AotSubject> Subject { get; init; }
}

internal sealed record SubjectModuleMutationSmokeResult
{
    [BaseField("hpd.base.aot.subject.verify.result.subject")]
    [BaseSubjectReference(typeof(AotSubject), Requirement = BaseSubjectReferenceRequirement.Active)]
    public required BaseSubjectReference<AotSubject> Subject { get; init; }
}

[JsonSerializable(typeof(SubjectModuleMutationSmokeRequest))]
[JsonSerializable(typeof(SubjectModuleMutationSmokeResult))]
internal sealed partial class SubjectModuleMutationSmokeJsonContext : JsonSerializerContext;
