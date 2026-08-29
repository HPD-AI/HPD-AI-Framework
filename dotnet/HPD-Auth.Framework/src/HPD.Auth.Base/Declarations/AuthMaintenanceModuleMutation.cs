using HPD.Base;

namespace HPD.Auth.Base;

[BaseRegisteredModuleMutation("hpd.auth.maintenance-run.initialize.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthMaintenanceRunInitializeV1), typeof(AuthMaintenanceRunResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.cleanup.advance")]
internal static partial class AuthMaintenanceRunInitializeOperationV1
{
    private const string Capture = "hpd.auth.maintenance-run.initialize.capture.run";
    private const string CreateStatement = "hpd.auth.maintenance-run.initialize.statement.000.create";
    private const string PresentGuard = "hpd.auth.maintenance-run.initialize.guard.present";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(
        new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.maintenance-run.initialize.v1", Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.cleanup.advance",
            Audience = BaseModuleMutationAudience.System,
            RequestTypeId = "hpd.auth.type.auth-maintenance-run-initialize-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-maintenance-run-result-v1.v1",
            SystemCollectionIds = [AuthMaintenanceRunRecordV1.Collection.Id],
            SystemSourceGrants =
            [
                new BaseModuleSystemSourceGrant
                {
                    CollectionId = AuthMaintenanceRunRecordV1.Collection.Id,
                    GrantId = "auth.cleanup.execute",
                },
            ],
            GenerationCellIds = [], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [BaseModuleMutationTemplateBuilder.CaptureRecord(Capture, RecordId("capture"), BaseModuleCapturePresence.AllowEither)],
                Guards =
                [
                    BaseModuleMutationTemplateBuilder.FieldEquals(
                        "hpd.auth.maintenance-run.initialize.guard.activationId", Capture,
                        AuthMaintenanceRunRecordV1.Fields.ActivationId.ModuleMutation, RequestActivation("guard")),
                    BaseModuleMutationTemplateBuilder.FieldEquals(
                        "hpd.auth.maintenance-run.initialize.guard.kind", Capture,
                        AuthMaintenanceRunRecordV1.Fields.Kind.ModuleMutation,
                        BaseModuleMutationTemplateBuilder.Request(
                            "hpd.auth.maintenance-run.initialize.expression.kind.guard", RequestProperties.Kind)),
                    BaseModuleMutationTemplateBuilder.RecordPresent(PresentGuard, Capture, true),
                ],
                Preconditions = [],
                Body = BaseModuleMutationTemplateBuilder.Block(
                    BaseModuleMutationTemplateBuilder.If(
                        "hpd.auth.maintenance-run.initialize.statement.000.presence", PresentGuard,
                        BaseModuleMutationTemplateBuilder.Block(
                            BaseModuleMutationTemplateBuilder.Require(
                                "hpd.auth.maintenance-run.initialize.statement.001.requireActivationId",
                                "hpd.auth.maintenance-run.initialize.guard.activationId",
                                "auth.maintenanceRun.identityMismatch"),
                            BaseModuleMutationTemplateBuilder.Require(
                                "hpd.auth.maintenance-run.initialize.statement.002.requireKind",
                                "hpd.auth.maintenance-run.initialize.guard.kind",
                                "auth.maintenanceRun.kindMismatch")),
                        BaseModuleMutationTemplateBuilder.Block(Create()))),
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.ResultObject(
                        "hpd.auth.maintenance-run.initialize.expression.result.000",
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.Cutoff,
                            BaseModuleMutationTemplateBuilder.Conditional(
                                "hpd.auth.maintenance-run.initialize.expression.resultCutoff.000", PresentGuard, ResultProperties.Cutoff,
                                BaseModuleMutationTemplateBuilder.Captured(
                                    "hpd.auth.maintenance-run.initialize.expression.capturedCutoff.000", Capture,
                                    AuthMaintenanceRunRecordV1.Fields.Cutoff.ModuleMutation),
                                BaseModuleMutationTemplateBuilder.Request(
                                    "hpd.auth.maintenance-run.initialize.expression.requestCutoff.000", RequestProperties.Cutoff))),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.Id, DerivedId("result")),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.Kind,
                            BaseModuleMutationTemplateBuilder.Conditional(
                                "hpd.auth.maintenance-run.initialize.expression.resultKind.000", PresentGuard, ResultProperties.Kind,
                                BaseModuleMutationTemplateBuilder.Captured(
                                    "hpd.auth.maintenance-run.initialize.expression.capturedKind.000", Capture,
                                    AuthMaintenanceRunRecordV1.Fields.Kind.ModuleMutation),
                                BaseModuleMutationTemplateBuilder.Request(
                                    "hpd.auth.maintenance-run.initialize.expression.requestKind.000", RequestProperties.Kind))),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.Revision,
                            BaseModuleMutationTemplateBuilder.Conditional(
                                "hpd.auth.maintenance-run.initialize.expression.resultRevision.000", PresentGuard,
                                BaseModuleMutationTemplateBuilder.CapturedRevision(
                                    "hpd.auth.maintenance-run.initialize.expression.capturedRevision.000", Capture),
                                BaseModuleMutationTemplateBuilder.CommittedRevision(
                                    "hpd.auth.maintenance-run.initialize.expression.committedRevision.000", CreateStatement))))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<string> RequestActivation(string suffix) =>
        BaseModuleMutationTemplateBuilder.Request(
            $"hpd.auth.maintenance-run.initialize.expression.activationId.{suffix}", RequestProperties.ActivationId);

    private static BaseModuleValue<string> DerivedId(string suffix) =>
        BaseModuleMutationTemplateBuilder.Sha256HexStringIdentity(
            $"hpd.auth.maintenance-run.initialize.expression.derivedId.{suffix}",
            AuthMaintenanceRunRecordV1.Fields.Id.ModuleMutation,
            "hpd.auth.maintenance-run.v1", RequestActivation($"derived.{suffix}"));

    private static BaseModuleValue<BaseRecordId<AuthMaintenanceRunRecordV1>> RecordId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthMaintenanceRunRecordV1>(
            $"hpd.auth.maintenance-run.initialize.expression.recordId.{suffix}", DerivedId($"record.{suffix}"));

    private static BaseModuleCreateStatement Create() => BaseModuleMutationTemplateBuilder.Create(
        CreateStatement, RecordId("create"),
        BaseModuleMutationTemplateBuilder.Object<AuthMaintenanceRunRecordV1>(
            "hpd.auth.maintenance-run.initialize.expression.payload.000",
            BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceRunRecordV1.Fields.ActivationId, RequestActivation("payload")),
            BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceRunRecordV1.Fields.CreatedAt,
                BaseModuleMutationTemplateBuilder.Request("hpd.auth.maintenance-run.initialize.expression.createdAt.000", RequestProperties.Cutoff)),
            BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceRunRecordV1.Fields.Cutoff,
                BaseModuleMutationTemplateBuilder.Request("hpd.auth.maintenance-run.initialize.expression.cutoff.000", RequestProperties.Cutoff)),
            BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceRunRecordV1.Fields.Id, DerivedId("payload")),
            BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceRunRecordV1.Fields.Kind,
                BaseModuleMutationTemplateBuilder.Request("hpd.auth.maintenance-run.initialize.expression.kind.payload", RequestProperties.Kind))));
}
