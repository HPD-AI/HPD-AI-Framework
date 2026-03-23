namespace HPD.ML.Serialization.Zip;

using System.Buffers;
using System.IO.Compression;
using System.Text.Json;
using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Saves and loads IModel as ZIP archives.
/// </summary>
public sealed class ZipSerializer : ISerializer
{
    private readonly ParameterWriterRegistry _parameterWriters = new();
    private readonly JsonSerializerOptions _jsonOptions;

    public ZipSerializer()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine(
                SerializerJsonContext.Default,
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()),
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <summary>Register a custom parameter writer for a learned parameters type.</summary>
    public void RegisterParameterWriter<TParams>(IParameterWriter<TParams> writer)
        where TParams : ILearnedParameters
    {
        _parameterWriters.Register(writer);
    }

    public void Save(SaveRequest request)
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var pipeline = new List<TransformEntry>();
            string? parameterType = null;
            bool hasInferenceState = false;

            // ── Learned Parameters ─────────────────────────────────
            if (request.Content.HasFlag(SaveContent.LearnedParameters))
            {
                var parameters = request.Model.Parameters;
                parameterType = parameters.GetType().Name;

                var writer = _parameterWriters.GetWriter(parameters);
                if (writer is not null)
                {
                    var weightsEntry = archive.CreateEntry("parameters/weights.bin");
                    using (var stream = weightsEntry.Open())
                        writer.WriteWeights(parameters, stream);

                    var metadataEntry = archive.CreateEntry("parameters/metadata.json");
                    using (var stream = metadataEntry.Open())
                        writer.WriteMetadata(parameters, stream, _jsonOptions);
                }
                else
                {
                    // Fallback: serialize as JSON using runtime type
                    var fallbackEntry = archive.CreateEntry("parameters/parameters.json");
                    using var stream = fallbackEntry.Open();
                    JsonSerializer.Serialize(stream, parameters, parameters.GetType(), _jsonOptions);
                }
            }

            // ── Pipeline Topology ──────────────────────────────────
            if (request.Content.HasFlag(SaveContent.PipelineTopology))
            {
                var topologyEntry = archive.CreateEntry("topology/pipeline.json");
                using (var stream = topologyEntry.Open())
                    TopologyWriter.Write(request.Model.Transform, stream, pipeline, _jsonOptions);

                var schemaEntry = archive.CreateEntry("topology/schema.json");
                using (var stream = schemaEntry.Open())
                    SchemaWriter.Write(request.Model.Transform, stream, _jsonOptions);
            }

            // ── Inference State ────────────────────────────────────
            if (request.Content.HasFlag(SaveContent.InferenceState) && request.InferenceState is not null)
            {
                hasInferenceState = true;
                var stateEntry = archive.CreateEntry("state/state.bin");
                using var stream = stateEntry.Open();
                var json = JsonSerializer.SerializeToUtf8Bytes(request.InferenceState, request.InferenceState.GetType());
                stream.Write(json);
            }

            // ── Manifest (written last, but uses collected data) ──
            var manifest = new Manifest
            {
                Content = request.Content,
                SavedAtUtc = DateTime.UtcNow,
                ParameterType = parameterType,
                Pipeline = pipeline.Count > 0 ? pipeline : null,
                HasInferenceState = hasInferenceState
            };

            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var stream = manifestEntry.Open())
                JsonSerializer.Serialize(stream, manifest, _jsonOptions);
        }

        // Copy ZIP bytes to destination
        zipStream.Position = 0;
        var bytes = zipStream.GetBuffer().AsSpan(0, (int)zipStream.Length);
        var dest = request.Destination.GetSpan(bytes.Length);
        bytes.CopyTo(dest);
        request.Destination.Advance(bytes.Length);
    }

    public IModel Load(LoadRequest request)
    {
        using var memoryStream = new MemoryStream(request.Source.ToArray());
        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read);

        // Read manifest
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("ZIP archive missing manifest.json");
        Manifest manifest;
        using (var stream = manifestEntry.Open())
            manifest = JsonSerializer.Deserialize<Manifest>(stream, _jsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize manifest.");

        if (manifest.FormatId != "hpd-ml-zip-v1")
            throw new InvalidOperationException(
                $"Unsupported format: {manifest.FormatId}. Expected hpd-ml-zip-v1.");

        // ── Load Parameters ────────────────────────────────────
        ILearnedParameters parameters;
        if (manifest.Content.HasFlag(SaveContent.LearnedParameters) && manifest.ParameterType is not null)
        {
            var writer = _parameterWriters.GetWriterByTypeName(manifest.ParameterType);
            if (writer is not null)
            {
                using var weightsStream = archive.GetEntry("parameters/weights.bin")!.Open();
                using var metadataStream = archive.GetEntry("parameters/metadata.json")!.Open();
                parameters = writer.ReadModel(weightsStream, metadataStream, _jsonOptions);
            }
            else
            {
                // No registered writer — check for JSON fallback
                var fallbackEntry = archive.GetEntry("parameters/parameters.json")
                    ?? throw new InvalidOperationException(
                        $"No parameter writer registered for '{manifest.ParameterType}' " +
                        "and no parameters/parameters.json fallback found.");

                // Without a registered writer, we can't deserialize the JSON back
                // to a concrete type. The caller must register the appropriate writer.
                throw new InvalidOperationException(
                    $"No parameter writer registered for '{manifest.ParameterType}'. " +
                    "Register a writer via RegisterParameterWriter before loading.");
            }
        }
        else
        {
            parameters = EmptyParameters.Instance;
        }

        // ── Load Pipeline ──────────────────────────────────────
        ITransform transform;
        if (manifest.Content.HasFlag(SaveContent.PipelineTopology) && manifest.Pipeline is not null)
        {
            using var stream = archive.GetEntry("topology/pipeline.json")!.Open();
            transform = TopologyWriter.Read(stream, manifest.Pipeline, _jsonOptions);
        }
        else
        {
            transform = new IdentityTransform();
        }

        return new Model(transform, parameters);
    }
}

/// <summary>Sentinel for models loaded without parameters.</summary>
internal sealed class EmptyParameters : ILearnedParameters
{
    public static readonly EmptyParameters Instance = new();
}
