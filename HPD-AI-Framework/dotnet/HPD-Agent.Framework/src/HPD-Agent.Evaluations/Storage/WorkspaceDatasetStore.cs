// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using HPD.Agent.Evaluations.Batch;

namespace HPD.Agent.Evaluations.Storage;

/// <summary>
/// Workspace-backed dataset registry. Datasets are workspace spaces and immutable
/// versions are typed documents attached to those spaces.
/// </summary>
public sealed class WorkspaceDatasetStore : IDatasetStore
{
    public const string DatasetKind = "eval_dataset";
    public const string DatasetVersionRole = "eval_dataset_version";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IWorkspaceStore _workspace;
    private readonly WorkspacePrincipalRef _principal;

    public WorkspaceDatasetStore(
        IWorkspaceStore workspace,
        WorkspacePrincipalRef? principal = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _principal = principal ?? WorkspacePrincipalRef.System;
    }

    public async ValueTask<DatasetVersionRecord> RegisterDatasetVersionAsync<TInput>(
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

        var datasetId = dataset.DatasetId!;
        var version = dataset.Version!;
        var datasetSpace = await GetOrCreateDatasetSpaceAsync(datasetId, ct).ConfigureAwait(false);

        var existing = await LoadVersionEnvelopeAsync<TInput>(datasetSpace.Id, version, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            var normalizedCandidate = await NormalizeWithExistingVersionsAsync(datasetSpace.Id, dataset, options, ct)
                .ConfigureAwait(false);
            if (!string.Equals(existing.Record.ContentHash, normalizedCandidate.Record.ContentHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Dataset '{datasetId}' version '{version}' is already registered with different content.");
            }

            return existing.Record;
        }

        var normalized = await NormalizeWithExistingVersionsAsync(datasetSpace.Id, dataset, options, ct)
            .ConfigureAwait(false);

        await WriteVersionDocumentAsync(datasetSpace.Id, normalized, ct).ConfigureAwait(false);
        return normalized.Record;
    }

    public async ValueTask<DatasetRecord?> GetDatasetAsync(
        string datasetId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var datasetSpace = await FindDatasetSpaceAsync(datasetId, ct).ConfigureAwait(false);
        if (datasetSpace is null)
            return null;

        var versions = await LoadVersionRecordsAsync(datasetSpace.Id, ct).ConfigureAwait(false);
        var current = versions
            .OrderBy(v => v.RegisteredAt)
            .ThenBy(v => v.Version, StringComparer.Ordinal)
            .LastOrDefault();

        return current is null
            ? null
            : new DatasetRecord(
                current.DatasetId,
                current.Version,
                current.Description,
                current.ContentHash,
                current.RegisteredAt,
                current.Metadata);
    }

    public async IAsyncEnumerable<DatasetRecord> ListDatasetsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var spaces = await _workspace.ListSpacesAsync(
            _principal,
            new WorkspaceSpaceQuery { Kind = DatasetKind },
            ct).ConfigureAwait(false);

        foreach (var space in spaces.OrderBy(s => s.ExternalId, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var record = await GetDatasetAsync(space.ExternalId, ct).ConfigureAwait(false);
            if (record is not null)
                yield return record;
        }
    }

    public async ValueTask<Dataset<TInput>?> GetDatasetVersionAsync<TInput>(
        string datasetId,
        string version,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var datasetSpace = await FindDatasetSpaceAsync(datasetId, ct).ConfigureAwait(false);
        if (datasetSpace is null)
            return null;

        var envelope = await LoadVersionEnvelopeAsync<TInput>(datasetSpace.Id, version, ct).ConfigureAwait(false);
        return envelope?.Dataset.ToDataset();
    }

    public async IAsyncEnumerable<DatasetVersionRecord> GetDatasetVersionsAsync(
        string datasetId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var datasetSpace = await FindDatasetSpaceAsync(datasetId, ct).ConfigureAwait(false);
        if (datasetSpace is null)
            yield break;

        var versions = await LoadVersionRecordsAsync(datasetSpace.Id, ct).ConfigureAwait(false);
        foreach (var version in versions
            .OrderBy(v => v.RegisteredAt)
            .ThenBy(v => v.Version, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            yield return version;
        }
    }

    public async IAsyncEnumerable<EvalCase<TInput>> GetActiveCasesAsync<TInput>(
        string datasetId,
        DateTimeOffset at,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var hydrated = await HydrateStoreAsync<TInput>(datasetId, ct).ConfigureAwait(false);
        await foreach (var evalCase in hydrated.GetActiveCasesAsync<TInput>(datasetId, at, ct).ConfigureAwait(false))
            yield return evalCase;
    }

    public async IAsyncEnumerable<EvalCase<TInput>> GetCaseHistoryAsync<TInput>(
        string datasetId,
        string caseId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var hydrated = await HydrateStoreAsync<TInput>(datasetId, ct).ConfigureAwait(false);
        await foreach (var evalCase in hydrated.GetCaseHistoryAsync<TInput>(datasetId, caseId, ct).ConfigureAwait(false))
            yield return evalCase;
    }

    public async ValueTask<DatasetVersionDiff<TInput>> CompareVersionsAsync<TInput>(
        string datasetId,
        string fromVersion,
        string toVersion,
        CancellationToken ct = default)
    {
        var hydrated = await HydrateStoreAsync<TInput>(datasetId, ct).ConfigureAwait(false);
        return await hydrated.CompareVersionsAsync<TInput>(datasetId, fromVersion, toVersion, ct).ConfigureAwait(false);
    }

    private async Task<StoredDatasetVersion<TInput>> NormalizeWithExistingVersionsAsync<TInput>(
        string datasetSpaceId,
        Dataset<TInput> dataset,
        DatasetRegistrationOptions<TInput>? options,
        CancellationToken ct)
    {
        var hydrated = await HydrateStoreAsync<TInput>(datasetSpaceId, isSpaceId: true, ct).ConfigureAwait(false);
        var record = await hydrated.RegisterDatasetVersionAsync(dataset, options, ct).ConfigureAwait(false);
        var normalized = await hydrated.GetDatasetVersionAsync<TInput>(record.DatasetId, record.Version, ct)
            .ConfigureAwait(false);

        if (normalized is null)
            throw new InvalidOperationException($"Dataset '{record.DatasetId}' version '{record.Version}' was not registered.");

        return new StoredDatasetVersion<TInput>(record, normalized.ToDto());
    }

    private async Task<InMemoryDatasetStore> HydrateStoreAsync<TInput>(
        string datasetIdOrSpaceId,
        CancellationToken ct) =>
        await HydrateStoreAsync<TInput>(datasetIdOrSpaceId, isSpaceId: false, ct).ConfigureAwait(false);

    private async Task<InMemoryDatasetStore> HydrateStoreAsync<TInput>(
        string datasetIdOrSpaceId,
        bool isSpaceId,
        CancellationToken ct)
    {
        var store = new InMemoryDatasetStore();
        var datasetSpace = isSpaceId
            ? await _workspace.GetSpaceAsync(_principal, datasetIdOrSpaceId, ct).ConfigureAwait(false)
            : await FindDatasetSpaceAsync(datasetIdOrSpaceId, ct).ConfigureAwait(false);

        if (datasetSpace is null)
            return store;

        var attachments = await ListVersionAttachmentsAsync(datasetSpace.Id, ct).ConfigureAwait(false);
        foreach (var attachment in attachments.OrderBy(a => a.CreatedAt))
        {
            var envelope = await LoadVersionEnvelopeAsync<TInput>(attachment, ct).ConfigureAwait(false);
            if (envelope is null)
                continue;

            await store.RegisterDatasetVersionAsync(
                envelope.Dataset.ToDataset(),
                new DatasetRegistrationOptions<TInput>
                {
                    Description = envelope.Record.Description,
                    Metadata = envelope.Record.Metadata,
                    RegisteredAt = envelope.Record.RegisteredAt
                },
                ct).ConfigureAwait(false);
        }

        return store;
    }

    private async Task WriteVersionDocumentAsync<TInput>(
        string datasetSpaceId,
        StoredDatasetVersion<TInput> envelope,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await JsonSerializer.SerializeAsync(buffer, envelope, JsonOptions, ct).ConfigureAwait(false);
        buffer.Position = 0;

        await _workspace.WriteContentAsync(
            _principal,
            datasetSpaceId,
            existingAttachmentId: null,
            buffer,
            new WriteWorkspaceSpaceContentRequest
            {
                ContentType = "application/json",
                Role = DatasetVersionRole,
                Name = $"{envelope.Record.Version}.json",
                Permission = "read_write",
                AttachmentMetadata = new Dictionary<string, string>
                {
                    ["document_type"] = "dataset_version",
                    ["dataset_id"] = envelope.Record.DatasetId,
                    ["version"] = envelope.Record.Version,
                    ["content_hash"] = envelope.Record.ContentHash,
                    ["case_count"] = envelope.Record.CaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }
            },
            ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<DatasetVersionRecord>> LoadVersionRecordsAsync(
        string datasetSpaceId,
        CancellationToken ct)
    {
        var records = new List<DatasetVersionRecord>();
        var attachments = await ListVersionAttachmentsAsync(datasetSpaceId, ct).ConfigureAwait(false);
        foreach (var attachment in attachments)
        {
            await using var stream = await _workspace.OpenContentAsync(
                _principal,
                attachment.ContentId,
                attachment.ContentVersion,
                ct).ConfigureAwait(false);
            if (stream is null)
                continue;

            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("record", out var recordElement))
                continue;

            var record = recordElement.Deserialize<DatasetVersionRecord>(JsonOptions);
            if (record is not null)
                records.Add(record);
        }

        return records;
    }

    private async Task<StoredDatasetVersion<TInput>?> LoadVersionEnvelopeAsync<TInput>(
        string datasetSpaceId,
        string version,
        CancellationToken ct)
    {
        var attachments = await ListVersionAttachmentsAsync(datasetSpaceId, ct).ConfigureAwait(false);
        foreach (var attachment in attachments)
        {
            if (attachment.Metadata?.TryGetValue("version", out var attachmentVersion) == true &&
                !string.Equals(attachmentVersion, version, StringComparison.Ordinal))
            {
                continue;
            }

            var envelope = await LoadVersionEnvelopeAsync<TInput>(attachment, ct).ConfigureAwait(false);
            if (envelope is not null && string.Equals(envelope.Record.Version, version, StringComparison.Ordinal))
                return envelope;
        }

        return null;
    }

    private async Task<StoredDatasetVersion<TInput>?> LoadVersionEnvelopeAsync<TInput>(
        WorkspaceContentAttachmentInfo attachment,
        CancellationToken ct)
    {
        await using var stream = await _workspace.OpenContentAsync(
            _principal,
            attachment.ContentId,
            attachment.ContentVersion,
            ct).ConfigureAwait(false);
        if (stream is null)
            return null;

        return await JsonSerializer.DeserializeAsync<StoredDatasetVersion<TInput>>(stream, JsonOptions, ct)
            .ConfigureAwait(false);
    }

    private Task<IReadOnlyList<WorkspaceContentAttachmentInfo>> ListVersionAttachmentsAsync(
        string datasetSpaceId,
        CancellationToken ct) =>
        _workspace.ListContentAsync(
            _principal,
            datasetSpaceId,
            new WorkspaceContentAttachmentQuery { Role = DatasetVersionRole },
            ct);

    private async Task<WorkspaceSpaceInfo> GetOrCreateDatasetSpaceAsync(
        string datasetId,
        CancellationToken ct)
    {
        var existing = await FindDatasetSpaceAsync(datasetId, ct).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        return await _workspace.CreateSpaceAsync(
            _principal,
            new CreateWorkspaceSpaceRequest
            {
                Kind = DatasetKind,
                ExternalId = datasetId,
                Name = datasetId
            },
            ct).ConfigureAwait(false);
    }

    private Task<WorkspaceSpaceInfo?> FindDatasetSpaceAsync(
        string datasetId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        return _workspace.FindSpaceAsync(
            _principal,
            new WorkspaceSpaceQuery
            {
                Kind = DatasetKind,
                ExternalId = datasetId
            },
            ct);
    }

    private sealed record StoredDatasetVersion<TInput>(
        DatasetVersionRecord Record,
        DatasetDto<TInput> Dataset);
}
