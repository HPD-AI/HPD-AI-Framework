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
internal enum GatewayAdminClientRequestBodyPresence : byte { None, Required, Optional }

internal sealed record GatewayAdminClientPaginationSpecification(
    GatewayAdminClientPaginationKind Kind,
    int? DefaultMaximum,
    int? MinimumMaximum,
    int? MaximumMaximum)
{
    internal static GatewayAdminClientPaginationSpecification None { get; } =
        new(GatewayAdminClientPaginationKind.None, null, null, null);

    internal static GatewayAdminClientPaginationSpecification OpaqueCursorV1 { get; } =
        new(GatewayAdminClientPaginationKind.OpaqueCursor, 64, 1, 256);

    internal void Validate()
    {
        if (Kind == GatewayAdminClientPaginationKind.None)
        {
            if (DefaultMaximum is not null || MinimumMaximum is not null || MaximumMaximum is not null)
                throw new InvalidOperationException("Non-paged operations cannot declare pagination bounds.");
            return;
        }
        if (DefaultMaximum is not { } value || MinimumMaximum is not { } minimum ||
            MaximumMaximum is not { } maximum || minimum < 1 || minimum > value || value > maximum)
            throw new InvalidOperationException("Opaque cursor pagination requires an ordered positive minimum, default, and maximum.");
    }
}

internal sealed record GatewayAdminClientOperationSemantics(
    string Operation,
    Type? RequestType,
    GatewayAdminClientRequestBodyPresence RequestBodyPresence,
    Type SuccessType,
    int SuccessStatus,
    GatewayAdminClientSuccessMeaning SuccessMeaning,
    GatewayAdminClientIdempotency Idempotency,
    GatewayAdminClientDesiredPrecondition DesiredPrecondition,
    bool ProtectedNotFound,
    GatewayAdminClientPaginationSpecification Pagination,
    ImmutableArray<GatewayAdminClientParameterConstraint> ParameterConstraints,
    ImmutableArray<int> DocumentedErrors);

internal static class GatewayAdminClientSemanticLedger
{
    private static readonly ImmutableArray<int> PublicReadErrors = [401, 403, 429, 500, 504];
    private static readonly ImmutableArray<int> ProtectedReadErrors = [401, 403, 404, 429, 500, 504];
    private static readonly ImmutableArray<int> PublicBodyErrors = [400, 401, 403, 413, 415, 429, 500, 504];
    private static readonly ImmutableArray<int> ProtectedBodyErrors = [400, 401, 403, 404, 413, 415, 429, 500, 504];
    private static readonly ImmutableArray<int> ProvisionErrors = [401, 403, 404, 409, 422, 429, 500, 503, 504];
    private static readonly ImmutableArray<int> MutationBodyErrors = [400, 401, 403, 404, 409, 413, 415, 422, 429, 500, 503, 504];
    private static readonly ImmutableArray<int> AdministrationErrors = [400, 401, 403, 404, 413, 415, 429, 500, 503, 504];
    private static readonly ImmutableArray<int> ExportErrors = [401, 403, 404, 410, 429, 500, 504];

    internal static ImmutableArray<GatewayAdminClientOperationSemantics> V1 { get; } =
    [
        Read("capabilities", typeof(GatewayCapabilityCatalog), PublicReadErrors, GatewayAdminClientParameterProfiles.Global),
        Read("host-capabilities", typeof(GatewayHostCapabilitySnapshotResponse), PublicReadErrors, GatewayAdminClientParameterProfiles.Global),
        Read("validate", typeof(GatewayValidationResponse), PublicBodyErrors, GatewayAdminClientParameterProfiles.Global, typeof(GatewayConfiguration)),
        Created("provision", typeof(GatewayProvisionResponse), ProvisionErrors, GatewayAdminClientParameterProfiles.TargetMutation),
        Read("desired", typeof(GatewayDesiredProjection), ProtectedReadErrors, GatewayAdminClientParameterProfiles.TargetRead, protectedNotFound: true),
        Read("status", typeof(GatewayTargetStatusResponse), ProtectedReadErrors, GatewayAdminClientParameterProfiles.TargetRead, protectedNotFound: true),
        Read("effective", typeof(GatewayEffectiveSnapshot), ProtectedReadErrors, GatewayAdminClientParameterProfiles.TargetRead, protectedNotFound: true),
        Created("submit", typeof(GatewayRevisionResponse), MutationBodyErrors, GatewayAdminClientParameterProfiles.TargetMutation, typeof(GatewayRevisionRequest)),
        Accepted("submit-and-activate", typeof(GatewayRevisionResponse), MutationBodyErrors, GatewayAdminClientParameterProfiles.TargetCas, typeof(GatewayRevisionRequest), cas: true),
        Page("revisions", typeof(GatewayAdminPage<GatewayRevisionProjection>), ProtectedReadErrors, GatewayAdminClientParameterProfiles.TargetPage),
        Read("revision", typeof(GatewayRevisionProjection), ProtectedReadErrors, GatewayAdminClientParameterProfiles.TargetRevisionRead, protectedNotFound: true),
        Read("validation", typeof(GatewayValidationProjection), ProtectedReadErrors, GatewayAdminClientParameterProfiles.TargetValidationRead, protectedNotFound: true),
        Accepted("activate", typeof(GatewayRevisionResponse), MutationBodyErrors, GatewayAdminClientParameterProfiles.TargetRevisionCas, typeof(GatewayActivationRequest), cas: true, bodyOptional: true),
        Accepted("rollback", typeof(GatewayRevisionResponse), MutationBodyErrors, GatewayAdminClientParameterProfiles.TargetRevisionCas, typeof(GatewayActivationRequest), cas: true, bodyOptional: true),
        Page("activations", typeof(GatewayActivationHistoryResponse), ProtectedReadErrors, GatewayAdminClientParameterProfiles.TargetPage),
        Read("compare", typeof(GatewayRevisionComparison), ProtectedBodyErrors, GatewayAdminClientParameterProfiles.TargetRead, typeof(GatewayCompareRequest), protectedNotFound: true),
        Read("export", typeof(GatewayExportResponse), ExportErrors, GatewayAdminClientParameterProfiles.TargetRevisionRead, protectedNotFound: true),
        Created("import", typeof(GatewayRevisionResponse), MutationBodyErrors, GatewayAdminClientParameterProfiles.TargetMutation, typeof(GatewayImportRequest)),
        Accepted("import-and-activate", typeof(GatewayRevisionResponse), MutationBodyErrors, GatewayAdminClientParameterProfiles.TargetCas, typeof(GatewayImportRequest), cas: true),
        Read("operation", typeof(GatewayOperationProjection), ProtectedReadErrors, GatewayAdminClientParameterProfiles.NamespaceOperation, protectedNotFound: true),
        Page("audit", typeof(GatewayAdminPage<GatewayAuditProjection>), ProtectedReadErrors, GatewayAdminClientParameterProfiles.NamespacePage),
        Accepted("backup", typeof(GatewayAdministrativeResponse), AdministrationErrors, GatewayAdminClientParameterProfiles.NamespaceMutation, typeof(GatewayBackupRequest)),
        Accepted("purge", typeof(GatewayAdministrativeResponse), AdministrationErrors, GatewayAdminClientParameterProfiles.NamespaceMutation, typeof(GatewayPurgeRequest)),
    ];

    internal static GatewayAdminClientOperationSemantics For(string operation) =>
        V1.Single(item => StringComparer.Ordinal.Equals(item.Operation, operation));

    private static GatewayAdminClientOperationSemantics Read(
        string operation, Type success, ImmutableArray<int> errors,
        ImmutableArray<GatewayAdminClientParameterConstraint> parameters,
        Type? request = null, bool protectedNotFound = false) =>
        Create(operation, request, success, 200, GatewayAdminClientSuccessMeaning.CompletedRead,
            GatewayAdminClientIdempotency.Forbidden, GatewayAdminClientDesiredPrecondition.Forbidden,
            protectedNotFound, GatewayAdminClientPaginationSpecification.None, parameters, errors);

    private static GatewayAdminClientOperationSemantics Page(
        string operation, Type success, ImmutableArray<int> errors,
        ImmutableArray<GatewayAdminClientParameterConstraint> parameters) =>
        Create(operation, null, success, 200, GatewayAdminClientSuccessMeaning.CompletedRead,
            GatewayAdminClientIdempotency.Forbidden, GatewayAdminClientDesiredPrecondition.Forbidden,
            true, GatewayAdminClientPaginationSpecification.OpaqueCursorV1, parameters, errors);

    private static GatewayAdminClientOperationSemantics Created(
        string operation, Type success, ImmutableArray<int> errors,
        ImmutableArray<GatewayAdminClientParameterConstraint> parameters, Type? request = null) =>
        Create(operation, request, success, 201, GatewayAdminClientSuccessMeaning.Created,
            GatewayAdminClientIdempotency.Required, GatewayAdminClientDesiredPrecondition.Forbidden,
            true, GatewayAdminClientPaginationSpecification.None, parameters, errors);

    private static GatewayAdminClientOperationSemantics Accepted(
        string operation, Type success, ImmutableArray<int> errors,
        ImmutableArray<GatewayAdminClientParameterConstraint> parameters, Type request,
        bool cas = false, bool bodyOptional = false) =>
        Create(operation, request, success, 202, GatewayAdminClientSuccessMeaning.AcceptedNotActive,
            GatewayAdminClientIdempotency.Required,
            cas ? GatewayAdminClientDesiredPrecondition.CreateOrReplace : GatewayAdminClientDesiredPrecondition.Forbidden,
            true, GatewayAdminClientPaginationSpecification.None, parameters, errors, bodyOptional);

    private static GatewayAdminClientOperationSemantics Create(
        string operation,
        Type? request,
        Type success,
        int successStatus,
        GatewayAdminClientSuccessMeaning successMeaning,
        GatewayAdminClientIdempotency idempotency,
        GatewayAdminClientDesiredPrecondition desiredPrecondition,
        bool protectedNotFound,
        GatewayAdminClientPaginationSpecification pagination,
        ImmutableArray<GatewayAdminClientParameterConstraint> parameters,
        ImmutableArray<int> documentedErrors,
        bool bodyOptional = false) =>
        CreateValidated(operation, request, success, successStatus, successMeaning, idempotency,
            desiredPrecondition, protectedNotFound, pagination, parameters, documentedErrors, bodyOptional);

    private static GatewayAdminClientOperationSemantics CreateValidated(
        string operation, Type? request, Type success, int successStatus,
        GatewayAdminClientSuccessMeaning successMeaning, GatewayAdminClientIdempotency idempotency,
        GatewayAdminClientDesiredPrecondition desiredPrecondition, bool protectedNotFound,
        GatewayAdminClientPaginationSpecification pagination,
        ImmutableArray<GatewayAdminClientParameterConstraint> parameters,
        ImmutableArray<int> documentedErrors,
        bool bodyOptional)
    {
        pagination.Validate();
        return new(operation, request, request is null ? GatewayAdminClientRequestBodyPresence.None :
            bodyOptional ? GatewayAdminClientRequestBodyPresence.Optional : GatewayAdminClientRequestBodyPresence.Required,
            success, successStatus, successMeaning, idempotency,
            desiredPrecondition, protectedNotFound, pagination, parameters, documentedErrors);
    }
}
