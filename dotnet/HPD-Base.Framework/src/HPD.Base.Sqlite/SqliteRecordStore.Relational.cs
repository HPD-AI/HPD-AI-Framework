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
                var compiler = new SqliteRelationalReadCompiler(_physical, request);
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
                    DependencyEvidence = request.Plan.Sources
                        .Select(static source => new BaseReadDependencyEvidence { CollectionId = source.CollectionId })
                        .DistinctBy(static evidence => evidence.CollectionId, StringComparer.Ordinal)
                        .ToArray(),
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
