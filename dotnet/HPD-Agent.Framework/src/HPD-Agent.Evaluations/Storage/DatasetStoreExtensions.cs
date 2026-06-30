// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json.Nodes;
using HPD.Agent.Evaluations.Batch;
using HPD.Serialization;

namespace HPD.Agent.Evaluations.Storage;

/// <summary>AOT-friendly import/export and report helpers for <see cref="IDatasetStore"/>.</summary>
public static class DatasetStoreExtensions
{
    public static async ValueTask ExportDatasetVersionToFileAsync<TInput>(
        this IDatasetStore store,
        string datasetId,
        string version,
        string path,
        Func<TInput, JsonNode?> serializeInput,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(serializeInput);

        var dataset = await store.GetDatasetVersionAsync<TInput>(datasetId, version, ct)
            .ConfigureAwait(false);
        if (dataset is null)
            throw new KeyNotFoundException($"Dataset '{datasetId}' version '{version}' was not found.");

        var node = DatasetYamlSerializer.ToJsonNode(dataset, serializeInput);
        var text = HpdConfigSerializer.InferFormat(path) switch
        {
            HpdConfigFormat.Json => node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            HpdConfigFormat.Yaml => HpdConfigSerializer.WriteYaml(node),
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
        };

        await File.WriteAllTextAsync(path, text, ct).ConfigureAwait(false);
    }

    public static async ValueTask<IReadOnlyList<string>> ExportDatasetVersionsToDirectoryAsync<TInput>(
        this IDatasetStore store,
        string datasetId,
        string directory,
        Func<TInput, JsonNode?> serializeInput,
        string extension = ".yaml",
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        Directory.CreateDirectory(directory);

        var paths = new List<string>();
        await foreach (var version in store.GetDatasetVersionsAsync(datasetId, ct).ConfigureAwait(false))
        {
            var path = Path.Combine(
                directory,
                $"{SanitizeFileName(datasetId)}-{SanitizeFileName(version.Version)}{NormalizeExtension(extension)}");
            await store.ExportDatasetVersionToFileAsync<TInput>(
                datasetId,
                version.Version,
                path,
                serializeInput,
                ct).ConfigureAwait(false);
            paths.Add(path);
        }

        return paths;
    }

    public static async ValueTask<DatasetVersionRecord> ImportDatasetVersionFromFileAsync<TInput>(
        this IDatasetStore store,
        string path,
        Func<JsonNode?, TInput> parseInput,
        DatasetRegistrationOptions<TInput>? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(parseInput);

        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var dataset = HpdConfigSerializer.InferFormat(path) switch
        {
            HpdConfigFormat.Json => DatasetYamlSerializer.FromJsonNode(JsonNode.Parse(text), parseInput),
            HpdConfigFormat.Yaml => Dataset<TInput>.FromYaml(text, parseInput),
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
        };
        return await store.RegisterDatasetVersionAsync(dataset, options, ct)
            .ConfigureAwait(false);
    }

    public static async ValueTask<DatasetDiffReport<TInput>> CreateDiffReportAsync<TInput>(
        this IDatasetStore store,
        string datasetId,
        string fromVersion,
        string toVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        var diff = await store.CompareVersionsAsync<TInput>(
            datasetId,
            fromVersion,
            toVersion,
            ct).ConfigureAwait(false);

        return new DatasetDiffReport<TInput>(
            diff.DatasetId,
            diff.FromVersion,
            diff.ToVersion,
            diff.Added.Count,
            diff.Removed.Count,
            diff.Changed.Count,
            diff.Added,
            diff.Removed,
            diff.Changed);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    private static string NormalizeExtension(string extension)
        => extension.StartsWith(".", StringComparison.Ordinal) ? extension : $".{extension}";
}
