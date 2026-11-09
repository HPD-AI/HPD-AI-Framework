// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Evaluations.Batch;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Agent.Evaluations.Storage;

/// <summary>
/// Restart-safe dataset serialization authority for one input and evaluator model.
/// </summary>
/// <remarks>
/// Durable stores require this codec because <c>TInput</c> and evaluator instances
/// cannot otherwise be reconstructed from the untyped <see cref="IDatasetStore"/> API.
/// </remarks>
public sealed class DatasetCodec<TInput>
{
    /// <summary>Creates a durable dataset codec.</summary>
    public DatasetCodec(
        JsonTypeInfo<TInput> inputTypeInfo,
        Func<EvalCase<TInput>, string>? fingerprintCase = null,
        Func<IEvaluator, JsonNode?>? serializeEvaluator = null,
        Func<JsonNode?, IEvaluator>? deserializeEvaluator = null,
        Func<IReportEvaluator, JsonNode?>? serializeReportEvaluator = null,
        Func<JsonNode?, IReportEvaluator>? deserializeReportEvaluator = null)
    {
        ArgumentNullException.ThrowIfNull(inputTypeInfo);
        if ((serializeEvaluator is null) != (deserializeEvaluator is null))
        {
            throw new ArgumentException(
                "Dataset evaluator serialization and deserialization must be supplied together.");
        }
        if ((serializeReportEvaluator is null) != (deserializeReportEvaluator is null))
        {
            throw new ArgumentException(
                "Report-evaluator serialization and deserialization must be supplied together.");
        }

        InputTypeInfo = inputTypeInfo;
        FingerprintCase = fingerprintCase;
        SerializeEvaluator = serializeEvaluator;
        DeserializeEvaluator = deserializeEvaluator;
        SerializeReportEvaluator = serializeReportEvaluator;
        DeserializeReportEvaluator = deserializeReportEvaluator;
    }

    /// <summary>Gets the source-generated input JSON metadata.</summary>
    public JsonTypeInfo<TInput> InputTypeInfo { get; }

    /// <summary>Gets an optional stable case fingerprint.</summary>
    public Func<EvalCase<TInput>, string>? FingerprintCase { get; }

    /// <summary>Gets the evaluator serializer, when evaluator instances are persisted.</summary>
    public Func<IEvaluator, JsonNode?>? SerializeEvaluator { get; }

    /// <summary>Gets the evaluator parser, when evaluator instances are persisted.</summary>
    public Func<JsonNode?, IEvaluator>? DeserializeEvaluator { get; }

    /// <summary>Gets the report-evaluator serializer, when report evaluators are persisted.</summary>
    public Func<IReportEvaluator, JsonNode?>? SerializeReportEvaluator { get; }

    /// <summary>Gets the report-evaluator parser, when report evaluators are persisted.</summary>
    public Func<JsonNode?, IReportEvaluator>? DeserializeReportEvaluator { get; }
}

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
