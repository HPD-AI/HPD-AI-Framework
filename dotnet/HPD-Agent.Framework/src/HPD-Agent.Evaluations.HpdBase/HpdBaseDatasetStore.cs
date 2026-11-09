// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HPD.Agent.Evaluations.Batch;
using HPD.Agent.Evaluations.Storage;
using HPD.Base;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace HPD.Agent.Evaluations.HpdBase;

/// <summary>Restart-durable HPD Base implementation of immutable evaluation dataset storage.</summary>
public sealed class HpdBaseDatasetStore : IDatasetStore
{
    private const string PayloadSchema = "hpd.agent.evaluation-dataset.v1";
    private const int PageSize = 250;
    private static readonly PrincipalContext StorePrincipal = new()
    {
        AuthenticationState = PrincipalAuthenticationState.System,
        SubjectKind = AccessSubjectKind.System,
        SubjectId = "hpd-agent-stores",
    };

    private readonly BaseSession _session;
    private readonly HpdBaseDatasetStoreOptions _options;

    /// <summary>Creates an HPD Base-backed immutable dataset store.</summary>
    public HpdBaseDatasetStore(
        IBaseSessionFactory sessions,
        HpdBaseDatasetStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _session = sessions.For(StorePrincipal);
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async ValueTask<DatasetVersionRecord> RegisterDatasetVersionAsync<TInput>(
        Dataset<TInput> dataset,
        DatasetRegistrationOptions<TInput>? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (string.IsNullOrWhiteSpace(dataset.DatasetId))
            throw new ArgumentException("DatasetId is required.", nameof(dataset));
        if (string.IsNullOrWhiteSpace(dataset.Version))
            throw new ArgumentException("Version is required.", nameof(dataset));

        DatasetCodec<TInput> codec = _options.RequireCodec<TInput>();
        options ??= new DatasetRegistrationOptions<TInput>();
        DateTimeOffset registeredAt = options.RegisteredAt ?? DateTimeOffset.UtcNow;
        Dataset<TInput> normalized =
            InMemoryDatasetStore.NormalizeDataset(dataset, options, registeredAt);
        Func<EvalCase<TInput>, string>? fingerprint =
            options.FingerprintCase ?? codec.FingerprintCase;
        string contentHash =
            InMemoryDatasetStore.ComputeDatasetHash(normalized, fingerprint);
        string inputType = InputTypeName<TInput>();

        HpdBaseDatasetVersion? existing = await LoadVersionAsync(
            normalized.DatasetId!,
            normalized.Version!,
            ct).ConfigureAwait(false);
        if (existing is not null)
            return ExistingVersion(existing, contentHash, normalized.DatasetId!, normalized.Version!);

        HpdBaseDatasetVersion version = new()
        {
            DatasetId = normalized.DatasetId!,
            Version = normalized.Version!,
            ContentHash = contentHash,
            InputType = inputType,
            CaseCount = normalized.Cases.Count,
            RegisteredAt = registeredAt,
            Description = options.Description ?? string.Empty,
            MetadataPayload = SerializeMetadata(options.Metadata),
            Payload = SerializeDataset(normalized, codec, inputType),
        };
        HpdBaseDatasetHead head = new()
        {
            DatasetId = version.DatasetId,
            CurrentVersion = version.Version,
            ContentHash = version.ContentHash,
            RegisteredAt = version.RegisteredAt,
            Description = version.Description,
            MetadataPayload = version.MetadataPayload,
        };

        BaseBatchBuilder batch = _session.Atomic();
        batch.Create(
            HpdBaseDatasetVersion.Collection,
            VersionRecordId(version.DatasetId, version.Version),
            version);
        batch.Upsert(
            HpdBaseDatasetHead.Collection,
            HeadRecordId(version.DatasetId),
            head,
            head);
        try
        {
            (await batch.CommitAsync(ct).ConfigureAwait(false))
                .RequireValue()
                .RequireCommitted();
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            HpdBaseDatasetVersion? raced = await LoadVersionAsync(
                version.DatasetId,
                version.Version,
                ct).ConfigureAwait(false);
            if (raced is null)
                throw;
            return ExistingVersion(
                raced,
                contentHash,
                version.DatasetId,
                version.Version);
        }

        return ToVersionRecord(version);
    }

    /// <inheritdoc />
    public async ValueTask<DatasetRecord?> GetDatasetAsync(
        string datasetId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        BaseRecord<HpdBaseDatasetHead>? record = await GetRecordAsync(
            HpdBaseDatasetHead.Collection,
            HeadRecordId(datasetId),
            ct).ConfigureAwait(false);
        return record is null ? null : ToDatasetRecord(record.Value);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<DatasetRecord> ListDatasetsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (HpdBaseDatasetHead head in QueryRecordsAsync(
                           HpdBaseDatasetHead.Collection,
                           query => query
                               .OrderBy(HpdBaseDatasetHead.Fields.DatasetId)
                               .Take(PageSize),
                           ct).ConfigureAwait(false))
        {
            yield return ToDatasetRecord(head);
        }
    }

    /// <inheritdoc />
    public async ValueTask<Dataset<TInput>?> GetDatasetVersionAsync<TInput>(
        string datasetId,
        string version,
        CancellationToken ct = default)
    {
        DatasetCodec<TInput> codec = _options.RequireCodec<TInput>();
        HpdBaseDatasetVersion? record = await LoadVersionAsync(
            datasetId,
            version,
            ct).ConfigureAwait(false);
        return record is null
            ? null
            : DeserializeDataset(record, codec);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<DatasetVersionRecord> GetDatasetVersionsAsync(
        string datasetId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        await foreach (HpdBaseDatasetVersion version in QueryRecordsAsync(
                           HpdBaseDatasetVersion.Collection,
                           query => query
                               .Where(
                                   HpdBaseDatasetVersion.Fields.DatasetId,
                                   datasetId)
                               .OrderBy(
                                   HpdBaseDatasetVersion.Fields.RegisteredAt)
                               .Take(PageSize),
                           ct).ConfigureAwait(false))
        {
            yield return ToVersionRecord(version);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<EvalCase<TInput>> GetActiveCasesAsync<TInput>(
        string datasetId,
        DateTimeOffset at,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        DatasetCodec<TInput> codec = _options.RequireCodec<TInput>();
        InMemoryDatasetStore snapshot =
            await LoadSnapshotAsync(datasetId, codec, ct).ConfigureAwait(false);
        await foreach (EvalCase<TInput> evalCase in snapshot
                           .GetActiveCasesAsync<TInput>(datasetId, at, ct)
                           .ConfigureAwait(false))
        {
            yield return evalCase;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<EvalCase<TInput>> GetCaseHistoryAsync<TInput>(
        string datasetId,
        string caseId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        DatasetCodec<TInput> codec = _options.RequireCodec<TInput>();
        InMemoryDatasetStore snapshot =
            await LoadSnapshotAsync(datasetId, codec, ct).ConfigureAwait(false);
        await foreach (EvalCase<TInput> evalCase in snapshot
                           .GetCaseHistoryAsync<TInput>(datasetId, caseId, ct)
                           .ConfigureAwait(false))
        {
            yield return evalCase;
        }
    }

    /// <inheritdoc />
    public async ValueTask<DatasetVersionDiff<TInput>> CompareVersionsAsync<TInput>(
        string datasetId,
        string fromVersion,
        string toVersion,
        CancellationToken ct = default)
    {
        DatasetCodec<TInput> codec = _options.RequireCodec<TInput>();
        HpdBaseDatasetVersion fromRecord = await LoadVersionAsync(
                datasetId, fromVersion, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"Dataset '{datasetId}' version '{fromVersion}' was not found.");
        HpdBaseDatasetVersion toRecord = await LoadVersionAsync(
                datasetId, toVersion, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"Dataset '{datasetId}' version '{toVersion}' was not found.");
        Dataset<TInput> from = DeserializeDataset(fromRecord, codec);
        Dataset<TInput> to = DeserializeDataset(toRecord, codec);

        var snapshot = new InMemoryDatasetStore();
        await RegisterSnapshotVersionAsync(snapshot, fromRecord, from, codec, ct)
            .ConfigureAwait(false);
        await RegisterSnapshotVersionAsync(snapshot, toRecord, to, codec, ct)
            .ConfigureAwait(false);
        return await snapshot.CompareVersionsAsync<TInput>(
            datasetId, fromVersion, toVersion, ct).ConfigureAwait(false);
    }

    private async ValueTask<InMemoryDatasetStore> LoadSnapshotAsync<TInput>(
        string datasetId,
        DatasetCodec<TInput> codec,
        CancellationToken ct)
    {
        var snapshot = new InMemoryDatasetStore();
        await foreach (HpdBaseDatasetVersion record in QueryRecordsAsync(
                           HpdBaseDatasetVersion.Collection,
                           query => query
                               .Where(
                                   HpdBaseDatasetVersion.Fields.DatasetId,
                                   datasetId)
                               .OrderBy(
                                   HpdBaseDatasetVersion.Fields.RegisteredAt)
                               .Take(PageSize),
                           ct).ConfigureAwait(false))
        {
            Dataset<TInput> dataset = DeserializeDataset(record, codec);
            await RegisterSnapshotVersionAsync(
                snapshot, record, dataset, codec, ct).ConfigureAwait(false);
        }
        return snapshot;
    }

    private static async ValueTask RegisterSnapshotVersionAsync<TInput>(
        InMemoryDatasetStore snapshot,
        HpdBaseDatasetVersion record,
        Dataset<TInput> dataset,
        DatasetCodec<TInput> codec,
        CancellationToken ct)
    {
        await snapshot.RegisterDatasetVersionAsync(
            dataset,
            new DatasetRegistrationOptions<TInput>
            {
                Description = EmptyToNull(record.Description),
                Metadata = DeserializeMetadata(record.MetadataPayload),
                RegisteredAt = record.RegisteredAt,
                FingerprintCase = codec.FingerprintCase,
            },
            ct).ConfigureAwait(false);
    }

    private async ValueTask<HpdBaseDatasetVersion?> LoadVersionAsync(
        string datasetId,
        string version,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        BaseRecord<HpdBaseDatasetVersion>? record = await GetRecordAsync(
            HpdBaseDatasetVersion.Collection,
            VersionRecordId(datasetId, version),
            ct).ConfigureAwait(false);
        if (record is null)
            return null;
        if (!StringComparer.Ordinal.Equals(record.Value.DatasetId, datasetId) ||
            !StringComparer.Ordinal.Equals(record.Value.Version, version))
        {
            throw new InvalidDataException(
                $"Stored dataset version '{datasetId}/{version}' has conflicting identity.");
        }
        return record.Value;
    }

    private static DatasetVersionRecord ExistingVersion(
        HpdBaseDatasetVersion existing,
        string contentHash,
        string datasetId,
        string version)
    {
        if (!StringComparer.Ordinal.Equals(existing.ContentHash, contentHash))
        {
            throw new InvalidOperationException(
                $"Dataset '{datasetId}' version '{version}' is already registered with different content.");
        }
        return ToVersionRecord(existing);
    }

    private static DatasetVersionRecord ToVersionRecord(
        HpdBaseDatasetVersion value) =>
        new(
            value.DatasetId,
            value.Version,
            EmptyToNull(value.Description),
            value.ContentHash,
            value.CaseCount,
            value.RegisteredAt,
            DeserializeMetadata(value.MetadataPayload));

    private static DatasetRecord ToDatasetRecord(HpdBaseDatasetHead value) =>
        new(
            value.DatasetId,
            value.CurrentVersion,
            EmptyToNull(value.Description),
            value.ContentHash,
            value.RegisteredAt,
            DeserializeMetadata(value.MetadataPayload));

    private static string SerializeDataset<TInput>(
        Dataset<TInput> dataset,
        DatasetCodec<TInput> codec,
        string inputType)
    {
        var evaluators = SerializeEvaluators(
            dataset.Evaluators, codec.SerializeEvaluator);
        var reportEvaluators = SerializeReportEvaluators(
            dataset.ReportEvaluators, codec.SerializeReportEvaluator);
        var cases = new JsonArray();
        foreach (EvalCase<TInput> evalCase in dataset.Cases)
        {
            cases.Add((JsonNode)new JsonObject
            {
                ["caseId"] = evalCase.CaseId,
                ["name"] = evalCase.Name,
                ["version"] = evalCase.Version,
                ["validFrom"] = evalCase.ValidFrom?.ToString(
                    "O", CultureInfo.InvariantCulture),
                ["validTo"] = evalCase.ValidTo?.ToString(
                    "O", CultureInfo.InvariantCulture),
                ["input"] = JsonSerializer.SerializeToNode(
                    evalCase.Input,
                    codec.InputTypeInfo),
                ["groundTruth"] = evalCase.GroundTruth,
                ["metadata"] = SerializeMetadataObject(evalCase.Metadata),
                ["evaluators"] = SerializeEvaluators(
                    evalCase.Evaluators, codec.SerializeEvaluator),
                ["reportEvaluators"] = SerializeReportEvaluators(
                    evalCase.ReportEvaluators,
                    codec.SerializeReportEvaluator),
            });
        }

        return new JsonObject
        {
            ["schema"] = PayloadSchema,
            ["inputType"] = inputType,
            ["datasetId"] = dataset.DatasetId,
            ["version"] = dataset.Version,
            ["evaluators"] = evaluators,
            ["reportEvaluators"] = reportEvaluators,
            ["cases"] = cases,
        }.ToJsonString();
    }

    private static Dataset<TInput> DeserializeDataset<TInput>(
        HpdBaseDatasetVersion record,
        DatasetCodec<TInput> codec)
    {
        string expectedInputType = InputTypeName<TInput>();
        if (!StringComparer.Ordinal.Equals(record.InputType, expectedInputType))
        {
            throw new InvalidDataException(
                $"Dataset '{record.DatasetId}/{record.Version}' stores input type " +
                $"'{record.InputType}', not '{expectedInputType}'.");
        }

        JsonNode root;
        try
        {
            root = JsonNode.Parse(record.Payload)
                ?? throw new JsonException("Dataset payload was JSON null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Dataset '{record.DatasetId}/{record.Version}' contains invalid JSON.",
                exception);
        }

        if (root is not JsonObject payload ||
            RequiredString(payload, "schema") != PayloadSchema ||
            RequiredString(payload, "inputType") != expectedInputType ||
            RequiredString(payload, "datasetId") != record.DatasetId ||
            RequiredString(payload, "version") != record.Version)
        {
            throw new InvalidDataException(
                $"Dataset '{record.DatasetId}/{record.Version}' has an incompatible payload contract.");
        }
        if (payload["cases"] is not JsonArray cases)
            throw new InvalidDataException("Dataset payload requires a cases array.");

        var convertedCases = new List<EvalCase<TInput>>(cases.Count);
        foreach (JsonNode? item in cases)
        {
            if (item is not JsonObject evalCase)
                throw new InvalidDataException("Dataset cases must be JSON objects.");
            convertedCases.Add(new EvalCase<TInput>
            {
                CaseId = OptionalString(evalCase, "caseId"),
                Name = OptionalString(evalCase, "name"),
                Version = OptionalString(evalCase, "version"),
                ValidFrom = OptionalDateTimeOffset(evalCase, "validFrom"),
                ValidTo = OptionalDateTimeOffset(evalCase, "validTo"),
                Input = evalCase["input"] is { } input
                    ? input.Deserialize(codec.InputTypeInfo)
                        ?? throw new InvalidDataException(
                            $"Dataset case '{OptionalString(evalCase, "caseId")}' input was JSON null.")
                    : throw new InvalidDataException(
                        $"Dataset case '{OptionalString(evalCase, "caseId")}' has no input."),
                GroundTruth = OptionalString(evalCase, "groundTruth"),
                Metadata = DeserializeMetadataObject(evalCase["metadata"]),
                Evaluators = DeserializeEvaluators(
                    evalCase["evaluators"], codec.DeserializeEvaluator),
                ReportEvaluators = DeserializeReportEvaluators(
                    evalCase["reportEvaluators"],
                    codec.DeserializeReportEvaluator),
            });
        }

        return new Dataset<TInput>
        {
            DatasetId = record.DatasetId,
            Version = record.Version,
            Cases = convertedCases,
            Evaluators = DeserializeEvaluators(
                payload["evaluators"], codec.DeserializeEvaluator),
            ReportEvaluators = DeserializeReportEvaluators(
                payload["reportEvaluators"],
                codec.DeserializeReportEvaluator),
        };
    }

    private static JsonArray SerializeEvaluators(
        IReadOnlyList<IEvaluator>? evaluators,
        Func<IEvaluator, JsonNode?>? serialize)
    {
        var result = new JsonArray();
        if (evaluators is null || evaluators.Count == 0)
            return result;
        if (serialize is null)
        {
            throw new InvalidOperationException(
                "The dataset contains evaluator instances, but its DatasetCodec does not provide evaluator serialization.");
        }
        foreach (IEvaluator evaluator in evaluators)
            result.Add(serialize(evaluator)?.DeepClone());
        return result;
    }

    private static JsonArray SerializeReportEvaluators(
        IReadOnlyList<IReportEvaluator>? evaluators,
        Func<IReportEvaluator, JsonNode?>? serialize)
    {
        var result = new JsonArray();
        if (evaluators is null || evaluators.Count == 0)
            return result;
        if (serialize is null)
        {
            throw new InvalidOperationException(
                "The dataset contains report-evaluator instances, but its DatasetCodec does not provide report-evaluator serialization.");
        }
        foreach (IReportEvaluator evaluator in evaluators)
            result.Add(serialize(evaluator)?.DeepClone());
        return result;
    }

    private static IReadOnlyList<IEvaluator> DeserializeEvaluators(
        JsonNode? node,
        Func<JsonNode?, IEvaluator>? deserialize)
    {
        if (node is not JsonArray array || array.Count == 0)
            return [];
        if (deserialize is null)
        {
            throw new InvalidDataException(
                "The stored dataset contains evaluator instances, but its DatasetCodec does not provide evaluator deserialization.");
        }
        return array.Select(value => deserialize(value?.DeepClone())).ToArray();
    }

    private static IReadOnlyList<IReportEvaluator> DeserializeReportEvaluators(
        JsonNode? node,
        Func<JsonNode?, IReportEvaluator>? deserialize)
    {
        if (node is not JsonArray array || array.Count == 0)
            return [];
        if (deserialize is null)
        {
            throw new InvalidDataException(
                "The stored dataset contains report-evaluator instances, but its DatasetCodec does not provide report-evaluator deserialization.");
        }
        return array.Select(value => deserialize(value?.DeepClone())).ToArray();
    }

    private static string SerializeMetadata(
        IReadOnlyDictionary<string, object>? metadata) =>
        SerializeMetadataObject(metadata)?.ToJsonString() ?? "null";

    private static JsonObject? SerializeMetadataObject(
        IEnumerable<KeyValuePair<string, object>>? metadata)
    {
        if (metadata is null)
            return null;
        var result = new JsonObject();
        foreach ((string key, object value) in metadata)
            result[key] = SerializeMetadataValue(value);
        return result;
    }

    private static JsonNode? SerializeMetadataValue(object? value) => value switch
    {
        null => null,
        JsonElement element => JsonNode.Parse(element.GetRawText()),
        JsonNode node => node.DeepClone(),
        string text => JsonValue.Create(text),
        bool boolean => JsonValue.Create(boolean),
        int number => JsonValue.Create(number),
        long number => JsonValue.Create(number),
        double number => JsonValue.Create(number),
        float number => JsonValue.Create(number),
        decimal number => JsonValue.Create(number),
        _ => JsonValue.Create(value.ToString()),
    };

    private static IReadOnlyDictionary<string, object>? DeserializeMetadata(
        string json)
    {
        try
        {
            return DeserializeMetadataObject(JsonNode.Parse(json));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Stored dataset metadata contains invalid JSON.",
                exception);
        }
    }

    private static Dictionary<string, object>? DeserializeMetadataObject(
        JsonNode? node)
    {
        if (node is null)
            return null;
        if (node is not JsonObject metadata)
            throw new InvalidDataException("Dataset metadata must be a JSON object.");
        return metadata.ToDictionary(
            static pair => pair.Key,
            static pair => (object)ToJsonElement(pair.Value));
    }

    private static JsonElement ToJsonElement(JsonNode? node)
    {
        using JsonDocument document = JsonDocument.Parse(
            node?.ToJsonString() ?? "null");
        return document.RootElement.Clone();
    }

    private static string? EmptyToNull(string value) =>
        value.Length == 0 ? null : value;

    private static string RequiredString(JsonObject value, string property) =>
        OptionalString(value, property)
        ?? throw new InvalidDataException(
            $"Dataset payload requires string property '{property}'.");

    private static string? OptionalString(
        JsonObject value,
        string property) =>
        value[property] is JsonValue node &&
        node.TryGetValue(out string? text)
            ? text
            : null;

    private static DateTimeOffset? OptionalDateTimeOffset(
        JsonObject value,
        string property)
    {
        string? text = OptionalString(value, property);
        return text is null
            ? null
            : DateTimeOffset.Parse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal);
    }

    private static string InputTypeName<TInput>() =>
        typeof(TInput).FullName ?? typeof(TInput).Name;

    private async ValueTask<BaseRecord<T>?> GetRecordAsync<T>(
        BaseCollection<T> collection,
        RecordId id,
        CancellationToken cancellationToken)
    {
        BaseResult<BaseRecord<T>> result = await _session.Collection(collection)
            .GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseRecord<T>> failure &&
            failure.Status == OperationStatus.NotFound)
        {
            return null;
        }
        return result.RequireValue();
    }

    private async IAsyncEnumerable<T> QueryRecordsAsync<T>(
        BaseCollection<T> collection,
        Func<BaseQuery<T>, BaseQuery<T>> configure,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        BaseQuery<T> query = configure(_session.Collection(collection).Query());
        while (true)
        {
            BasePage<BaseRecord<T>> page = (await query.PageAsync(
                    cancellationToken).ConfigureAwait(false))
                .RequireValue();
            foreach (BaseRecord<T> item in page.Items)
                yield return item.Value;

            if (!page.Page.HasMore ||
                string.IsNullOrWhiteSpace(page.Page.NextCursor))
            {
                yield break;
            }
            query = query.ContinueFrom(page.Page.NextCursor);
        }
    }

    private static RecordId HeadRecordId(string datasetId) =>
        RecordId.Create(HashKey("dataset", datasetId));

    private static RecordId VersionRecordId(
        string datasetId,
        string version) =>
        RecordId.Create(HashKey(
            "dataset-version",
            datasetId + "\0" + version));

    private static string HashKey(string kind, string value) =>
        kind + "_" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool IsConcurrencyFailure(Exception exception) =>
        exception switch
        {
            BaseBatchOutcomeException batch =>
                batch.Error?.Code is BaseMutationErrorCodes.TransactionConflict
                    or BaseMutationErrorCodes.RevisionConflict
                    or BaseMutationErrorCodes.UpsertPreconditionFailed,
            BaseOperationException operation =>
                operation.Error.Code is BaseMutationErrorCodes.TransactionConflict
                    or BaseMutationErrorCodes.RevisionConflict
                    or BaseMutationErrorCodes.UpsertPreconditionFailed,
            _ => false,
        };
}
