using System.Text.Json;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Serialization;
using HPDAgent.Graph.Abstractions.Storage;

namespace HPDAgent.Graph.Core.Storage;

/// <summary>
/// File-backed scheduled graph store using one JSON file per graph schedule.
/// </summary>
public sealed class JsonScheduledGraphStore : IScheduledGraphStore
{
    private readonly string _schedulesDirectory;

    public JsonScheduledGraphStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _schedulesDirectory = Path.Combine(rootDirectory, "schedules");
    }

    public async Task<ScheduledGraph?> LoadAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ct.ThrowIfCancellationRequested();

        var path = GetSchedulePath(graphId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
            stream,
            GraphConfigJsonSerializerContext.Default.ScheduledGraph,
            ct);
    }

    public async Task SaveAsync(ScheduledGraph schedule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule.GraphId);
        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_schedulesDirectory);
        var path = GetSchedulePath(schedule.GraphId);
        await WriteJsonAsync(path, schedule, ct);
    }

    public Task DeleteAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ct.ThrowIfCancellationRequested();

        var path = GetSchedulePath(graphId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ScheduledGraph>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!Directory.Exists(_schedulesDirectory))
        {
            return Array.Empty<ScheduledGraph>();
        }

        var schedules = new List<ScheduledGraph>();
        foreach (var path in Directory.EnumerateFiles(_schedulesDirectory, "*.schedule.json").OrderBy(static p => p, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            await using var stream = File.OpenRead(path);
            var schedule = await JsonSerializer.DeserializeAsync(
                stream,
                GraphConfigJsonSerializerContext.Default.ScheduledGraph,
                ct);

            if (schedule is not null)
            {
                schedules.Add(schedule);
            }
        }

        return schedules.OrderBy(static schedule => schedule.GraphId, StringComparer.Ordinal).ToList();
    }

    private string GetSchedulePath(string graphId) =>
        Path.Combine(_schedulesDirectory, $"{EncodeFileName(graphId)}.schedule.json");

    private static async Task WriteJsonAsync(string path, ScheduledGraph schedule, CancellationToken ct)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                schedule,
                GraphConfigJsonSerializerContext.Default.ScheduledGraph,
                ct);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private static string EncodeFileName(string value) => Uri.EscapeDataString(value);
}
