using System.Collections.Immutable;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Effective;
using HPD.Gateway.Management;
using HPD.Gateway.Status;

namespace HPD.Gateway.Admin;

internal enum GatewayAdminClientIdempotency : byte { Forbidden, Required }
internal enum GatewayAdminClientDesiredPrecondition : byte { Forbidden, CreateOrReplace }
internal enum GatewayAdminClientSuccessMeaning : byte { CompletedRead, Created, AcceptedNotActive }
internal enum GatewayAdminClientPaginationKind : byte { None, OpaqueCursor }

internal sealed record GatewayAdminClientOperationSemantics(
    string Operation,
    Type? RequestType,
    Type SuccessType,
    int SuccessStatus,
    GatewayAdminClientSuccessMeaning SuccessMeaning,
    GatewayAdminClientIdempotency Idempotency,
    GatewayAdminClientDesiredPrecondition DesiredPrecondition,
    bool ProtectedNotFound,
    GatewayAdminClientPaginationKind Pagination,
    ImmutableArray<int> DocumentedErrors);

internal static class GatewayAdminClientSemanticLedger
{
    internal static ImmutableArray<GatewayAdminClientOperationSemantics> V1 { get; } =
    [
        Read("capabilities", typeof(GatewayCapabilityCatalog)),
        Read("host-capabilities", typeof(GatewayHostCapabilitySnapshotResponse)),
        Read("validate", typeof(GatewayValidationResponse), typeof(GatewayConfiguration)),
        Created("provision", typeof(GatewayProvisionResponse)),
        Read("desired", typeof(GatewayDesiredProjection), protectedNotFound: true),
        Read("status", typeof(GatewayTargetStatusResponse), protectedNotFound: true),
        Read("effective", typeof(GatewayEffectiveSnapshot), protectedNotFound: true),
        Created("submit", typeof(GatewayRevisionResponse), typeof(GatewayRevisionRequest), protectedNotFound: true),
        Accepted("submit-and-activate", typeof(GatewayRevisionResponse), typeof(GatewayRevisionRequest), cas: true),
        Page("revisions", typeof(GatewayAdminPage<GatewayRevisionProjection>)),
        Read("revision", typeof(GatewayRevisionProjection), protectedNotFound: true),
        Read("validation", typeof(GatewayValidationProjection), protectedNotFound: true),
        Accepted("activate", typeof(GatewayRevisionResponse), typeof(GatewayActivationRequest), cas: true),
        Accepted("rollback", typeof(GatewayRevisionResponse), typeof(GatewayActivationRequest), cas: true),
        Page("activations", typeof(GatewayActivationHistoryResponse)),
        Read("compare", typeof(GatewayRevisionComparison), typeof(GatewayCompareRequest), protectedNotFound: true),
        Read("export", typeof(GatewayExportResponse), protectedNotFound: true),
        Created("import", typeof(GatewayRevisionResponse), typeof(GatewayImportRequest), protectedNotFound: true),
        Accepted("import-and-activate", typeof(GatewayRevisionResponse), typeof(GatewayImportRequest), cas: true),
        Read("operation", typeof(GatewayOperationProjection), protectedNotFound: true),
        Page("audit", typeof(GatewayAdminPage<GatewayAuditProjection>)),
        Accepted("backup", typeof(GatewayAdministrativeResponse), typeof(GatewayBackupRequest), protectedNotFound: true, cas: false),
        Accepted("purge", typeof(GatewayAdministrativeResponse), typeof(GatewayPurgeRequest), protectedNotFound: true, cas: false),
    ];

    internal static GatewayAdminClientOperationSemantics For(string operation) =>
        V1.Single(item => StringComparer.Ordinal.Equals(item.Operation, operation));

    internal static IEnumerable<int> ErrorStatuses(string operation)
    {
        yield return 401;
        yield return 403;
        yield return 429;
        yield return 500;
        yield return 504;
        if (operation is not ("capabilities" or "host-capabilities" or "validate")) yield return 404;
        if (operation is "validate" or "submit" or "submit-and-activate" or "activate" or "rollback" or
            "compare" or "import" or "import-and-activate" or "backup" or "purge")
        {
            yield return 400;
            yield return 413;
            yield return 415;
        }
        if (operation is "provision" or "submit" or "submit-and-activate" or "activate" or "rollback" or
            "import" or "import-and-activate")
        {
            yield return 409;
            yield return 422;
            yield return 503;
        }
        if (operation == "export") yield return 410;
        if (operation is "backup" or "purge") yield return 503;
    }

    private static GatewayAdminClientOperationSemantics Read(
        string operation, Type success, Type? request = null, bool protectedNotFound = false) =>
        Create(operation, request, success, 200, GatewayAdminClientSuccessMeaning.CompletedRead,
            GatewayAdminClientIdempotency.Forbidden, GatewayAdminClientDesiredPrecondition.Forbidden,
            protectedNotFound, GatewayAdminClientPaginationKind.None);

    private static GatewayAdminClientOperationSemantics Page(string operation, Type success) =>
        Create(operation, null, success, 200, GatewayAdminClientSuccessMeaning.CompletedRead,
            GatewayAdminClientIdempotency.Forbidden, GatewayAdminClientDesiredPrecondition.Forbidden,
            true, GatewayAdminClientPaginationKind.OpaqueCursor);

    private static GatewayAdminClientOperationSemantics Created(
        string operation, Type success, Type? request = null, bool protectedNotFound = true) =>
        Create(operation, request, success, 201, GatewayAdminClientSuccessMeaning.Created,
            GatewayAdminClientIdempotency.Required, GatewayAdminClientDesiredPrecondition.Forbidden,
            protectedNotFound, GatewayAdminClientPaginationKind.None);

    private static GatewayAdminClientOperationSemantics Accepted(
        string operation, Type success, Type request, bool protectedNotFound = true, bool cas = false) =>
        Create(operation, request, success, 202, GatewayAdminClientSuccessMeaning.AcceptedNotActive,
            GatewayAdminClientIdempotency.Required,
            cas ? GatewayAdminClientDesiredPrecondition.CreateOrReplace : GatewayAdminClientDesiredPrecondition.Forbidden,
            protectedNotFound, GatewayAdminClientPaginationKind.None);

    private static GatewayAdminClientOperationSemantics Create(
        string operation,
        Type? request,
        Type success,
        int successStatus,
        GatewayAdminClientSuccessMeaning successMeaning,
        GatewayAdminClientIdempotency idempotency,
        GatewayAdminClientDesiredPrecondition desiredPrecondition,
        bool protectedNotFound,
        GatewayAdminClientPaginationKind pagination) =>
        new(operation, request, success, successStatus, successMeaning, idempotency,
            desiredPrecondition, protectedNotFound, pagination,
            ErrorStatuses(operation).Distinct().Order().ToImmutableArray());
}
