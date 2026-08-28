using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Base;

namespace HPD.Auth.Base;

/// <summary>Executes one retry-stable, bounded Auth expiration cohort.</summary>
internal sealed class AuthExpirationActivationHandler(TimeProvider timeProvider)
    : IBaseActivationHandler<AuthExpirationTriggerInputV1, AuthExpirationResultV1>
{
    public async ValueTask<BaseActivationHandlerResult<AuthExpirationResultV1>> ExecuteAsync(
        BaseActivationContext context,
        AuthExpirationTriggerInputV1 input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        BaseResult<AuthMaintenanceRunReadV1.Row?> observed = await context.Reads.FirstAsync(
            AuthMaintenanceRunReadV1.Handle,
            new AuthMaintenanceRunReadV1 { ActivationId = context.Claim.ActivationId },
            cancellationToken).ConfigureAwait(false);
        if (observed is BaseFailure<AuthMaintenanceRunReadV1.Row?> readFailure)
            return AuthActivationFailureMapper.Map<AuthExpirationResultV1>(readFailure.Error);

        AuthMaintenanceRunReadV1.Row? run = observed.RequireValue();
        if (run is not null && run.Kind != input.Kind)
            return AuthActivationFailureMapper.Domain<AuthExpirationResultV1>("auth.maintenanceRun.kindMismatch");

        DateTimeOffset proposedCutoff = run?.Cutoff ?? timeProvider.GetUtcNow();
        var initialize = new AuthMaintenanceRunInitializeV1
        {
            ActivationId = context.Claim.ActivationId,
            Kind = input.Kind,
            Cutoff = proposedCutoff,
        };
        BaseMutationRequestFingerprint initializeFingerprint = Fingerprint(
            "hpd.auth.maintenance-run.initialize.v1", context.Claim.ActivationId,
            input.Kind.ToString(), proposedCutoff.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        BaseMutationRequestIdentity initializeIdentity = context.DeriveChildIdentity(
            "maintenance-run", 1, initializeFingerprint);
        BaseResult<BaseModuleMutationExecutionResult<AuthMaintenanceRunResultV1>> initialized =
            await context.ExecuteModuleMutationAsync(
                AuthMaintenanceRunInitializeOperationV1.Identity,
                initialize,
                initializeIdentity,
                context.GuardModuleMutation("maintenance-run", 1, initializeFingerprint),
                cancellationToken).ConfigureAwait(false);
        if (initialized is BaseFailure<BaseModuleMutationExecutionResult<AuthMaintenanceRunResultV1>> initializeFailure)
            return AuthActivationFailureMapper.Map<AuthExpirationResultV1>(initializeFailure.Error);

        AuthMaintenanceRunResultV1 authority = initialized.RequireValue().Result;
        BaseResult<BaseSelectionMutationResult> mutation = input.Kind switch
        {
            AuthMaintenanceKindV1.sessionExpiration => await ExpireSessionsAsync(
                context, authority.Cutoff, cancellationToken).ConfigureAwait(false),
            AuthMaintenanceKindV1.refreshExpiration => await DeleteRefreshTokensAsync(
                context, authority.Cutoff, cancellationToken).ConfigureAwait(false),
            AuthMaintenanceKindV1.deliveryExpiration => await DeleteDeliveriesAsync(
                context, authority.Cutoff, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("auth.expiration.kindInvalid"),
        };
        if (mutation is BaseFailure<BaseSelectionMutationResult> mutationFailure)
            return AuthActivationFailureMapper.Map<AuthExpirationResultV1>(mutationFailure.Error);

        BaseSelectionMutationResult result = mutation.RequireValue();
        if (input.Kind == AuthMaintenanceKindV1.deliveryExpiration)
        {
            BaseResult<BaseSelectionMutationResult> maintenance = await DeleteMaintenanceRunsAsync(
                context, authority.Cutoff, cancellationToken).ConfigureAwait(false);
            if (maintenance is BaseFailure<BaseSelectionMutationResult> maintenanceFailure)
                return AuthActivationFailureMapper.Map<AuthExpirationResultV1>(maintenanceFailure.Error);
        }

        return new BaseActivationHandlerResult<AuthExpirationResultV1>
        {
            Result = new AuthExpirationResultV1
            {
                SelectedCount = result.SelectedCount,
                MutatedCount = result.MutatedCount,
                MaintenanceRunId = authority.Id,
                Cutoff = authority.Cutoff,
            },
        };
    }

    private static ValueTask<BaseResult<BaseSelectionMutationResult>> ExpireSessionsAsync(
        BaseActivationContext context,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        BaseCollectionSession<AuthSessionRecordV1> sessions = context.Collection(AuthSessionRecordV1.Collection);
        BaseMutationRequestFingerprint fingerprint = Fingerprint(
            "auth.sessions.expire-due.v1", cutoff.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        BaseMutationRequestIdentity identity = context.DeriveChildIdentity("expire-sessions", 2, fingerprint);
        return sessions.Query()
            .Where(AuthSessionRecordV1.Fields.Revoked.Equal(false))
            .WhereLessThanOrEqual(AuthSessionRecordV1.Fields.ExpiresAt, cutoff)
            .OrderBy(AuthSessionRecordV1.Fields.ExpiresAt)
            .ThenBy(AuthSessionRecordV1.Fields.Id)
            .ThenByRecordId()
            .Take(200)
            .PatchSelectedAsync(
                sessions.GetMergePatchSelectionProfile(AuthSelectionProfiles.SessionsExpireDue),
                SessionExpirationPatch(cutoff),
                RequiresFalse(AuthSessionRecordV1.Fields.Revoked.Id),
                identity,
                context.GuardSelectionMutation("expire-sessions", 2, identity),
                cancellationToken);
    }

    private static ValueTask<BaseResult<BaseSelectionMutationResult>> DeleteRefreshTokensAsync(
        BaseActivationContext context,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        DateTimeOffset retentionCutoff = cutoff.AddDays(-30);
        BaseCollectionSession<AuthRefreshTokenRecordV1> tokens = context.Collection(AuthRefreshTokenRecordV1.Collection);
        BaseMutationRequestFingerprint fingerprint = Fingerprint(
            "auth.refreshTokens.delete-expired.v1",
            retentionCutoff.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        BaseMutationRequestIdentity identity = context.DeriveChildIdentity("expire-refresh-tokens", 2, fingerprint);
        return tokens.Query()
            .WhereLessThanOrEqual(AuthRefreshTokenRecordV1.Fields.ExpiresAt, retentionCutoff)
            .OrderBy(AuthRefreshTokenRecordV1.Fields.ExpiresAt)
            .ThenBy(AuthRefreshTokenRecordV1.Fields.Id)
            .ThenByRecordId()
            .Take(200)
            .DeleteSelectedAsync(
                tokens.GetDeleteSelectionProfile(AuthSelectionProfiles.RefreshTokensDeleteExpired),
                BasePreviousStateRequirement.None,
                identity,
                context.GuardSelectionMutation("expire-refresh-tokens", 2, identity),
                cancellationToken);
    }

    private static ValueTask<BaseResult<BaseSelectionMutationResult>> DeleteDeliveriesAsync(
        BaseActivationContext context,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        BaseCollectionSession<AuthRefreshTokenDeliveryRecordV1> deliveries =
            context.Collection(AuthRefreshTokenDeliveryRecordV1.Collection);
        BaseMutationRequestFingerprint fingerprint = Fingerprint(
            "auth.refreshTokenDeliveries.delete-expired.v1",
            cutoff.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        BaseMutationRequestIdentity identity = context.DeriveChildIdentity("expire-deliveries", 2, fingerprint);
        return deliveries.Query()
            .WhereLessThanOrEqual(AuthRefreshTokenDeliveryRecordV1.Fields.ExpiresAt, cutoff)
            .OrderBy(AuthRefreshTokenDeliveryRecordV1.Fields.ExpiresAt)
            .ThenBy(AuthRefreshTokenDeliveryRecordV1.Fields.Id)
            .ThenByRecordId()
            .Take(200)
            .DeleteSelectedAsync(
                deliveries.GetDeleteSelectionProfile(AuthSelectionProfiles.RefreshTokenDeliveriesDeleteExpired),
                BasePreviousStateRequirement.None,
                identity,
                context.GuardSelectionMutation("expire-deliveries", 2, identity),
                cancellationToken);
    }

    private static ValueTask<BaseResult<BaseSelectionMutationResult>> DeleteMaintenanceRunsAsync(
        BaseActivationContext context,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        DateTimeOffset retentionCutoff = cutoff.AddDays(-35);
        BaseCollectionSession<AuthMaintenanceRunRecordV1> runs = context.Collection(AuthMaintenanceRunRecordV1.Collection);
        BaseMutationRequestFingerprint fingerprint = Fingerprint(
            "auth.maintenanceRuns.delete-expired.v1",
            retentionCutoff.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        BaseMutationRequestIdentity identity = context.DeriveChildIdentity("expire-maintenance-runs", 3, fingerprint);
        return runs.Query()
            .WhereLessThanOrEqual(AuthMaintenanceRunRecordV1.Fields.Cutoff, retentionCutoff)
            .OrderBy(AuthMaintenanceRunRecordV1.Fields.Cutoff)
            .ThenBy(AuthMaintenanceRunRecordV1.Fields.Id)
            .ThenByRecordId()
            .Take(200)
            .DeleteSelectedAsync(
                runs.GetDeleteSelectionProfile(AuthSelectionProfiles.MaintenanceRunsDeleteExpired),
                BasePreviousStateRequirement.None,
                identity,
                context.GuardSelectionMutation("expire-maintenance-runs", 3, identity),
                cancellationToken);
    }

    private static BasePreviousStateRequirement RequiresFalse(string fieldId) => new()
    {
        Revision = new BaseRevisionRequirement { Kind = BaseRevisionRequirementKind.None },
        Fields =
        [
            new BasePreviousFieldRequirement
            {
                FieldId = fieldId,
                Kind = BasePreviousFieldRequirementKind.Equal,
                Value = new QueryValue { Kind = QueryValueKind.Boolean, Boolean = false },
            },
        ],
    };

    private static RecordPatchRequest SessionExpirationPatch(DateTimeOffset cutoff)
    {
        Dictionary<string, JsonElement> fields = new(StringComparer.Ordinal)
        {
            [Wire(AuthSessionRecordV1.Fields.RetentionEligibleAt)] = Instant(cutoff.AddDays(30)),
            [Wire(AuthSessionRecordV1.Fields.Revoked)] = Literal("true"),
            [Wire(AuthSessionRecordV1.Fields.RevokedAt)] = Instant(cutoff),
            [Wire(AuthSessionRecordV1.Fields.State)] = Literal("\"loggedOut\""),
        };
        return new RecordPatchRequest
        {
            Patch = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields },
            RemovedFieldIds = ImmutableArray<string>.Empty,
        };
    }

    private static string Wire<T>(BaseField<AuthSessionRecordV1, T> field) =>
        AuthSessionRecordV1.Collection.Definition.Fields!
            .Single(candidate => candidate.Id == field.Id).WireName;

    private static JsonElement Instant(DateTimeOffset value) => Literal(
        "\"" + value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            System.Globalization.CultureInfo.InvariantCulture) + "\"");

    private static JsonElement Literal(string canonicalJson)
    {
        using JsonDocument document = JsonDocument.Parse(canonicalJson);
        return document.RootElement.Clone();
    }

    private static BaseMutationRequestFingerprint Fingerprint(string domain, params string[] values)
    {
        using var buffer = new MemoryStream();
        Write(buffer, domain);
        foreach (string value in values)
            Write(buffer, value);
        return BaseMutationRequestFingerprint.Create(
            SHA256.HashData(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length))));
    }

    private static void Write(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }
}

internal static class AuthActivationFailureMapper
{
    internal static BaseActivationHandlerResult<TResult> Map<TResult>(BaseError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        string code = error.Code;
        if (code is "base.selection.timeout" or "base.runtime.transaction.timeout"
            or "base.moduleMutation.storeError" or "base.selection.transactionConflict"
            or "base.activation.claimUnavailable")
        {
            return new BaseActivationHandlerResult<TResult>
            {
                FailureCode = "auth.persistence.unavailable",
                Retryable = true,
            };
        }
        if (code.Contains("indeterminate", StringComparison.OrdinalIgnoreCase)
            || code.Contains("outcomeUnknown", StringComparison.Ordinal))
        {
            return new BaseActivationHandlerResult<TResult>
            {
                FailureCode = "auth.operation.outcomeUnknown",
                Retryable = false,
            };
        }
        if (code.StartsWith("auth.", StringComparison.Ordinal))
            return Domain<TResult>(code);
        return new BaseActivationHandlerResult<TResult>
        {
            FailureCode = "auth.persistence.unavailable",
            Retryable = false,
        };
    }

    internal static BaseActivationHandlerResult<TResult> Domain<TResult>(string code) => new()
    {
        FailureCode = code,
        Retryable = false,
    };
}
