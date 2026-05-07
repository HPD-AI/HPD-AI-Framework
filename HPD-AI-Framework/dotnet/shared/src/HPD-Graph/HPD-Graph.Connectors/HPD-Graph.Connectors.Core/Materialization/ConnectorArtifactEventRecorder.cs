using System.Text.Json;
using HPD.Events;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Connectors.Abstractions.Events;

namespace HPDAgent.Graph.Connectors.Core.Materialization;

public interface IConnectorArtifactEventRecorder
{
    Task RecordAsync(Event evt, IArtifactRegistry artifacts, CancellationToken ct = default);
}

public sealed class ConnectorArtifactEventRecorder : IConnectorArtifactEventRecorder
{
    private readonly TimeProvider _timeProvider;

    public ConnectorArtifactEventRecorder(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task RecordAsync(Event evt, IArtifactRegistry artifacts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(artifacts);

        return evt switch
        {
            ExternalArtifactMaterializedEvent materialized => RecordMaterializedAsync(materialized, artifacts, ct),
            ArtifactObservedEvent observed => RecordObservedAsync(observed, artifacts, ct),
            ArtifactCheckCompletedEvent check => RecordCheckAsync(check, artifacts, ct),
            _ => Task.CompletedTask
        };
    }

    private Task RecordMaterializedAsync(
        ExternalArtifactMaterializedEvent evt,
        IArtifactRegistry artifacts,
        CancellationToken ct)
    {
        var version = !string.IsNullOrWhiteSpace(evt.Version)
            ? evt.Version!
            : !string.IsNullOrWhiteSpace(evt.ExternalRunId)
                ? $"external:{evt.ExternalRunId}"
                : $"external:{evt.MaterializedAt.ToUnixTimeMilliseconds()}";

        return artifacts.RegisterAsync(
            evt.ArtifactKey,
            version,
            new ArtifactMetadata
            {
                CreatedAt = evt.MaterializedAt,
                InputVersions = evt.InputVersions.ToDictionary(static input => input.ArtifactKey, static input => input.Version),
                CustomMetadata = BuildMetadata(
                    ("connector.eventKind", "external-materialized"),
                    ("connector.connectionId", evt.ConnectionId),
                    ("connector.externalRunId", evt.ExternalRunId),
                    ("connector.materializedAt", evt.MaterializedAt.ToString("O")),
                    ("connector.metadata", CloneNullable(evt.Metadata)))
            },
            ct);
    }

    private Task RecordObservedAsync(
        ArtifactObservedEvent evt,
        IArtifactRegistry artifacts,
        CancellationToken ct)
    {
        var version = !string.IsNullOrWhiteSpace(evt.ExternalRunId)
            ? $"observation:{evt.ExternalRunId}"
            : $"observation:{evt.ObservedAt.ToUnixTimeMilliseconds()}";

        return artifacts.RegisterAsync(
            evt.ArtifactKey,
            version,
            new ArtifactMetadata
            {
                CreatedAt = evt.ObservedAt,
                InputVersions = new Dictionary<ArtifactKey, string>(),
                CustomMetadata = BuildMetadata(
                    ("connector.eventKind", "observed"),
                    ("connector.connectionId", evt.ConnectionId),
                    ("connector.externalRunId", evt.ExternalRunId),
                    ("connector.observedAt", evt.ObservedAt.ToString("O")),
                    ("connector.metadata", CloneNullable(evt.Metadata)))
            },
            ct);
    }

    private async Task RecordCheckAsync(
        ArtifactCheckCompletedEvent evt,
        IArtifactRegistry artifacts,
        CancellationToken ct)
    {
        var version = await artifacts.GetLatestVersionAsync(evt.ArtifactKey, ct: ct).ConfigureAwait(false)
            ?? $"check:{Sanitize(evt.CheckName)}:{_timeProvider.GetUtcNow().ToUnixTimeMilliseconds()}";
        var existing = await artifacts.GetMetadataAsync(evt.ArtifactKey, version, ct).ConfigureAwait(false);
        var metadata = MergeMetadata(
            existing?.CustomMetadata,
            BuildMetadata(
                ("connector.eventKind", "check-completed"),
                ($"connector.checks.{evt.CheckName}.passed", evt.Passed),
                ($"connector.checks.{evt.CheckName}.severity", evt.Severity),
                ($"connector.checks.{evt.CheckName}.metadata", CloneNullable(evt.Metadata))));

        await artifacts.RegisterAsync(
            evt.ArtifactKey,
            version,
            new ArtifactMetadata
            {
                CreatedAt = existing?.CreatedAt ?? _timeProvider.GetUtcNow(),
                InputVersions = existing?.InputVersions ?? new Dictionary<ArtifactKey, string>(),
                ProducedByNodeId = existing?.ProducedByNodeId,
                ExecutionId = existing?.ExecutionId,
                CustomMetadata = metadata
            },
            ct).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, object> BuildMetadata(params (string Key, object? Value)[] entries)
    {
        var metadata = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            if (value is not null)
            {
                metadata[key] = value;
            }
        }

        return metadata;
    }

    private static IReadOnlyDictionary<string, object> MergeMetadata(
        IReadOnlyDictionary<string, object>? existing,
        IReadOnlyDictionary<string, object> next)
    {
        var metadata = existing is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(existing, StringComparer.Ordinal);

        foreach (var (key, value) in next)
        {
            metadata[key] = value;
        }

        return metadata;
    }

    private static JsonElement? CloneNullable(JsonElement? element) =>
        element.HasValue ? element.Value.Clone() : null;

    private static string Sanitize(string value)
    {
        var chars = value.Select(static ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray();
        return new string(chars);
    }
}
