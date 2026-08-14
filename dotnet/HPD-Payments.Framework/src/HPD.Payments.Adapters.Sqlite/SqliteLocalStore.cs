using System.Buffers.Binary;
using Microsoft.Data.Sqlite;

namespace HPD.Payments.Adapters.Sqlite;

/// <summary>Identifies the closed outcome of a local SQLite compare-bind operation.</summary>
public enum SqliteAppendOutcome
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>The payload was durably appended.</summary>
    Appended,
    /// <summary>The identical generation and digest were already present.</summary>
    Replay,
    /// <summary>The expected generation or digest conflicted.</summary>
    Conflict,
    /// <summary>The operation was cancelled or failed after its outcome became unknowable.</summary>
    Indeterminate,
}

/// <summary>Returns the bounded result of one local append.</summary>
/// <param name="Outcome">Closed append outcome.</param>
/// <param name="Generation">Current generation observed by the transaction.</param>
public readonly record struct SqliteAppendResult(SqliteAppendOutcome Outcome, ulong Generation);

/// <summary>Owns a bounded page of immutable SQLite journal entries.</summary>
public sealed class SqliteJournalPage
{
    private readonly byte[][] _payloads;

    /// <summary>Gets copies of the payloads owned by this page.</summary>
    public IReadOnlyList<byte[]> Payloads => _payloads;

    /// <summary>Gets an opaque store-bound continuation token, or empty memory at end.</summary>
    public ReadOnlyMemory<byte> Continuation { get; }

    internal SqliteJournalPage(byte[][] payloads, byte[] continuation)
    {
        _payloads = payloads;
        Continuation = continuation;
    }
}

/// <summary>Implements the real embedded <c>E-LOCAL</c> persistence mechanics in one SQLite database.</summary>
/// <remarks>
/// The store owns storage mechanics only. It does not interpret authority facts, certify distributed
/// domains, or supply quota/wallet policy. A guarded dual-endpoint operation is admitted only when
/// both endpoints and the relation share this exact database transaction.
/// </remarks>
public sealed class SqliteLocalStore : IDisposable, IAsyncDisposable
{
    private const int MaximumPayloadBytes = 1_048_576;
    private const int MaximumPageSize = 1024;
    private readonly string _connectionString;
    private bool _disposed;

    /// <summary>Creates or opens a local SQLite store and initializes its durable schema.</summary>
    /// <param name="databasePath">Explicit database path; <c>:memory:</c> is intentionally rejected because death/restore is required.</param>
    /// <exception cref="ArgumentException">The path is missing or requests a transient in-memory database.</exception>
    public SqliteLocalStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || databasePath == ":memory:")
            throw new ArgumentException("A durable SQLite database path is required.", nameof(databasePath));
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 30,
        };
        _connectionString = builder.ToString();
        Initialize();
    }

    /// <summary>Atomically compares one stream generation and appends an immutable payload.</summary>
    /// <param name="stream">Bounded caller-defined owner or continuation stream key.</param>
    /// <param name="expectedGeneration">Exact current generation expected; zero denotes an absent stream.</param>
    /// <param name="digest">Canonical semantic digest bytes.</param>
    /// <param name="payload">Owned immutable bytes to append.</param>
    /// <param name="cancellationToken">Cooperative cancellation; cancellation never proves non-commit.</param>
    /// <returns>The exact transaction outcome and observed generation.</returns>
    public async ValueTask<SqliteAppendResult> CompareBindAppendAsync(string stream, ulong expectedGeneration, ReadOnlyMemory<byte> digest, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        ValidateKey(stream, nameof(stream));
        ValidateBytes(digest, 128, nameof(digest));
        ValidateBytes(payload, MaximumPayloadBytes, nameof(payload));
        ThrowIfDisposed();
        try
        {
            using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            var current = await CurrentGenerationAsync(connection, transaction, stream, cancellationToken).ConfigureAwait(false);
            var next = checked(expectedGeneration + 1);
            if (current != expectedGeneration)
            {
                if (current == next && await IsReplayAsync(connection, transaction, stream, next, digest, cancellationToken).ConfigureAwait(false))
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return new(SqliteAppendOutcome.Replay, current);
                }
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new(SqliteAppendOutcome.Conflict, current);
            }
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO journal(stream,generation,digest,payload,recorded_utc) VALUES($s,$g,$d,$p,$t)";
            command.Parameters.AddWithValue("$s", stream);
            command.Parameters.AddWithValue("$g", checked((long)next));
            command.Parameters.Add("$d", SqliteType.Blob).Value = digest.ToArray();
            command.Parameters.Add("$p", SqliteType.Blob).Value = payload.ToArray();
            command.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(SqliteAppendOutcome.Appended, next);
        }
        catch (OperationCanceledException) { return new(SqliteAppendOutcome.Indeterminate, expectedGeneration); }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6) { return new(SqliteAppendOutcome.Indeterminate, expectedGeneration); }
    }

    /// <summary>Reads a bounded immutable history page using a store-bound opaque continuation.</summary>
    public async ValueTask<SqliteJournalPage> ReadAsync(string stream, ulong throughGeneration, int maximumItems, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default)
    {
        ValidateKey(stream, nameof(stream));
        if (throughGeneration == 0 || maximumItems is < 1 or > MaximumPageSize || continuation.Length is not (0 or 8))
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        ThrowIfDisposed();
        var after = continuation.IsEmpty ? 0UL : BinaryPrimitives.ReadUInt64BigEndian(continuation.Span);
        using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT generation,payload FROM journal WHERE stream=$s AND generation>$a AND generation<=$t ORDER BY generation LIMIT $n";
        command.Parameters.AddWithValue("$s", stream);
        command.Parameters.AddWithValue("$a", checked((long)after));
        command.Parameters.AddWithValue("$t", checked((long)throughGeneration));
        command.Parameters.AddWithValue("$n", maximumItems + 1);
        var rows = new List<(ulong Generation, byte[] Payload)>(maximumItems + 1);
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add((checked((ulong)reader.GetInt64(0)), (byte[])reader[1]));
        var hasMore = rows.Count > maximumItems;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var token = hasMore ? new byte[8] : [];
        if (hasMore) BinaryPrimitives.WriteUInt64BigEndian(token, rows[^1].Generation);
        return new(rows.Select(static x => x.Payload).ToArray(), token);
    }

    /// <summary>Atomically records a relation only when both endpoint generations still match in this database.</summary>
    public async ValueTask<bool> GuardedRelateAsync(string relationId, string sourceStream, ulong sourceGeneration, string targetStream, ulong targetGeneration, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        ValidateKey(relationId, nameof(relationId)); ValidateKey(sourceStream, nameof(sourceStream)); ValidateKey(targetStream, nameof(targetStream));
        ValidateBytes(payload, MaximumPayloadBytes, nameof(payload)); ThrowIfDisposed();
        using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        if (await CurrentGenerationAsync(connection, transaction, sourceStream, cancellationToken).ConfigureAwait(false) != sourceGeneration ||
            await CurrentGenerationAsync(connection, transaction, targetStream, cancellationToken).ConfigureAwait(false) != targetGeneration)
        { await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); return false; }
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO relations(relation_id,source_stream,source_generation,target_stream,target_generation,payload) VALUES($r,$s,$sg,$t,$tg,$p)";
        command.Parameters.AddWithValue("$r", relationId); command.Parameters.AddWithValue("$s", sourceStream); command.Parameters.AddWithValue("$sg", checked((long)sourceGeneration));
        command.Parameters.AddWithValue("$t", targetStream); command.Parameters.AddWithValue("$tg", checked((long)targetGeneration)); command.Parameters.Add("$p", SqliteType.Blob).Value = payload.ToArray();
        var changed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false); return changed == 1;
    }

    /// <summary>Durably records a discoverable continuation, recovery item, custody observation, correction, or residue.</summary>
    public async ValueTask<bool> PutDiscoverableAsync(string kind, string itemId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        ValidateKey(kind, nameof(kind)); ValidateKey(itemId, nameof(itemId)); ValidateBytes(payload, MaximumPayloadBytes, nameof(payload)); ThrowIfDisposed();
        using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false); using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO discoverable(kind,item_id,payload,state,attempts) VALUES($k,$i,$p,'ready',0)";
        command.Parameters.AddWithValue("$k", kind); command.Parameters.AddWithValue("$i", itemId); command.Parameters.Add("$p", SqliteType.Blob).Value = payload.ToArray();
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <summary>Claims a bounded recovery batch and advances attempts atomically; stale generations remain addressable.</summary>
    public async ValueTask<IReadOnlyList<byte[]>> SweepAsync(string kind, int maximumItems, CancellationToken cancellationToken = default)
    {
        ValidateKey(kind, nameof(kind)); if (maximumItems is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(maximumItems)); ThrowIfDisposed();
        using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false); using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE discoverable SET attempts=attempts+1 WHERE rowid IN (SELECT rowid FROM discoverable WHERE kind=$k AND state='ready' ORDER BY rowid LIMIT $n) RETURNING payload";
        command.Parameters.AddWithValue("$k", kind); command.Parameters.AddWithValue("$n", maximumItems);
        var result = new List<byte[]>(maximumItems); using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add((byte[])reader[0]);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false); return result;
    }

    /// <summary>Releases pooled SQLite resources owned by this instance; durable state remains restorable.</summary>
    public void Dispose() { if (_disposed) return; _disposed = true; using var connection = new SqliteConnection(_connectionString); SqliteConnection.ClearPool(connection); }

    /// <summary>Asynchronously releases this store; no durable records are deleted.</summary>
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;" +
            "CREATE TABLE IF NOT EXISTS journal(stream TEXT NOT NULL,generation INTEGER NOT NULL CHECK(generation>0),digest BLOB NOT NULL,payload BLOB NOT NULL,recorded_utc TEXT NOT NULL,PRIMARY KEY(stream,generation));" +
            "CREATE UNIQUE INDEX IF NOT EXISTS ux_journal_digest ON journal(stream,generation,digest);" +
            "CREATE TABLE IF NOT EXISTS relations(relation_id TEXT PRIMARY KEY,source_stream TEXT NOT NULL,source_generation INTEGER NOT NULL,target_stream TEXT NOT NULL,target_generation INTEGER NOT NULL,payload BLOB NOT NULL);" +
            "CREATE TABLE IF NOT EXISTS discoverable(kind TEXT NOT NULL,item_id TEXT NOT NULL,payload BLOB NOT NULL,state TEXT NOT NULL,attempts INTEGER NOT NULL,PRIMARY KEY(kind,item_id));";
        command.ExecuteNonQuery();
    }

    private async ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    { var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken).ConfigureAwait(false); return connection; }

    private static async ValueTask<ulong> CurrentGenerationAsync(SqliteConnection connection, SqliteTransaction transaction, string stream, CancellationToken cancellationToken)
    { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT COALESCE(MAX(generation),0) FROM journal WHERE stream=$s"; command.Parameters.AddWithValue("$s", stream); return checked((ulong)(long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L)); }

    private static async ValueTask<bool> IsReplayAsync(SqliteConnection connection, SqliteTransaction transaction, string stream, ulong generation, ReadOnlyMemory<byte> digest, CancellationToken cancellationToken)
    { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT digest FROM journal WHERE stream=$s AND generation=$g"; command.Parameters.AddWithValue("$s", stream); command.Parameters.AddWithValue("$g", checked((long)generation)); var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false); return value is byte[] bytes && bytes.AsSpan().SequenceEqual(digest.Span); }

    private static void ValidateKey(string value, string parameter) { if (string.IsNullOrWhiteSpace(value) || value.Length > 1024) throw new ArgumentException("A non-empty bounded key is required.", parameter); }
    private static void ValidateBytes(ReadOnlyMemory<byte> value, int maximum, string parameter) { if (value.IsEmpty || value.Length > maximum) throw new ArgumentException("Non-empty owned bytes within the bound are required.", parameter); }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
