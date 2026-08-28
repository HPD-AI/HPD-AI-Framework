using HPD.Base;

namespace HPD.Auth.Base;

[BaseRegisteredModuleMutation("hpd.auth.cleanup.reconcile-cursor.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthCleanupReconcileCursorV1), typeof(AuthCleanupReconcileCursorResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.cleanup.advance")]
internal static partial class AuthCleanupReconcileCursorOperationV1
{
    private const string Prefix = "hpd.auth.cleanup.reconcile-cursor";
    private const string Capture = "cursor";
    private const string CreatePage = Prefix + ".statement.create.page";
    private const string CreateWrap = Prefix + ".statement.create.wrap";
    private const string PatchPage = Prefix + ".statement.patch.page";
    private const string PatchWrap = Prefix + ".statement.patch.wrap";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
        Id = "hpd.auth.cleanup.reconcile-cursor.v1", Version = 1,
        OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.cleanup.advance",
        Audience = BaseModuleMutationAudience.Service,
        RequestTypeId = "hpd.auth.type.auth-cleanup-reconcile-cursor-v1.v1",
        ResultTypeId = "hpd.auth.type.auth-cleanup-reconcile-cursor-result-v1.v1",
        SystemCollectionIds = [AuthMaintenanceCursorRecordV1.Collection.Id],
        SystemSourceGrants = [new BaseModuleSystemSourceGrant
        {
            CollectionId = AuthMaintenanceCursorRecordV1.Collection.Id,
            GrantId = "auth.cleanup.execute",
        }],
        GenerationCellIds = [], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [BaseModuleMutationTemplateBuilder.CaptureRecord(
                Capture, CursorId("capture"), BaseModuleCapturePresence.AllowEither)],
            Guards = [.. Guards()], Preconditions = [], Body = Body(), Result = Result(),
        },
        Limits = AuthModuleMutationDefaults.Limits() with
        {
            MaximumStatements = 48,
            MaximumExpressionNodes = 256,
        },
        ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
        Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
    });

    private static BaseModuleGuard[] Guards() =>
    [
        Presence("expected.afterSubjectId.missing", ExpectedAfterSubjectId("presence.missing"), BaseModuleFieldPresenceTest.Missing),
        Presence("expected.afterSubjectKind.missing", ExpectedAfterSubjectKind("presence.missing"), BaseModuleFieldPresenceTest.Missing),
        Presence("expected.afterTenantId.missing", ExpectedAfterTenantId("presence.missing"), BaseModuleFieldPresenceTest.Missing),
        Presence("expected.pass.missing", ExpectedPass("presence.missing"), BaseModuleFieldPresenceTest.Missing),
        Presence("expected.pass.present", ExpectedPass("presence.present"), BaseModuleFieldPresenceTest.PresentValue),
        Presence("expected.revision.missing", ExpectedRevision("presence.missing"), BaseModuleFieldPresenceTest.Missing),
        Presence("expected.revision.present", ExpectedRevision("presence.present"), BaseModuleFieldPresenceTest.PresentValue),
        BaseModuleMutationTemplateBuilder.FieldEquals(Prefix + ".guard.field.afterSubjectId", Capture,
            AuthMaintenanceCursorRecordV1.Fields.AfterSubjectId.ModuleMutation, ExpectedAfterSubjectId("field")),
        BaseModuleMutationTemplateBuilder.FieldEquals(Prefix + ".guard.field.afterSubjectKind", Capture,
            AuthMaintenanceCursorRecordV1.Fields.AfterSubjectKind.ModuleMutation, ExpectedAfterSubjectKind("field")),
        BaseModuleMutationTemplateBuilder.FieldEquals(Prefix + ".guard.field.afterTenantId", Capture,
            AuthMaintenanceCursorRecordV1.Fields.AfterTenantId.ModuleMutation, ExpectedAfterTenantId("field")),
        BaseModuleMutationTemplateBuilder.ValueEquals(Prefix + ".guard.field.pass", ExpectedPass("field"),
            BaseModuleMutationTemplateBuilder.LiftOptional(Prefix + ".expression.pass.capturedOptional.field",
                RequestProperties.ExpectedPassGeneration, CapturedPass("field"))),
        BaseModuleMutationTemplateBuilder.ValueEquals(Prefix + ".guard.field.revision", ExpectedRevision("field"),
            BaseModuleMutationTemplateBuilder.LiftOptional(Prefix + ".expression.revision.capturedOptional.field",
                RequestProperties.ExpectedRevision, CapturedRevision("field"))),
        Presence("next.afterSubjectId.missing", NextSubjectId("presence.missing"), BaseModuleFieldPresenceTest.Missing),
        Presence("next.afterSubjectId.present", NextSubjectId("presence.present"), BaseModuleFieldPresenceTest.PresentValue),
        Presence("next.afterSubjectKind.missing", NextSubjectKind("presence.missing"), BaseModuleFieldPresenceTest.Missing),
        Presence("next.afterSubjectKind.present", NextSubjectKind("presence.present"), BaseModuleFieldPresenceTest.PresentValue),
        Presence("next.afterTenantId.missing", NextTenantId("presence.missing"), BaseModuleFieldPresenceTest.Missing),
        Presence("next.afterTenantId.present", NextTenantId("presence.present"), BaseModuleFieldPresenceTest.PresentValue),
        BaseModuleMutationTemplateBuilder.RecordPresent(Prefix + ".guard.record.missing", Capture, false),
        BaseModuleMutationTemplateBuilder.RecordPresent(Prefix + ".guard.record.present", Capture, true),
        BaseModuleMutationTemplateBuilder.ValueEquals(Prefix + ".guard.wrap.false", Wrap("false"),
            BaseModuleMutationTemplateBuilder.Constant(Prefix + ".expression.wrap.false", RequestProperties.Wrap.ConstantAuthority, false)),
        BaseModuleMutationTemplateBuilder.ValueEquals(Prefix + ".guard.wrap.true", Wrap("true"),
            BaseModuleMutationTemplateBuilder.Constant(Prefix + ".expression.wrap.true", RequestProperties.Wrap.ConstantAuthority, true)),
    ];

    private static BaseModuleMutationBlock Body() => BaseModuleMutationTemplateBuilder.Block(
        BaseModuleMutationTemplateBuilder.If(Prefix + ".statement.branch.recordMissing", Prefix + ".guard.record.missing",
            MissingBranch(), PresentBranch()));

    private static BaseModuleMutationBlock MissingBranch() => BaseModuleMutationTemplateBuilder.Block(
        Require("missing.00.revision", "expected.revision.missing"),
        Require("missing.01.pass", "expected.pass.missing"),
        Require("missing.02.afterTenant", "expected.afterTenantId.missing"),
        Require("missing.03.afterKind", "expected.afterSubjectKind.missing"),
        Require("missing.04.afterSubject", "expected.afterSubjectId.missing"),
        BaseModuleMutationTemplateBuilder.If(Prefix + ".statement.missing.wrap", Prefix + ".guard.wrap.true",
            BaseModuleMutationTemplateBuilder.Block(
                Require("missing.wrap.00.nextTenant", "next.afterTenantId.missing"),
                Require("missing.wrap.01.nextKind", "next.afterSubjectKind.missing"),
                Require("missing.wrap.02.nextSubject", "next.afterSubjectId.missing"),
                Create(true)),
            BaseModuleMutationTemplateBuilder.Block(
                Require("missing.page.00.wrapFalse", "wrap.false"),
                Require("missing.page.01.nextTenant", "next.afterTenantId.present"),
                Require("missing.page.02.nextKind", "next.afterSubjectKind.present"),
                Require("missing.page.03.nextSubject", "next.afterSubjectId.present"),
                Create(false))));

    private static BaseModuleMutationBlock PresentBranch() => BaseModuleMutationTemplateBuilder.Block(
        Require("present.00.record", "record.present"),
        Require("present.01.revision", "expected.revision.present"),
        Require("present.02.pass", "expected.pass.present"),
        Require("present.03.passEqual", "field.pass"),
        Require("present.04.revisionEqual", "field.revision"),
        Require("present.05.afterTenantEqual", "field.afterTenantId"),
        Require("present.06.afterKindEqual", "field.afterSubjectKind"),
        Require("present.07.afterSubjectEqual", "field.afterSubjectId"),
        BaseModuleMutationTemplateBuilder.If(Prefix + ".statement.present.wrap", Prefix + ".guard.wrap.true",
            BaseModuleMutationTemplateBuilder.Block(
                Require("present.wrap.00.nextTenant", "next.afterTenantId.missing"),
                Require("present.wrap.01.nextKind", "next.afterSubjectKind.missing"),
                Require("present.wrap.02.nextSubject", "next.afterSubjectId.missing"),
                Patch(true)),
            BaseModuleMutationTemplateBuilder.Block(
                Require("present.page.00.wrapFalse", "wrap.false"),
                Require("present.page.01.nextTenant", "next.afterTenantId.present"),
                Require("present.page.02.nextKind", "next.afterSubjectKind.present"),
                Require("present.page.03.nextSubject", "next.afterSubjectId.present"),
                Patch(false))));

    private static BaseModuleCreateStatement Create(bool wrap)
    {
        string branch = wrap ? "create.wrap" : "create.page";
        return BaseModuleMutationTemplateBuilder.Create(
        wrap ? CreateWrap : CreatePage, CursorId(branch + ".record"),
        BaseModuleMutationTemplateBuilder.Object<AuthMaintenanceCursorRecordV1>(
            Prefix + (wrap ? ".expression.create.wrap" : ".expression.create.page"),
            [
                ..(!wrap ? new BaseModuleFieldValue<AuthMaintenanceCursorRecordV1>[]
                {
                    BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceCursorRecordV1.Fields.AfterSubjectId, NextSubjectId(branch)),
                    BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceCursorRecordV1.Fields.AfterSubjectKind, NextSubjectKind(branch)),
                    BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceCursorRecordV1.Fields.AfterTenantId, NextTenantId(branch)),
                } : []),
                BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceCursorRecordV1.Fields.Id, CursorIdValue(branch + ".payload")),
                BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceCursorRecordV1.Fields.LastPageDigest,
                    BaseModuleMutationTemplateBuilder.LiftOptional(Prefix + ".expression.pageDigest.optional." + branch, AuthMaintenanceCursorRecordV1.Fields.LastPageDigest.ModuleMutation, PageDigest(branch))),
                BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceCursorRecordV1.Fields.PassGeneration,
                    BaseModuleMutationTemplateBuilder.Constant(Prefix + ".expression.pass." + branch, AuthMaintenanceCursorRecordV1.Fields.PassGeneration.ConstantAuthority, 1L)),
                BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceCursorRecordV1.Fields.UpdatedAt, OperationTime(branch)),
            ]));
    }

    private static BaseModulePatchStatement Patch(bool wrap)
    {
        string branch = wrap ? "patch.wrap" : "patch.page";
        return BaseModuleMutationTemplateBuilder.Patch(
        wrap ? PatchWrap : PatchPage, CursorId(branch),
        BaseModuleMutationTemplateBuilder.Object<AuthMaintenanceCursorRecordV1>(
            Prefix + (wrap ? ".expression.patch.wrap" : ".expression.patch.page"),
            [
                ..(wrap ? new BaseModuleFieldValue<AuthMaintenanceCursorRecordV1>[]
                {
                    BaseModuleMutationTemplateBuilder.Remove(AuthMaintenanceCursorRecordV1.Fields.AfterSubjectId.ModuleMutation),
                    BaseModuleMutationTemplateBuilder.Remove(AuthMaintenanceCursorRecordV1.Fields.AfterSubjectKind.ModuleMutation),
                    BaseModuleMutationTemplateBuilder.Remove(AuthMaintenanceCursorRecordV1.Fields.AfterTenantId.ModuleMutation),
                } : new BaseModuleFieldValue<AuthMaintenanceCursorRecordV1>[]
                {
                    BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceCursorRecordV1.Fields.AfterSubjectId, NextSubjectId(branch)),
                    BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceCursorRecordV1.Fields.AfterSubjectKind, NextSubjectKind(branch)),
                    BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceCursorRecordV1.Fields.AfterTenantId, NextTenantId(branch)),
                }),
                BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceCursorRecordV1.Fields.LastPageDigest,
                    BaseModuleMutationTemplateBuilder.LiftOptional(Prefix + ".expression.pageDigest.optional." + branch, AuthMaintenanceCursorRecordV1.Fields.LastPageDigest.ModuleMutation, PageDigest(branch))),
                ..(wrap ? new BaseModuleFieldValue<AuthMaintenanceCursorRecordV1>[]
                {
                    BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceCursorRecordV1.Fields.PassGeneration, IncrementedPass(branch)),
                } : []),
                BaseModuleMutationTemplateBuilder.Field(AuthMaintenanceCursorRecordV1.Fields.UpdatedAt, OperationTime(branch)),
            ]),
        CapturedRevision(branch));
    }

    private static BaseModuleResultProjection Result()
    {
        BaseModuleValue<RevisionToken> createPageRevision = BaseModuleMutationTemplateBuilder.CommittedRevision(Prefix + ".expression.result.revision.createPage", CreatePage);
        BaseModuleValue<RevisionToken> createWrapRevision = BaseModuleMutationTemplateBuilder.CommittedRevision(Prefix + ".expression.result.revision.createWrap", CreateWrap);
        BaseModuleValue<RevisionToken> patchPageRevision = BaseModuleMutationTemplateBuilder.CommittedRevision(Prefix + ".expression.result.revision.patchPage", PatchPage);
        BaseModuleValue<RevisionToken> patchWrapRevision = BaseModuleMutationTemplateBuilder.CommittedRevision(Prefix + ".expression.result.revision.patchWrap", PatchWrap);
        BaseModuleValue<RevisionToken> createdRevision = BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.revision.created",
            Prefix + ".guard.wrap.true", createWrapRevision, createPageRevision);
        BaseModuleValue<RevisionToken> patchedRevision = BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.revision.patched",
            Prefix + ".guard.wrap.true", patchWrapRevision, patchPageRevision);
        BaseModuleValue<RevisionToken> revision = BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.revision",
            Prefix + ".guard.record.missing", createdRevision, patchedRevision);
        BaseModuleValue<long> one = BaseModuleMutationTemplateBuilder.Constant(Prefix + ".expression.result.pass.one",
            AuthMaintenanceCursorRecordV1.Fields.PassGeneration.ConstantAuthority, 1L);
        BaseModuleValue<long> existingPassForWrap = CapturedPass("result.wrap");
        BaseModuleValue<long> wrappedPass = BaseModuleMutationTemplateBuilder.Integer(Prefix + ".expression.result.pass.wrapped",
            BaseModuleNumericOperator.IntegerAddChecked, existingPassForWrap,
            BaseModuleMutationTemplateBuilder.Constant(Prefix + ".expression.result.pass.increment", AuthMaintenanceCursorRecordV1.Fields.PassGeneration.ConstantAuthority, 1L));
        BaseModuleValue<long> existingPass = CapturedPass("result.page");
        BaseModuleValue<long> presentPass = BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.pass.present",
            Prefix + ".guard.wrap.true", wrappedPass, existingPass);
        BaseModuleValue<long> pass = BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.pass",
            Prefix + ".guard.record.missing", ResultProperties.PassGeneration, one, presentPass);
        return BaseModuleMutationTemplateBuilder.Result(BaseModuleMutationTemplateBuilder.ResultObject(Prefix + ".expression.result",
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.AfterSubjectId,
                BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.afterSubjectId", Prefix + ".guard.wrap.true",
                    ResultProperties.AfterSubjectId, BaseModuleMutationTemplateBuilder.Missing(Prefix + ".expression.result.afterSubjectId.missing", ResultProperties.AfterSubjectId), NextSubjectId("result"))),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.AfterSubjectKind,
                BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.afterSubjectKind", Prefix + ".guard.wrap.true",
                    ResultProperties.AfterSubjectKind, BaseModuleMutationTemplateBuilder.Missing(Prefix + ".expression.result.afterSubjectKind.missing", ResultProperties.AfterSubjectKind), NextSubjectKind("result"))),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.AfterTenantId,
                BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.afterTenantId", Prefix + ".guard.wrap.true",
                    ResultProperties.AfterTenantId, BaseModuleMutationTemplateBuilder.Missing(Prefix + ".expression.result.afterTenantId.missing", ResultProperties.AfterTenantId), NextTenantId("result"))),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.PageDigest, PageDigest("result")),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.PassGeneration, pass),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.Revision, revision)));
    }

    private static BaseModuleRequireStatement Require(string id, string guardSuffix) =>
        BaseModuleMutationTemplateBuilder.Require(Prefix + ".statement.require." + id,
            Prefix + ".guard." + guardSuffix, "auth.cleanup.reconcileConflict");
    private static BaseModuleValuePresenceGuard Presence<T>(string id, BaseModuleValue<T> value, BaseModuleFieldPresenceTest test) =>
        BaseModuleMutationTemplateBuilder.ValuePresence(Prefix + ".guard." + id, value, test);
    private static BaseModuleValue<string> CursorIdValue(string id) => BaseModuleMutationTemplateBuilder.Request(Prefix + ".expression.cursorId." + id, RequestProperties.CursorId);
    private static BaseModuleValue<BaseRecordId<AuthMaintenanceCursorRecordV1>> CursorId(string id) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthMaintenanceCursorRecordV1>(Prefix + ".expression.recordId." + id, CursorIdValue(id));
    private static BaseModuleValue<RevisionToken?> ExpectedRevision(string id) => BaseModuleMutationTemplateBuilder.Request(Prefix + ".expression.expectedRevision." + id, RequestProperties.ExpectedRevision);
    private static BaseModuleValue<long?> ExpectedPass(string id) => BaseModuleMutationTemplateBuilder.Request(Prefix + ".expression.expectedPass." + id, RequestProperties.ExpectedPassGeneration);
    private static BaseModuleValue<Guid?> ExpectedAfterTenantId(string id) => BaseModuleMutationTemplateBuilder.Request(Prefix + ".expression.expectedAfterTenantId." + id, RequestProperties.ExpectedAfterTenantId);
    private static BaseModuleValue<AuthCleanupSubjectKindV1?> ExpectedAfterSubjectKind(string id) => BaseModuleMutationTemplateBuilder.Request(Prefix + ".expression.expectedAfterSubjectKind." + id, RequestProperties.ExpectedAfterSubjectKind);
    private static BaseModuleValue<Guid?> ExpectedAfterSubjectId(string id) => BaseModuleMutationTemplateBuilder.Request(Prefix + ".expression.expectedAfterSubjectId." + id, RequestProperties.ExpectedAfterSubjectId);
    private static BaseModuleValue<Guid?> NextTenantId(string id) => BaseModuleMutationTemplateBuilder.Request(Prefix + ".expression.nextTenantId." + id, RequestProperties.NextTenantId);
    private static BaseModuleValue<AuthCleanupSubjectKindV1?> NextSubjectKind(string id) => BaseModuleMutationTemplateBuilder.Request(Prefix + ".expression.nextSubjectKind." + id, RequestProperties.NextSubjectKind);
    private static BaseModuleValue<Guid?> NextSubjectId(string id) => BaseModuleMutationTemplateBuilder.Request(Prefix + ".expression.nextSubjectId." + id, RequestProperties.NextSubjectId);
    private static BaseModuleValue<BaseBinary> PageDigest(string id) => BaseModuleMutationTemplateBuilder.Request(Prefix + ".expression.pageDigest." + id, RequestProperties.PageDigest);
    private static BaseModuleValue<DateTimeOffset> OperationTime(string id) => BaseModuleMutationTemplateBuilder.Request(Prefix + ".expression.operationTime." + id, RequestProperties.OperationTime);
    private static BaseModuleValue<bool> Wrap(string id) => BaseModuleMutationTemplateBuilder.Request(Prefix + ".expression.wrap." + id, RequestProperties.Wrap);
    private static BaseModuleValue<RevisionToken> CapturedRevision(string id) => BaseModuleMutationTemplateBuilder.CapturedRevision(Prefix + ".expression.revision.captured." + id, Capture);
    private static BaseModuleValue<long> CapturedPass(string id) => BaseModuleMutationTemplateBuilder.Captured(Prefix + ".expression.pass.captured." + id, Capture, AuthMaintenanceCursorRecordV1.Fields.PassGeneration.ModuleMutation);
    private static BaseModuleValue<long> IncrementedPass(string id) => BaseModuleMutationTemplateBuilder.Integer(Prefix + ".expression.pass.incremented." + id,
        BaseModuleNumericOperator.IntegerAddChecked,
        CapturedPass(id),
        BaseModuleMutationTemplateBuilder.Constant(Prefix + ".expression.pass.one." + id, AuthMaintenanceCursorRecordV1.Fields.PassGeneration.ConstantAuthority, 1L));
}
