// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent.Evaluations.Batch;

namespace HPD.Agent.Evaluations.Storage;

/// <summary>
/// In-memory dataset registry with immutable version snapshots and SCD-2 case history.
/// </summary>
public sealed class InMemoryDatasetStore : IDatasetStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DatasetRecord> _datasets = new(StringComparer.Ordinal);
    private readonly Dictionary<(string DatasetId, string Version), DatasetVersionRecord> _versions = new();
    private readonly Dictionary<(string DatasetId, string Version), object> _snapshots = new();
    private readonly Dictionary<(string DatasetId, string CaseId), List<CaseHistoryEntry>> _caseHistory = new();

    public ValueTask<DatasetVersionRecord> RegisterDatasetVersionAsync<TInput>(
        Dataset<TInput> dataset,
        DatasetRegistrationOptions<TInput>? options = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(dataset);

        if (string.IsNullOrWhiteSpace(dataset.DatasetId))
            throw new ArgumentException("DatasetId is required.", nameof(dataset));

        if (string.IsNullOrWhiteSpace(dataset.Version))
            throw new ArgumentException("Version is required.", nameof(dataset));

        options ??= new DatasetRegistrationOptions<TInput>();
        var registeredAt = options.RegisteredAt ?? DateTimeOffset.UtcNow;
        var normalized = NormalizeDataset(dataset, options, registeredAt);
        var contentHash = ComputeDatasetHash(normalized, options.FingerprintCase);
        var key = (normalized.DatasetId!, normalized.Version!);

        lock (_gate)
        {
            if (_versions.TryGetValue(key, out var existing))
            {
                if (!string.Equals(existing.ContentHash, contentHash, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Dataset '{key.Item1}' version '{key.Item2}' is already registered with different content.");

                return ValueTask.FromResult(existing);
            }

            var version = new DatasetVersionRecord(
                DatasetId: key.Item1,
                Version: key.Item2,
                Description: options.Description,
                ContentHash: contentHash,
                CaseCount: normalized.Cases.Count,
                RegisteredAt: registeredAt,
                Metadata: options.Metadata);

            _versions[key] = version;
            _snapshots[key] = normalized;
            _datasets[key.Item1] = new DatasetRecord(
                DatasetId: key.Item1,
                CurrentVersion: key.Item2,
                Description: options.Description,
                ContentHash: contentHash,
                RegisteredAt: registeredAt,
                Metadata: options.Metadata);

            AddCaseHistory(normalized, registeredAt, options.FingerprintCase);
            return ValueTask.FromResult(version);
        }
    }

    public ValueTask<DatasetRecord?> GetDatasetAsync(string datasetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult(_datasets.GetValueOrDefault(datasetId));
    }

    public async IAsyncEnumerable<DatasetRecord> ListDatasetsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        List<DatasetRecord> records;
        lock (_gate)
            records = _datasets.Values.OrderBy(d => d.DatasetId, StringComparer.Ordinal).ToList();

        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            yield return record;
            await Task.Yield();
        }
    }

    public ValueTask<Dataset<TInput>?> GetDatasetVersionAsync<TInput>(
        string datasetId,
        string version,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var snapshot = _snapshots.GetValueOrDefault((datasetId, version));
            return ValueTask.FromResult(snapshot as Dataset<TInput>);
        }
    }

    public async IAsyncEnumerable<DatasetVersionRecord> GetDatasetVersionsAsync(
        string datasetId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        List<DatasetVersionRecord> records;
        lock (_gate)
        {
            records = _versions.Values
                .Where(v => string.Equals(v.DatasetId, datasetId, StringComparison.Ordinal))
                .OrderBy(v => v.RegisteredAt)
                .ThenBy(v => v.Version, StringComparer.Ordinal)
                .ToList();
        }

        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            yield return record;
            await Task.Yield();
        }
    }

    public async IAsyncEnumerable<EvalCase<TInput>> GetActiveCasesAsync<TInput>(
        string datasetId,
        DateTimeOffset at,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        List<EvalCase<TInput>> cases;
        lock (_gate)
        {
            cases = _caseHistory
                .Where(kvp => string.Equals(kvp.Key.DatasetId, datasetId, StringComparison.Ordinal))
                .SelectMany(kvp => kvp.Value)
                .Where(e => e.Case is EvalCase<TInput>
                    && e.ValidFrom <= at
                    && (e.ValidTo is null || e.ValidTo > at))
                .GroupBy(e => e.CaseId, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(e => e.ValidFrom).First())
                .Select(e => ApplyHistoryWindow((EvalCase<TInput>)e.Case, e.ValidFrom, e.ValidTo))
                .OrderBy(c => c.CaseId, StringComparer.Ordinal)
                .ToList();
        }

        foreach (var evalCase in cases)
        {
            ct.ThrowIfCancellationRequested();
            yield return evalCase;
            await Task.Yield();
        }
    }

    public async IAsyncEnumerable<EvalCase<TInput>> GetCaseHistoryAsync<TInput>(
        string datasetId,
        string caseId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        List<EvalCase<TInput>> cases;
        lock (_gate)
        {
            cases = _caseHistory.GetValueOrDefault((datasetId, caseId))?
                .Where(e => e.Case is EvalCase<TInput>)
                .OrderBy(e => e.ValidFrom)
                .Select(e => ApplyHistoryWindow((EvalCase<TInput>)e.Case, e.ValidFrom, e.ValidTo))
                .ToList() ?? [];
        }

        foreach (var evalCase in cases)
        {
            ct.ThrowIfCancellationRequested();
            yield return evalCase;
            await Task.Yield();
        }
    }

    public ValueTask<DatasetVersionDiff<TInput>> CompareVersionsAsync<TInput>(
        string datasetId,
        string fromVersion,
        string toVersion,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_snapshots.GetValueOrDefault((datasetId, fromVersion)) is not Dataset<TInput> from)
                throw new KeyNotFoundException($"Dataset '{datasetId}' version '{fromVersion}' was not found.");

            if (_snapshots.GetValueOrDefault((datasetId, toVersion)) is not Dataset<TInput> to)
                throw new KeyNotFoundException($"Dataset '{datasetId}' version '{toVersion}' was not found.");

            var fromById = from.Cases
                .Where(c => !string.IsNullOrWhiteSpace(c.CaseId))
                .ToDictionary(c => c.CaseId!, StringComparer.Ordinal);
            var toById = to.Cases
                .Where(c => !string.IsNullOrWhiteSpace(c.CaseId))
                .ToDictionary(c => c.CaseId!, StringComparer.Ordinal);

            var added = toById
                .Where(kvp => !fromById.ContainsKey(kvp.Key))
                .Select(kvp => kvp.Value)
                .OrderBy(c => c.CaseId, StringComparer.Ordinal)
                .ToList();

            var removed = fromById
                .Where(kvp => !toById.ContainsKey(kvp.Key))
                .Select(kvp => kvp.Value)
                .OrderBy(c => c.CaseId, StringComparer.Ordinal)
                .ToList();

            var changed = fromById.Keys
                .Intersect(toById.Keys, StringComparer.Ordinal)
                .Where(id => !CasesEquivalent(fromById[id], toById[id]))
                .OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => new DatasetCaseChange<TInput>(id, fromById[id], toById[id]))
                .ToList();

            return ValueTask.FromResult(new DatasetVersionDiff<TInput>(
                datasetId,
                fromVersion,
                toVersion,
                added,
                removed,
                changed));
        }
    }

    private void AddCaseHistory<TInput>(
        Dataset<TInput> dataset,
        DateTimeOffset registeredAt,
        Func<EvalCase<TInput>, string>? fingerprintCase)
    {
        foreach (var evalCase in dataset.Cases)
        {
            var caseId = evalCase.CaseId!;
            var validFrom = evalCase.ValidFrom ?? registeredAt;
            var key = (dataset.DatasetId!, caseId);
            if (!_caseHistory.TryGetValue(key, out var history))
            {
                history = [];
                _caseHistory[key] = history;
            }

            foreach (var open in history.Where(e => e.ValidTo is null && e.ValidFrom < validFrom))
                open.ValidTo = validFrom;

            history.Add(new CaseHistoryEntry(
                Case: evalCase,
                CaseId: caseId,
                CaseVersion: evalCase.Version ?? dataset.Version!,
                ValidFrom: validFrom,
                Fingerprint: fingerprintCase?.Invoke(evalCase) ?? FingerprintCase(evalCase))
            {
                ValidTo = evalCase.ValidTo,
            });
        }
    }

    private static Dataset<TInput> NormalizeDataset<TInput>(
        Dataset<TInput> dataset,
        DatasetRegistrationOptions<TInput> options,
        DateTimeOffset registeredAt)
    {
        var cases = dataset.Cases.Select((evalCase, index) =>
        {
            var caseId = evalCase.CaseId;
            if (string.IsNullOrWhiteSpace(caseId))
            {
                if (!options.GenerateMissingCaseIds)
                    throw new ArgumentException("All cases must have CaseId when GenerateMissingCaseIds is false.", nameof(dataset));

                caseId = $"case-{index}";
            }

            return new EvalCase<TInput>
            {
                CaseId = caseId,
                Name = evalCase.Name,
                Version = evalCase.Version ?? dataset.Version,
                ValidFrom = evalCase.ValidFrom ?? registeredAt,
                ValidTo = evalCase.ValidTo,
                Input = evalCase.Input,
                GroundTruth = evalCase.GroundTruth,
                Metadata = evalCase.Metadata,
                Evaluators = evalCase.Evaluators,
                ReportEvaluators = evalCase.ReportEvaluators,
            };
        }).ToList();

        return new Dataset<TInput>
        {
            DatasetId = dataset.DatasetId,
            Version = dataset.Version,
            Cases = cases,
            Evaluators = dataset.Evaluators,
            ReportEvaluators = dataset.ReportEvaluators,
        };
    }

    private static EvalCase<TInput> ApplyHistoryWindow<TInput>(
        EvalCase<TInput> evalCase,
        DateTimeOffset validFrom,
        DateTimeOffset? validTo) =>
        new()
        {
            CaseId = evalCase.CaseId,
            Name = evalCase.Name,
            Version = evalCase.Version,
            ValidFrom = validFrom,
            ValidTo = validTo,
            Input = evalCase.Input,
            GroundTruth = evalCase.GroundTruth,
            Metadata = evalCase.Metadata,
            Evaluators = evalCase.Evaluators,
            ReportEvaluators = evalCase.ReportEvaluators,
        };

    private static string ComputeDatasetHash<TInput>(
        Dataset<TInput> dataset,
        Func<EvalCase<TInput>, string>? fingerprintCase)
    {
        var builder = new StringBuilder()
            .Append(dataset.DatasetId).Append('|')
            .Append(dataset.Version).Append('|');

        foreach (var evalCase in dataset.Cases.OrderBy(c => c.CaseId, StringComparer.Ordinal))
        {
            builder
                .Append(evalCase.CaseId).Append('|')
                .Append(evalCase.Version).Append('|')
                .Append(evalCase.ValidFrom?.ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(evalCase.ValidTo?.ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(fingerprintCase?.Invoke(evalCase) ?? FingerprintCase(evalCase))
                .AppendLine();
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static bool CasesEquivalent<TInput>(EvalCase<TInput> left, EvalCase<TInput> right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal)
        && EqualityComparer<TInput>.Default.Equals(left.Input, right.Input)
        && string.Equals(left.GroundTruth, right.GroundTruth, StringComparison.Ordinal)
        && MetadataEquivalent(left.Metadata, right.Metadata);

    private static bool MetadataEquivalent(
        IDictionary<string, object>? left,
        IDictionary<string, object>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.Count != right.Count) return false;

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other)) return false;
            if (!Equals(value, other)) return false;
        }

        return true;
    }

    private static string FingerprintCase<TInput>(EvalCase<TInput> evalCase) =>
        string.Join("|",
            evalCase.Name,
            Convert.ToString(evalCase.Input, CultureInfo.InvariantCulture),
            evalCase.GroundTruth,
            FingerprintMetadata(evalCase.Metadata));

    private static string FingerprintMetadata(IDictionary<string, object>? metadata)
    {
        if (metadata is null || metadata.Count == 0) return string.Empty;

        return string.Join("|", metadata
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => $"{kvp.Key}={Convert.ToString(kvp.Value, CultureInfo.InvariantCulture)}"));
    }

    private sealed record CaseHistoryEntry(
        object Case,
        string CaseId,
        string CaseVersion,
        DateTimeOffset ValidFrom,
        string Fingerprint)
    {
        public DateTimeOffset? ValidTo { get; set; }
    }
}
