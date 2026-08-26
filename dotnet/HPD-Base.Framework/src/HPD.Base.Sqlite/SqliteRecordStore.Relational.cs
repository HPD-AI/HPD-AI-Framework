using System.Globalization;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

/// <summary>Represents a sqlite record store.</summary>
public sealed partial class SqliteRecordStore
{
    /// <inheritdoc />
    public RelationalReadCapability RelationalReads { get; } = CreateRelationalCapability();

    internal static RelationalReadCapability CreateRelationalCapability() => new()
    {
        Supported = true,
        JoinKinds = [BaseJoinKind.Inner, BaseJoinKind.Left, BaseJoinKind.Semi, BaseJoinKind.Anti],
        AggregateKinds = Enum.GetValues<BaseAggregateKind>(),
        ComparisonOperators =
        [
            FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.LessThan,
            FilterOperator.LessThanOrEqual, FilterOperator.GreaterThan,
            FilterOperator.GreaterThanOrEqual,
        ],
        ValueKinds = Enum.GetValues<QueryValueKind>(),
        CanonicalJsonValues = true,
        IndependentAggregateBranches = true,
        SingleSnapshotCompoundReads = true,
        MaxCompoundBranches = 32,
        MaxCompoundOperations = 256,
        MaxSources = 32,
        MaxJoins = 8,
        MaxPredicateNodes = 256,
        MaxGroupKeys = 8,
        MaxAggregates = 32,
        MaxProjectionFields = 64,
        MaxSortFields = 8,
        MaxResultRows = 1_000,
        MaxResultBytes = 16_777_216,
        SnapshotConsistency = true,
        CompleteDependencyEvidence = true,
    };

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseRelationalReadExecutionResult>> ExecuteReadAsync(
        BaseRelationalReadExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var acquisition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        acquisition.CancelAfter(request.AcquisitionTimeout);
        IAsyncDisposable generationLease;
        try
        {
            generationLease = await _schemaGenerationGate.AcquireSharedAsync(acquisition.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RelationalFailure("base.relational.read.timeout", "SQLite relational snapshot acquisition timed out.");
        }

        await using (generationLease.ConfigureAwait(false))
        {
            try
            {
                if (Volatile.Read(ref _schemaGeneration) != request.Plan.SchemaGeneration)
                    return new OperationResult<BaseRelationalReadExecutionResult>
                    {
                        Status = OperationStatus.CapabilityUnavailable,
                        Error = new BaseError
                        {
                            Code = "base.relational.read.schemaNotReady",
                            Message = "The requested SQLite schema generation is not ready.",
                            Category = ErrorCategory.Capability,
                        },
                    };
                if (request.Plan.Topology == BaseRelationalReadTopology.CompoundCount)
                    return await ExecuteCompoundReadAsync(request, cancellationToken).ConfigureAwait(false);
                var compiler = new SqliteRelationalReadCompiler(_physical, _names, _options.ExportedSubjects, request);
                SqliteRelationalReadCompiler.CompiledRead compiled = compiler.Compile();
                using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                execution.CancelAfter(request.ExecutionTimeout);
                await using SqliteConnection connection = await OpenInitializedAsync(execution.Token).ConfigureAwait(false);
                await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(execution.Token).ConfigureAwait(false);

                long count;
                await using (SqliteCommand countCommand = connection.CreateCommand())
                {
                    countCommand.Transaction = transaction;
                    countCommand.CommandTimeout = RelationalTimeoutSeconds(request.ExecutionTimeout);
                    countCommand.CommandText = compiled.CountSql;
                    compiled.Bind(countCommand);
                    count = Convert.ToInt64(await countCommand.ExecuteScalarAsync(execution.Token).ConfigureAwait(false), CultureInfo.InvariantCulture);
                }

                var rows = new List<BaseRelationalRow>();
                long bytes = 0;
                BaseRegisteredReadWindow? window = request.Plan.Window;
                int limit = window?.Kind == BaseRegisteredReadWindowKind.Offset
                    ? window.Limit!.Value
                    : window?.PerPage ?? request.MaxResultRows;
                int offset = window?.Kind == BaseRegisteredReadWindowKind.Offset
                    ? window.Offset!.Value
                    : window is null ? 0 : checked((window.Page!.Value - 1) * window.PerPage!.Value);
                await using (SqliteCommand command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandTimeout = RelationalTimeoutSeconds(request.ExecutionTimeout);
                    command.CommandText = compiled.PageSql;
                    compiled.Bind(command);
                    command.Parameters.AddWithValue("$__limit", limit);
                    command.Parameters.AddWithValue("$__offset", offset);
                    await using SqliteDataReader reader = await command.ExecuteReaderAsync(execution.Token).ConfigureAwait(false);
                    while (await reader.ReadAsync(execution.Token).ConfigureAwait(false))
                    {
                        if (rows.Count >= request.MaxResultRows)
                            return RelationalFailure("base.relational.read.limitExceeded", "SQLite relational result limits were exceeded.");
                        BaseRelationalRow row = compiled.ReadRow(reader);
                        if (!TryAccumulateRelationalResultBytes(
                                bytes,
                                SqliteRelationalReadCompiler.EstimateBytes(row),
                                out bytes))
                            return RelationalFailure("base.relational.read.limitExceeded", "SQLite relational result limits were exceeded.");
                        if (bytes > request.MaxResultBytes)
                            return RelationalFailure("base.relational.read.limitExceeded", "SQLite relational result limits were exceeded.");
                        rows.Add(row);
                    }
                }
                BaseReadDependencyEvidence[] dependencies = await ReadRelationalDependenciesAsync(
                    connection, transaction, request, execution.Token).ConfigureAwait(false);
                await transaction.CommitAsync(execution.Token).ConfigureAwait(false);
                return OperationResults.Ok(new BaseRelationalReadExecutionResult
                {
                    Result = new BaseRelationalReadResult
                    {
                        Rows = rows.ToArray(),
                        Page = window?.Kind == BaseRegisteredReadWindowKind.Offset
                            ? new PageInfo { Offset = offset, Limit = limit, HasMore = count > offset && count - offset > rows.Count }
                            : new PageInfo { Page = window?.Page ?? 1, PerPage = limit, HasMore = count > offset && count - offset > rows.Count },
                        Count = count,
                        SchemaGeneration = request.Plan.SchemaGeneration,
                    },
                    DependencyEvidence = dependencies,
                });
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return RelationalFailure("base.relational.read.timeout", "SQLite relational execution timed out.");
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                return RelationalFailure("base.relational.read.resultInvalid", "SQLite relational execution failed.");
            }
        }
    }

    private async ValueTask<OperationResult<BaseRelationalReadExecutionResult>> ExecuteCompoundReadAsync(
        BaseRelationalReadExecutionRequest request, CancellationToken cancellationToken)
    {
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        execution.CancelAfter(request.ExecutionTimeout);
        await using SqliteConnection connection = await OpenInitializedAsync(execution.Token).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(execution.Token).ConfigureAwait(false);
        var rows = new List<BaseRelationalRow>(request.Plan.CompoundCountBranches.Length); long bytes = 0;
        foreach (BaseRelationalCompoundCountBranch branch in request.Plan.CompoundCountBranches)
        {
            const string aggregateId = "base.compound.count";
            var branchPlan = new BaseRelationalReadPlan
            {
                Id = request.Plan.Id + "." + branch.Id, Topology = BaseRelationalReadTopology.Ordinary,
                SchemaGeneration = request.Plan.SchemaGeneration, Sources = [branch.Source], Predicate = branch.Predicate,
                Aggregates = [new BaseRelationalReadAggregate
                {
                    Id = aggregateId, Kind = BaseAggregateKind.Count,
                    Operand = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.RecordId, SourceId = branch.Source.Id, FieldId = "base.recordId" },
                }],
                Projection = [new BaseRelationalReadProjection
                {
                    FieldId = branch.CountOutputFieldId,
                    Operand = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.Aggregate, AggregateId = aggregateId },
                }],
                Parameters = request.Plan.Parameters,
                Budgets = request.Plan.Budgets with { MaxCompoundBranches = 0, MaxCompoundOperations = 0 },
                Pagination = new BaseRegisteredReadPaginationAuthority
                {
                    Mode = BaseRegisteredReadPaginationMode.PageOnly,
                    MaximumOffset = 0,
                },
            };
            var branchRequest = request with
            {
                Plan = branchPlan,
                SourcePolicies = request.SourcePolicies.Where(policy => string.Equals(policy.SourceId, branch.Source.Id, StringComparison.Ordinal)).ToArray(),
                MaxResultRows = 1,
            };
            var compiler = new SqliteRelationalReadCompiler(_physical, _names, _options.ExportedSubjects, branchRequest);
            SqliteRelationalReadCompiler.CompiledRead compiled = compiler.Compile();
            long count;
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction; command.CommandTimeout = RelationalTimeoutSeconds(request.ExecutionTimeout);
                command.CommandText = compiled.PageSql; compiled.Bind(command);
                command.Parameters.AddWithValue("$__limit", 1); command.Parameters.AddWithValue("$__offset", 0);
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(execution.Token).ConfigureAwait(false);
                if (!await reader.ReadAsync(execution.Token).ConfigureAwait(false)) throw new InvalidOperationException();
                BaseRelationalRow projected = compiled.ReadRow(reader);
                count = projected.Fields.Single(field => string.Equals(field.FieldId, branch.CountOutputFieldId, StringComparison.Ordinal)).Value.Integer
                    ?? throw new InvalidOperationException();
                if (await reader.ReadAsync(execution.Token).ConfigureAwait(false)) throw new InvalidOperationException();
            }
            var row = new BaseRelationalRow
            {
                Fields =
                [
                    new() { FieldId = branch.DiscriminatorOutputFieldId, Value = new QueryValue { Kind = QueryValueKind.String, String = branch.Discriminator } },
                    new() { FieldId = branch.CountOutputFieldId, Value = new QueryValue { Kind = QueryValueKind.Integer, Integer = count } },
                ],
            };
            if (!TryAccumulateRelationalResultBytes(bytes, SqliteRelationalReadCompiler.EstimateBytes(row), out bytes)
                || bytes > request.MaxResultBytes) return RelationalFailure("base.relational.read.limitExceeded", "SQLite relational result limits were exceeded.");
            rows.Add(row);
        }
        BaseReadDependencyEvidence[] dependencies = await ReadRelationalDependenciesAsync(connection, transaction, request, execution.Token).ConfigureAwait(false);
        BaseRelationalCompoundBranchEvidence[] evidence = request.Plan.CompoundCountBranches.Select((branch, ordinal) => new BaseRelationalCompoundBranchEvidence
        {
            BranchId = branch.Id, BranchChecksum = branch.BranchChecksum, RowOrdinal = ordinal,
            SchemaGeneration = request.Plan.SchemaGeneration,
        }).ToArray();
        if (!BaseRelationalReadEvidenceAccounting.TryMeasure(dependencies, evidence, out long evidenceBytes)
            || !TryAccumulateRelationalResultBytes(bytes, evidenceBytes, out bytes) || bytes > request.MaxResultBytes)
            return RelationalFailure("base.relational.read.limitExceeded", "SQLite relational result limits were exceeded.");
        await transaction.CommitAsync(execution.Token).ConfigureAwait(false);
        return OperationResults.Ok(new BaseRelationalReadExecutionResult
        {
            Result = new BaseRelationalReadResult
            {
                Rows = rows.ToArray(), Page = new PageInfo { Page = 1, PerPage = rows.Count, Limit = rows.Count, HasMore = false }, Count = rows.Count,
                SchemaGeneration = request.Plan.SchemaGeneration,
            },
            DependencyEvidence = dependencies,
            CompoundBranches = evidence,
        });
    }

    internal static bool TryAccumulateRelationalResultBytes(long current, long additional, out long total)
    {
        try
        {
            total = checked(current + additional);
            return current >= 0 && additional >= 0;
        }
        catch (OverflowException)
        {
            total = 0;
            return false;
        }
    }

    private async ValueTask<BaseReadDependencyEvidence[]> ReadRelationalDependenciesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BaseRelationalReadExecutionRequest request,
        CancellationToken cancellationToken)
    {
        BaseRelationalOperand? subject = request.Plan.Projection.Select(static value => value.Operand)
            .SingleOrDefault(static value => value.Kind == BaseRelationalOperandKind.SubjectReference);
        long? stateGeneration = null;
        if (subject is not null)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = RelationalTimeoutSeconds(request.ExecutionTimeout);
            command.CommandText = $"SELECT state_generation FROM {_names.SubjectContracts} WHERE contract_id=$contract AND contract_version=$version";
            command.Parameters.AddWithValue("$contract", subject.SubjectContractId!);
            command.Parameters.AddWithValue("$version", subject.SubjectContractVersion!.Value);
            object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is null || value is DBNull) throw new InvalidOperationException();
            stateGeneration = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (stateGeneration <= 0) throw new InvalidOperationException();
        }
        return request.Plan.Sources.Select(source => subject is not null && string.Equals(subject.SourceId, source.Id, StringComparison.Ordinal)
            ? new BaseReadDependencyEvidence
            {
                CollectionId = source.CollectionId,
                SubjectContractId = subject.SubjectContractId,
                SubjectContractVersion = subject.SubjectContractVersion,
                SubjectStateGeneration = stateGeneration,
            }
            : new BaseReadDependencyEvidence { CollectionId = source.CollectionId })
            .DistinctBy(static evidence => evidence.CollectionId, StringComparer.Ordinal)
            .ToArray();
    }

    private static int RelationalTimeoutSeconds(TimeSpan timeout) =>
        Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));

    private static OperationResult<BaseRelationalReadExecutionResult> RelationalFailure(string code, string message) =>
        OperationResults.StoreError<BaseRelationalReadExecutionResult>(new BaseError
        {
            Code = code,
            Message = message,
            Category = ErrorCategory.Store,
        });
}
