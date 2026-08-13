using System.Globalization;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

/// <summary>Represents a sqlite record store.</summary>
public sealed partial class SqliteRecordStore
{
    /// <inheritdoc />
    public RelationalReadCapability RelationalReads { get; } = new()
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
        MaxSources = 8,
        MaxJoins = 8,
        MaxPredicateNodes = 256,
        MaxGroupKeys = 8,
        MaxAggregates = 16,
        MaxProjectionFields = 64,
        MaxSortFields = 8,
        MaxResultRows = 1_000,
        MaxResultBytes = 1_048_576,
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
                var page = request.Plan.Page;
                int limit = page?.PerPage ?? request.MaxResultRows;
                int offset = page is null ? 0 : checked((page.Value.Page - 1) * page.Value.PerPage);
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
                        rows.Add(compiled.ReadRow(reader));
                    }
                }

                int bytes = rows.Sum(SqliteRelationalReadCompiler.EstimateBytes);
                if (bytes > request.MaxResultBytes)
                    return RelationalFailure("base.relational.read.limitExceeded", "SQLite relational result limits were exceeded.");
                BaseReadDependencyEvidence[] dependencies = await ReadRelationalDependenciesAsync(
                    connection, transaction, request, execution.Token).ConfigureAwait(false);
                await transaction.CommitAsync(execution.Token).ConfigureAwait(false);
                return OperationResults.Ok(new BaseRelationalReadExecutionResult
                {
                    Result = new BaseRelationalReadResult
                    {
                        Rows = rows.ToArray(),
                        Page = new PageInfo { Limit = limit, HasMore = offset + rows.Count < count },
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
