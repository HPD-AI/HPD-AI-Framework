// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Evaluations.Batch;

namespace HPD.Agent.Evaluations.Storage;

/// <summary>
/// Registry for immutable evaluation dataset versions and SCD-2 case history.
/// </summary>
public interface IDatasetStore
{
    ValueTask<DatasetVersionRecord> RegisterDatasetVersionAsync<TInput>(
        Dataset<TInput> dataset,
        DatasetRegistrationOptions<TInput>? options = null,
        CancellationToken ct = default);

    ValueTask<DatasetRecord?> GetDatasetAsync(
        string datasetId,
        CancellationToken ct = default);

    IAsyncEnumerable<DatasetRecord> ListDatasetsAsync(
        CancellationToken ct = default);

    ValueTask<Dataset<TInput>?> GetDatasetVersionAsync<TInput>(
        string datasetId,
        string version,
        CancellationToken ct = default);

    IAsyncEnumerable<DatasetVersionRecord> GetDatasetVersionsAsync(
        string datasetId,
        CancellationToken ct = default);

    IAsyncEnumerable<EvalCase<TInput>> GetActiveCasesAsync<TInput>(
        string datasetId,
        DateTimeOffset at,
        CancellationToken ct = default);

    IAsyncEnumerable<EvalCase<TInput>> GetCaseHistoryAsync<TInput>(
        string datasetId,
        string caseId,
        CancellationToken ct = default);

    ValueTask<DatasetVersionDiff<TInput>> CompareVersionsAsync<TInput>(
        string datasetId,
        string fromVersion,
        string toVersion,
        CancellationToken ct = default);
}

/// <summary>Options used when registering a new immutable dataset version.</summary>
public sealed class DatasetRegistrationOptions<TInput>
{
    public string? Description { get; init; }

    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    public bool GenerateMissingCaseIds { get; init; } = true;

    public DateTimeOffset? RegisteredAt { get; init; }

    /// <summary>
    /// Optional AOT-safe case fingerprint. Use this for complex input objects where
    /// <see cref="object.ToString"/> is not a stable semantic representation.
    /// </summary>
    public Func<EvalCase<TInput>, string>? FingerprintCase { get; init; }
}

public sealed record DatasetRecord(
    string DatasetId,
    string CurrentVersion,
    string? Description,
    string ContentHash,
    DateTimeOffset RegisteredAt,
    IReadOnlyDictionary<string, object>? Metadata);

public sealed record DatasetVersionRecord(
    string DatasetId,
    string Version,
    string? Description,
    string ContentHash,
    int CaseCount,
    DateTimeOffset RegisteredAt,
    IReadOnlyDictionary<string, object>? Metadata);

public sealed record DatasetVersionDiff<TInput>(
    string DatasetId,
    string FromVersion,
    string ToVersion,
    IReadOnlyList<EvalCase<TInput>> Added,
    IReadOnlyList<EvalCase<TInput>> Removed,
    IReadOnlyList<DatasetCaseChange<TInput>> Changed);

public sealed record DatasetDiffReport<TInput>(
    string DatasetId,
    string FromVersion,
    string ToVersion,
    int AddedCount,
    int RemovedCount,
    int ChangedCount,
    IReadOnlyList<EvalCase<TInput>> Added,
    IReadOnlyList<EvalCase<TInput>> Removed,
    IReadOnlyList<DatasetCaseChange<TInput>> Changed);

public sealed record DatasetCaseChange<TInput>(
    string CaseId,
    EvalCase<TInput> Before,
    EvalCase<TInput> After);
