using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent;

/// <summary>Durable storage contract for runtime extension schema declarations.</summary>
public interface IRuntimeAgentExtensionSchemaStore
{
    ValueTask<IReadOnlyList<RuntimeAgentExtensionSchema>> LoadAsync(
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        RuntimeAgentExtensionSchema schema,
        CancellationToken cancellationToken = default);
}

/// <summary>File-backed immutable runtime schema storage using atomic file replacement.</summary>
public sealed class FileRuntimeAgentExtensionSchemaStore : IRuntimeAgentExtensionSchemaStore
{
    private const int MaximumSchemaFileBytes = 1_048_576;
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileRuntimeAgentExtensionSchemaStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public async ValueTask<IReadOnlyList<RuntimeAgentExtensionSchema>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            var result = new List<RuntimeAgentExtensionSchema>();
            foreach (var path in Directory.EnumerateFiles(_directory, "*.json").OrderBy(static value => value, StringComparer.Ordinal))
            {
                var info = new FileInfo(path);
                if (info.Length > MaximumSchemaFileBytes)
                    throw new InvalidDataException($"Runtime schema file '{Path.GetFileName(path)}' exceeds the size limit.");
                await using var stream = File.OpenRead(path);
                var schema = await JsonSerializer.DeserializeAsync(
                        stream,
                        RuntimeAgentExtensionJsonContext.Default.RuntimeAgentExtensionSchema,
                        cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new InvalidDataException($"Runtime schema file '{Path.GetFileName(path)}' is empty.");
                result.Add(schema);
            }
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SaveAsync(
        RuntimeAgentExtensionSchema schema,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            var target = Path.Combine(_directory, FileName(schema.Key));
            var temporary = target + $".tmp-{System.Environment.ProcessId}-{Guid.NewGuid():N}";
            try
            {
                await using (var stream = new FileStream(
                                 temporary,
                                 new FileStreamOptions
                                 {
                                     Mode = FileMode.CreateNew,
                                     Access = FileAccess.Write,
                                     Share = FileShare.None,
                                     BufferSize = 16_384,
                                     Options = FileOptions.Asynchronous | FileOptions.WriteThrough
                                 }))
                {
                    await JsonSerializer.SerializeAsync(
                            stream,
                            schema,
                            RuntimeAgentExtensionJsonContext.Default.RuntimeAgentExtensionSchema,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                File.Move(temporary, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string FileName(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant() + ".json";
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(RuntimeAgentExtensionSchema))]
internal sealed partial class RuntimeAgentExtensionJsonContext : JsonSerializerContext;
