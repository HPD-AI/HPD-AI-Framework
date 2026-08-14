using System.Data;
using System.Data.Common;
using HPD.Payments.Persistence.AtomicDomains;
using HPD.Payments.Persistence.Ports;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Supporting.Ownership;
using HPD.Payments.Supporting.Relations;

#pragma warning disable CA2007 // Async-disposable configuration would obscure the transaction protocol; all task awaits are configured.
#pragma warning disable CA2100 // SQL text is internal and the only interpolated identifier is constructor-validated.
#pragma warning disable CA1863 // Each schema-qualified statement is constructed once per operation.
#pragma warning disable CA1305 // The substituted schema is validated lower-case ASCII and culture-invariant.

namespace HPD.Payments.Adapters.Postgres;

/// <summary>Executes the PostgreSQL mechanics for the certified D-OWNER, D-REL, and D-CONT boundaries.</summary>
/// <remarks>
/// Every mutating operation uses a serializable transaction and explicit row locks. Broker sends, provider calls,
/// projections, remote stores, and plugin hosts are never included. A connection/commit exception is reported as
/// indeterminate; callers must reconcile by stable identity before retrying.
/// </remarks>
public sealed class PostgresAtomicStore
{
    private readonly PostgresAdapterOptions _options;

    /// <summary>Creates a PostgreSQL atomic store over a provider-owned data source.</summary>
    /// <param name="options">Validated data source, topology revision, schema, and timeout.</param>
    public PostgresAtomicStore(PostgresAdapterOptions options) => _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Creates the append-only adapter schema using idempotent DDL.</summary>
    /// <param name="cancellationToken">Cooperative cancellation before or during execution.</param>
    /// <remarks>Migration execution is operational evidence only; it does not certify a transaction domain.</remarks>
    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _options.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = PostgresSql.CreateSchema(_options.Schema);
        command.CommandTimeout = checked((int)Math.Ceiling(_options.CommandTimeout.TotalSeconds));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Compare-binds one owner generation and appends immutable canonical bytes inside D-OWNER.</summary>
    /// <param name="owner">Exact owner conflict scope and expected generation.</param>
    /// <param name="semanticDigest">Canonical replay/conflict digest.</param>
    /// <param name="payload">Owned canonical fact bytes; copied by the provider parameter.</param>
    /// <param name="domain">Exact D-OWNER domain and topology revision.</param>
    /// <param name="epoch">Positive takeover epoch.</param>
    /// <param name="fence">Positive fencing token for stale-writer rejection.</param>
    /// <param name="cancellationToken">Cancellation may produce an indeterminate result after dispatch.</param>
    /// <returns>A receipt scoped to the database transaction; it makes no external-system claim.</returns>
    public async ValueTask<PostgresTransactionReceipt> CompareBindAppendAsync(OwnerReference owner, CanonicalDigest semanticDigest,
        ReadOnlyMemory<byte> payload, AtomicDomain domain, ulong epoch, ulong fence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(semanticDigest);
        ValidateOwnerDomain(owner, domain, AtomicDomainKind.DistributedOwner);
        if (payload.IsEmpty || payload.Length > 4 * 1024 * 1024 || epoch == 0 || fence == 0) throw new ArgumentException("Payload, epoch, and fence must be bounded and positive.");

        await using var connection = await _options.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            var scope = owner.SubjectId.Scope.ToString();
            var subject = Identity(owner.SubjectId);
            await using var locked = await ExecuteReaderAsync(connection, transaction, string.Format(PostgresSql.LockOwner, _options.Schema), cancellationToken,
                ("scope", scope), ("authority", (short)owner.Authority), ("subject", subject)).ConfigureAwait(false);
            var exists = await locked.ReadAsync(cancellationToken).ConfigureAwait(false);
            ulong generation = 0, lockedEpoch = 0, lockedFence = 0, topology = 0;
            if (exists)
            {
                generation = checked((ulong)locked.GetInt64(0)); lockedEpoch = checked((ulong)locked.GetInt64(1));
                lockedFence = checked((ulong)locked.GetInt64(2)); topology = checked((ulong)locked.GetInt64(3));
            }
            await locked.DisposeAsync().ConfigureAwait(false);

            if (exists && (generation != owner.Generation.Value || epoch < lockedEpoch || fence < lockedFence || topology != _options.TopologyRevision.Value))
            { await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); return Receipt(domain, PostgresTransactionOutcome.Conflict, lockedEpoch, lockedFence, "owner-guard-conflict"); }

            var next = exists ? checked(generation + 1) : owner.Generation.Value;
            var inserted = await ExecuteAsync(connection, transaction, $"INSERT INTO {_options.Schema}.owner_facts(scope,authority,subject,generation,semantic_digest,payload) VALUES(@scope,@authority,@subject,@generation,@digest,@payload) ON CONFLICT DO NOTHING", cancellationToken,
                ("scope", scope), ("authority", (short)owner.Authority), ("subject", subject), ("generation", checked((long)next)), ("digest", semanticDigest.CopyBytes()), ("payload", payload.ToArray())).ConfigureAwait(false);
            if (inserted == 0)
            { await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); return Receipt(domain, PostgresTransactionOutcome.Conflict, exists ? lockedEpoch : epoch, exists ? lockedFence : fence, "fact-replay-or-conflict"); }

            await ExecuteAsync(connection, transaction, $"INSERT INTO {_options.Schema}.owner_heads(scope,authority,subject,generation,epoch,fence,topology_revision) VALUES(@scope,@authority,@subject,@generation,@epoch,@fence,@topology) ON CONFLICT(scope,authority,subject) DO UPDATE SET generation=EXCLUDED.generation,epoch=EXCLUDED.epoch,fence=EXCLUDED.fence,topology_revision=EXCLUDED.topology_revision", cancellationToken,
                ("scope", scope), ("authority", (short)owner.Authority), ("subject", subject), ("generation", checked((long)next)), ("epoch", checked((long)epoch)), ("fence", checked((long)fence)), ("topology", checked((long)_options.TopologyRevision.Value))).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Receipt(domain, PostgresTransactionOutcome.Committed, epoch, fence, "owner-appended");
        }
        catch (OperationCanceledException) { return Receipt(domain, PostgresTransactionOutcome.Indeterminate, epoch, fence, "commit-boundary-cancelled"); }
        catch (DbException) { return Receipt(domain, PostgresTransactionOutcome.Indeterminate, epoch, fence, "database-outcome-unknown"); }
    }

    /// <summary>Guards both endpoint generations and inserts one immutable relation inside one D-REL boundary.</summary>
    /// <remarks>If the endpoints are not co-located in the supplied domain, this method returns Unsupported before opening a connection.</remarks>
    public async ValueTask<PostgresTransactionReceipt> GuardedRelateAsync(SupportingRelation relation, AtomicDomain domain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relation);
        if (!domain.IsValid) throw new ArgumentException("A valid relation domain is required.", nameof(domain));
        if (domain.Kind != AtomicDomainKind.DistributedRelation || domain.TopologyRevision != _options.TopologyRevision || relation.RelationId.Scope != domain.DomainId.Scope)
            return Receipt(domain, PostgresTransactionOutcome.Unsupported, 0, 0, "relation-boundary-unsupported");
        await using var connection = await _options.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            var endpoints = new[] { relation.Source, relation.Target }.OrderBy(static x => x.Authority).ThenBy(static x => Identity(x.SubjectId), StringComparer.Ordinal).ToArray();
            await using var reader = await ExecuteReaderAsync(connection, transaction, string.Format(PostgresSql.LockRelationEndpoints, _options.Schema), cancellationToken,
                ("scope", relation.RelationId.Scope.ToString()), ("a1", (short)endpoints[0].Authority), ("s1", Identity(endpoints[0].SubjectId)),
                ("a2", (short)endpoints[1].Authority), ("s2", Identity(endpoints[1].SubjectId))).ConfigureAwait(false);
            var observed = new List<(short Authority, string Subject, ulong Generation)>(2);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) observed.Add((reader.GetInt16(0), reader.GetString(1), checked((ulong)reader.GetInt64(2))));
            await reader.DisposeAsync().ConfigureAwait(false);
            if (observed.Count != 2 || endpoints.Any(e => !observed.Contains(((short)e.Authority, Identity(e.SubjectId), e.Generation.Value))))
            { await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); return Receipt(domain, PostgresTransactionOutcome.Conflict, 0, 0, "endpoint-generation-conflict"); }
            await ExecuteAsync(connection, transaction, $"INSERT INTO {_options.Schema}.relations(scope,relation_id,relation_kind,source_authority,source_subject,source_generation,target_authority,target_subject,target_generation,relation_revision,state,residue_code) VALUES(@scope,@id,@kind,@sa,@ss,@sg,@ta,@ts,@tg,@revision,1,'none') ON CONFLICT DO NOTHING", cancellationToken,
                ("scope", relation.RelationId.Scope.ToString()), ("id", Identity(relation.RelationId)), ("kind", (short)relation.Kind),
                ("sa", (short)relation.Source.Authority), ("ss", Identity(relation.Source.SubjectId)), ("sg", checked((long)relation.Source.Generation.Value)),
                ("ta", (short)relation.Target.Authority), ("ts", Identity(relation.Target.SubjectId)), ("tg", checked((long)relation.Target.Generation.Value)), ("revision", checked((long)relation.RelationRevision.Value))).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Receipt(domain, PostgresTransactionOutcome.Committed, 0, 0, "relation-committed");
        }
        catch (OperationCanceledException) { return Receipt(domain, PostgresTransactionOutcome.Indeterminate, 0, 0, "relation-outcome-unknown"); }
        catch (DbException) { return Receipt(domain, PostgresTransactionOutcome.Indeterminate, 0, 0, "relation-outcome-unknown"); }
    }

    /// <summary>Atomically verifies the owner fact and records its authority-created continuation inside D-CONT.</summary>
    /// <remarks>The commit makes the continuation discoverable in PostgreSQL; it does not enqueue, send, or execute it.</remarks>
    public async ValueTask<PostgresTransactionReceipt> CommitContinuationAsync(ContinuationDeclaration continuation, AtomicDomain domain,
        ulong epoch, ulong fence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        if (!domain.IsValid) throw new ArgumentException("A valid continuation domain is required.", nameof(domain));
        if (domain.Kind != AtomicDomainKind.DistributedContinuation || domain.TopologyRevision != _options.TopologyRevision || continuation.Owner.SubjectId.Scope != domain.DomainId.Scope)
            return Receipt(domain, PostgresTransactionOutcome.Unsupported, 0, 0, "continuation-boundary-unsupported");
        if (epoch == 0 || fence == 0) throw new ArgumentOutOfRangeException(nameof(epoch), "Epoch and fence must be positive.");
        await using var connection = await _options.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            await using var reader = await ExecuteReaderAsync(connection, transaction, string.Format(PostgresSql.LockOwner, _options.Schema), cancellationToken,
                ("scope", continuation.Owner.SubjectId.Scope.ToString()), ("authority", (short)continuation.Owner.Authority), ("subject", Identity(continuation.Owner.SubjectId))).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || checked((ulong)reader.GetInt64(0)) < continuation.Owner.Generation.Value ||
                epoch < checked((ulong)reader.GetInt64(1)) || fence < checked((ulong)reader.GetInt64(2)))
            { await reader.DisposeAsync().ConfigureAwait(false); await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); return Receipt(domain, PostgresTransactionOutcome.Conflict, epoch, fence, "continuation-owner-conflict"); }
            await reader.DisposeAsync().ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, $"INSERT INTO {_options.Schema}.continuations(scope,owner_authority,owner_subject,owner_generation,continuation_id,digest,state,lease_epoch,fence) VALUES(@scope,@authority,@subject,@generation,@id,@digest,1,@epoch,@fence) ON CONFLICT DO NOTHING", cancellationToken,
                ("scope", continuation.Owner.SubjectId.Scope.ToString()), ("authority", (short)continuation.Owner.Authority), ("subject", Identity(continuation.Owner.SubjectId)),
                ("generation", checked((long)continuation.Owner.Generation.Value)), ("id", Identity(continuation.ContinuationId)), ("digest", continuation.Digest.CopyBytes()),
                ("epoch", checked((long)epoch)), ("fence", checked((long)fence))).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Receipt(domain, PostgresTransactionOutcome.Committed, epoch, fence, "continuation-discoverable");
        }
        catch (OperationCanceledException) { return Receipt(domain, PostgresTransactionOutcome.Indeterminate, epoch, fence, "continuation-outcome-unknown"); }
        catch (DbException) { return Receipt(domain, PostgresTransactionOutcome.Indeterminate, epoch, fence, "continuation-outcome-unknown"); }
    }

    /// <summary>Returns a stable PostgreSQL cut vector for individually locked owners; it never claims a global snapshot.</summary>
    /// <param name="owners">Distinct owner identities, bounded to 256.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>One observed generation per owner from a repeatable-read database snapshot.</returns>
    public async ValueTask<IReadOnlyDictionary<OwnerReference, OwnerGeneration>> ReadCutVectorAsync(IReadOnlyCollection<OwnerReference> owners, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owners);
        if (owners.Count is < 1 or > 256 || owners.Any(static x => !x.IsValid) || owners.Select(static x => (x.Authority, x.SubjectId)).Distinct().Count() != owners.Count)
            throw new ArgumentException("A cut vector requires one to 256 distinct valid owners.", nameof(owners));
        await using var connection = await _options.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<OwnerReference, OwnerGeneration>(owners.Count);
        foreach (var owner in owners.OrderBy(static x => x.Authority).ThenBy(static x => Identity(x.SubjectId), StringComparer.Ordinal))
        {
            await using var reader = await ExecuteReaderAsync(connection, transaction, string.Format(PostgresSql.LockOwner, _options.Schema), cancellationToken,
                ("scope", owner.SubjectId.Scope.ToString()), ("authority", (short)owner.Authority), ("subject", Identity(owner.SubjectId))).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(owner, OwnerGeneration.Create(checked((ulong)reader.GetInt64(0))));
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static void ValidateOwnerDomain(OwnerReference owner, AtomicDomain domain, AtomicDomainKind expected)
    {
        if (!owner.IsValid || !domain.IsValid || domain.Kind != expected || owner.SubjectId.Scope != domain.DomainId.Scope) throw new ArgumentException("Owner and distributed domain are incompatible.");
    }

    private static PostgresTransactionReceipt Receipt(AtomicDomain domain, PostgresTransactionOutcome outcome, ulong epoch, ulong fence, string code) =>
        new(domain, outcome, epoch, fence, code);

    private static string Identity(SemanticId identity) => Convert.ToHexString(identity.GetCanonicalBytes());

    private async ValueTask<int> ExecuteAsync(DbConnection connection, DbTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] values)
    {
        await using var command = CreateCommand(connection, transaction, sql, values);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<DbDataReader> ExecuteReaderAsync(DbConnection connection, DbTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] values)
    {
        var command = CreateCommand(connection, transaction, sql, values);
        try { return await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); }
        catch { await command.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private DbCommand CreateCommand(DbConnection connection, DbTransaction transaction, string sql, params (string Name, object Value)[] values)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        command.CommandTimeout = checked((int)Math.Ceiling(_options.CommandTimeout.TotalSeconds));
        foreach (var (name, value) in values) { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }
        return command;
    }
}
