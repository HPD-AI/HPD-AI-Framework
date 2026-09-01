using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using HPD.Auth.Base;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Infrastructure.Base;
using HPD.Base;

namespace HPD.Auth.Infrastructure.Stores;

/// <summary>Persists Auth sessions through registered HPD Base reads, operations, and selection profiles.</summary>
internal sealed class AuthBaseSessionStore(AuthBaseRuntime runtime) : ISessionManager
{
    private static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromDays(14);

    /// <inheritdoc />
    public async Task<UserSession> CreateSessionAsync(Guid userId, SessionContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        AuthUserByIdReadV1.Row user = await RequireUserAsync(userId, ct).ConfigureAwait(false);
        DateTimeOffset now = runtime.GetUtcNow();
        Guid sessionId = Guid.NewGuid();
        var request = new AuthSessionCreateV1
        {
            SessionId = sessionId, TenantId = runtime.TenantId, UserId = userId,
            ExpectedUserRevision = user.Revision, Aal = ParseAal(context.AAL),
            State = AuthSessionStateV1.active, IpAddress = context.IpAddress,
            UserAgent = context.UserAgent, CreatedAt = now, LastActiveAt = now,
            ExpiresAt = now.Add(context.Lifetime ?? DefaultSessionLifetime), Revoked = false,
            OperationTime = now,
        };
        BaseInstalledModuleMutationHandle<AuthSessionCreateV1, AuthSessionCreateResultV1> operation = runtime
            .OpenServiceSession().ModuleMutations.Get(AuthSessionCreateOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
            request, $"session:{sessionId:D}:create");
        BaseResult<BaseModuleMutationExecutionResult<AuthSessionCreateResultV1>> result = await operation
            .ExecuteAsync(request, identity, cancellationToken: ct).ConfigureAwait(false);
        if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthSessionCreateResultV1>> failure)
            throw Failure(failure.Error);
        return new UserSession
        {
            Id = sessionId, InstanceId = runtime.TenantId, UserId = userId,
            AAL = context.AAL, IpAddress = context.IpAddress, UserAgent = context.UserAgent,
            CreatedAt = now.UtcDateTime, LastActiveAt = now.UtcDateTime,
            ExpiresAt = request.ExpiresAt.UtcDateTime, IsRevoked = false, SessionState = "active",
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSession>> GetActiveSessionsAsync(Guid userId, CancellationToken ct = default)
    {
        BaseResult<AuthActiveSessionsReadV1.Row[]> result = await runtime.OpenServiceSession().Reads.ToArrayAsync(
            AuthActiveSessionsReadV1.Handle,
            new AuthActiveSessionsReadV1
            {
                TenantId = runtime.TenantId,
                UserId = BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D")),
                Now = runtime.GetUtcNow(),
            }, ct).ConfigureAwait(false);
        if (result is BaseFailure<AuthActiveSessionsReadV1.Row[]> failure)
            throw Failure(failure.Error);
        return result.RequireValue().Select(Map).ToArray();
    }

    /// <inheritdoc />
    public async Task<UserSession> TouchSessionAsync(
        Guid userId,
        Guid sessionId,
        SessionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        AuthUserByIdReadV1.Row user = await RequireUserAsync(userId, ct).ConfigureAwait(false);
        DateTimeOffset now = runtime.GetUtcNow();
        BaseResult<AuthActiveSessionsReadV1.Row[]> sessions = await runtime.OpenServiceSession().Reads.ToArrayAsync(
            AuthActiveSessionsReadV1.Handle,
            new AuthActiveSessionsReadV1
            {
                TenantId = runtime.TenantId,
                UserId = BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D")),
                Now = now,
            }, ct).ConfigureAwait(false);
        if (sessions is BaseFailure<AuthActiveSessionsReadV1.Row[]> readFailure)
            throw Failure(readFailure.Error);
        AuthActiveSessionsReadV1.Row current = sessions.RequireValue()
            .SingleOrDefault(candidate => candidate.Id == sessionId)
            ?? throw new AuthBasePersistenceException("auth.session.notFound");
        var request = new AuthSessionTouchV1
        {
            SessionId = sessionId,
            TenantId = runtime.TenantId,
            UserId = userId,
            SsoProviderId = current.SsoProviderId,
            ExpectedUserRevision = user.Revision,
            ExpectedSessionRevision = current.Revision,
            LastActiveAt = now,
            IpAddress = context.IpAddress,
            UserAgent = context.UserAgent,
            DeviceInfo = current.DeviceInfo,
            OperationTime = now,
        };
        BaseInstalledModuleMutationHandle<AuthSessionTouchV1, AuthSessionTouchResultV1> operation = runtime
            .OpenServiceSession().ModuleMutations.Get(AuthSessionTouchOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
            request,
            $"session:{sessionId:D}:revision:{current.Revision.Value}:touch");
        BaseResult<BaseModuleMutationExecutionResult<AuthSessionTouchResultV1>> result = await operation
            .ExecuteAsync(request, identity, cancellationToken: ct).ConfigureAwait(false);
        if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthSessionTouchResultV1>> failure)
            throw Failure(failure.Error);
        UserSession committed = Map(current);
        committed.IpAddress = context.IpAddress;
        committed.UserAgent = context.UserAgent;
        committed.LastActiveAt = now.UtcDateTime;
        return committed;
    }

    /// <inheritdoc />
    public Task RevokeSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        RevokeAsync(null, sessionId, null, ct);

    /// <inheritdoc />
    public Task RevokeAllSessionsAsync(Guid userId, Guid? exceptSessionId = null, CancellationToken ct = default) =>
        RevokeAsync(userId, null, exceptSessionId, ct);

    private async Task RevokeAsync(Guid? userId, Guid? sessionId, Guid? exceptSessionId, CancellationToken ct)
    {
        DateTimeOffset now = runtime.GetUtcNow();
        BaseCollectionSession<AuthSessionRecordV1> sessions = runtime.OpenServiceSession().Collection(AuthSessionRecordV1.Collection);
        BaseMergePatchSelectionProfile<AuthSessionRecordV1> profile =
            sessions.GetMergePatchSelectionProfile(AuthSelectionProfiles.SessionsRevokeUser);
        RecordPatchRequest patch = RevocationPatch(now);
        for (int chunk = 0; ; chunk++)
        {
            BaseQuery<AuthSessionRecordV1> query = sessions.Query()
                .Where(AuthSessionRecordV1.Fields.TenantId.Equal(runtime.TenantId))
                .Where(AuthSessionRecordV1.Fields.Revoked.Equal(false));
            if (userId is Guid owner)
                query = query.Where(AuthSessionRecordV1.Fields.UserId.Equal(BaseRecordId<AuthUserRecordV1>.Create(owner.ToString("D"))));
            if (sessionId is Guid exact)
                query = query.Where(AuthSessionRecordV1.Fields.Id.Equal(exact));
            if (exceptSessionId is Guid excluded)
                query = query.Where(AuthSessionRecordV1.Fields.Id.NotEqual(excluded));
            query = query.OrderBy(AuthSessionRecordV1.Fields.Id).ThenByRecordId().Take(200);
            string subject = userId?.ToString("D") ?? sessionId!.Value.ToString("D");
            BaseMutationRequestIdentity identity = AuthBaseRuntime.MutationIdentity(
                "auth.sessions.revoke-user.v1", runtime.TenantId, subject,
                chunk.ToString(System.Globalization.CultureInfo.InvariantCulture),
                exceptSessionId?.ToString("D"), now.ToString("O"));
            BaseResult<BaseSelectionMutationResult> result = await query.PatchSelectedAsync(
                profile, patch, BasePreviousStateRequirement.None, identity,
                cancellationToken: ct).ConfigureAwait(false);
            if (result is BaseFailure<BaseSelectionMutationResult> failure)
                throw Failure(failure.Error);
            if (result.RequireValue().SelectedCount == 0 || sessionId.HasValue)
                return;
        }
    }

    private async Task<AuthUserByIdReadV1.Row> RequireUserAsync(Guid userId, CancellationToken ct)
    {
        BaseResult<AuthUserByIdReadV1.Row?> result = await runtime.OpenServiceSession().Reads.FirstAsync(
            AuthUserByIdReadV1.Handle,
            new AuthUserByIdReadV1 { TenantId = runtime.TenantId, UserId = userId }, ct).ConfigureAwait(false);
        if (result is BaseFailure<AuthUserByIdReadV1.Row?> failure)
            throw Failure(failure.Error);
        return result.RequireValue() ?? throw new AuthBasePersistenceException("auth.user.notFound");
    }

    private static RecordPatchRequest RevocationPatch(DateTimeOffset now)
    {
        Dictionary<string, JsonElement> fields = new(StringComparer.Ordinal)
        {
            [Wire(AuthSessionRecordV1.Fields.RetentionEligibleAt)] = UtcDateTime(now.AddDays(30)),
            [Wire(AuthSessionRecordV1.Fields.Revoked)] = JsonSerializer.SerializeToElement(
                true, AuthBaseJsonSerializerContext.Default.Boolean),
            [Wire(AuthSessionRecordV1.Fields.RevokedAt)] = UtcDateTime(now),
            [Wire(AuthSessionRecordV1.Fields.State)] = JsonSerializer.SerializeToElement(
                "loggedOut", AuthBaseJsonSerializerContext.Default.String),
        };
        return new RecordPatchRequest
        {
            Patch = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields },
            RemovedFieldIds = ImmutableArray<string>.Empty,
        };
    }

    private static string Wire<T>(BaseField<AuthSessionRecordV1, T> field) =>
        AuthSessionRecordV1.Collection.Definition.Fields!.Single(candidate => candidate.Id == field.Id).WireName;

    private static JsonElement UtcDateTime(DateTimeOffset value) => JsonSerializer.SerializeToElement(
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture),
        AuthBaseJsonSerializerContext.Default.String);

    private static AuthSessionAssuranceLevelV1 ParseAal(string value) => value switch
    {
        "aal1" => AuthSessionAssuranceLevelV1.aal1,
        "aal2" => AuthSessionAssuranceLevelV1.aal2,
        "aal3" => AuthSessionAssuranceLevelV1.aal3,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "The session assurance level is invalid."),
    };

    private static UserSession Map(AuthActiveSessionsReadV1.Row row) => new()
    {
        Id = row.Id, InstanceId = row.TenantId,
        UserId = Guid.ParseExact(row.UserId.Value.Value, "D"), AAL = row.Aal.ToString(),
        BrokerSessionId = row.BrokerSessionId, BrokerUserId = row.BrokerUserId,
        SSOProviderId = row.SsoProviderId is null ? null : Guid.ParseExact(row.SsoProviderId.Value.Value.Value, "D"),
        NotBefore = row.NotBefore?.UtcDateTime, NotAfter = row.NotAfter?.UtcDateTime,
        OAuthClientId = row.OauthClientId, Scopes = row.Scopes,
        ClientSessions = row.ClientSessions is null ? null : Encoding.UTF8.GetString(row.ClientSessions.Value.Utf8.Span),
        SessionState = row.State switch { AuthSessionStateV1.active => "active", AuthSessionStateV1.loggingOut => "logging_out", _ => "logged_out" },
        IpAddress = row.IpAddress, UserAgent = row.UserAgent, DeviceInfo = row.DeviceInfo,
        CreatedAt = row.CreatedAt.UtcDateTime, LastActiveAt = row.LastActiveAt.UtcDateTime,
        ExpiresAt = row.ExpiresAt.UtcDateTime, IsRevoked = false,
    };

    private static AuthBasePersistenceException Failure(BaseError error) => new(error.Code);
}
