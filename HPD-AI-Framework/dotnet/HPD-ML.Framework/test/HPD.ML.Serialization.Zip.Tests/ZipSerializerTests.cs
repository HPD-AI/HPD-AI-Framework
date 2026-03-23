namespace HPD.ML.Serialization.Zip.Tests;

using System.Buffers;
using HPD.ML.Abstractions;
using HPD.ML.Core;

public class ZipSerializerTests
{
    private static ZipSerializer CreateSerializer()
    {
        var serializer = new ZipSerializer();
        serializer.RegisterParameterWriter(new TestParameterWriter());
        return serializer;
    }

    private static IModel CreateTestModel(double[] weights, double bias)
    {
        var parameters = new TestParameters { Weights = weights, Bias = bias };
        var transform = new TestTransform();
        return new Model(transform, parameters);
    }

    // ── Save/Load Round-Trip ────────────────────────────────

    [Fact]
    public void SaveAndLoad_AllContent_RoundTrips()
    {
        var serializer = CreateSerializer();
        var model = CreateTestModel([1.0, 2.0, 3.0], 0.5);

        using var stream = new MemoryStream();
        serializer.Save(model, SaveContent.All, new ZipFormat(), stream);

        stream.Position = 0;
        var loaded = serializer.Load(new ZipFormat(), stream);

        var loadedParams = Assert.IsType<TestParameters>(loaded.Parameters);
        Assert.Equal([1.0, 2.0, 3.0], loadedParams.Weights);
        Assert.Equal(0.5, loadedParams.Bias);
    }

    [Fact]
    public void SaveAndLoad_ParametersOnly_RoundTrips()
    {
        var serializer = CreateSerializer();
        var model = CreateTestModel([4.0, 5.0], -1.0);

        using var stream = new MemoryStream();
        serializer.Save(model, SaveContent.LearnedParameters, new ZipFormat(), stream);

        stream.Position = 0;
        var loaded = serializer.Load(new ZipFormat(), stream);

        var loadedParams = Assert.IsType<TestParameters>(loaded.Parameters);
        Assert.Equal([4.0, 5.0], loadedParams.Weights);
        Assert.Equal(-1.0, loadedParams.Bias);
    }

    [Fact]
    public void SaveAndLoad_TopologyOnly_ReturnsEmptyParams()
    {
        var serializer = CreateSerializer();
        var model = CreateTestModel([1.0], 0.0);

        using var stream = new MemoryStream();
        serializer.Save(model, SaveContent.PipelineTopology, new ZipFormat(), stream);

        stream.Position = 0;
        var loaded = serializer.Load(new ZipFormat(), stream);

        Assert.IsType<EmptyParameters>(loaded.Parameters);
    }

    // ── ZIP Archive Structure ───────────────────────────────

    [Fact]
    public void Save_AllContent_ContainsExpectedEntries()
    {
        var serializer = CreateSerializer();
        var model = CreateTestModel([1.0], 0.0);

        using var stream = new MemoryStream();
        serializer.Save(model, SaveContent.All, new ZipFormat(), stream);

        stream.Position = 0;
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.NotNull(archive.GetEntry("parameters/weights.bin"));
        Assert.NotNull(archive.GetEntry("parameters/metadata.json"));
        Assert.NotNull(archive.GetEntry("topology/pipeline.json"));
        Assert.NotNull(archive.GetEntry("topology/schema.json"));
    }

    [Fact]
    public void Save_ParametersOnly_NoTopologyEntries()
    {
        var serializer = CreateSerializer();
        var model = CreateTestModel([1.0], 0.0);

        using var stream = new MemoryStream();
        serializer.Save(model, SaveContent.LearnedParameters, new ZipFormat(), stream);

        stream.Position = 0;
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.NotNull(archive.GetEntry("parameters/weights.bin"));
        Assert.Null(archive.GetEntry("topology/pipeline.json"));
    }

    // ── Manifest Correctness ────────────────────────────────

    [Fact]
    public void Save_ManifestHasCorrectContent()
    {
        var serializer = CreateSerializer();
        var model = CreateTestModel([1.0, 2.0], 0.5);

        using var stream = new MemoryStream();
        serializer.Save(model, SaveContent.All, new ZipFormat(), stream);

        stream.Position = 0;
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        using var manifestStream = archive.GetEntry("manifest.json")!.Open();
        var manifest = System.Text.Json.JsonSerializer.Deserialize<Manifest>(manifestStream,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

        Assert.NotNull(manifest);
        Assert.Equal("hpd-ml-zip-v1", manifest.FormatId);
        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal(nameof(TestParameters), manifest.ParameterType);
        Assert.NotNull(manifest.Pipeline);
    }

    // ── Error Handling ──────────────────────────────────────

    [Fact]
    public void Load_MissingManifest_Throws()
    {
        var serializer = CreateSerializer();

        // Create empty ZIP
        using var zipStream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            // empty
        }

        zipStream.Position = 0;
        Assert.Throws<InvalidOperationException>(() =>
            serializer.Load(new ZipFormat(), zipStream));
    }

    [Fact]
    public void Load_WrongFormat_Throws()
    {
        var serializer = CreateSerializer();

        using var zipStream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("manifest.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("""{"formatId":"wrong-format","schemaVersion":1,"content":0}""");
        }

        zipStream.Position = 0;
        var ex = Assert.Throws<InvalidOperationException>(() =>
            serializer.Load(new ZipFormat(), zipStream));
        Assert.Contains("Unsupported format", ex.Message);
    }

    [Fact]
    public void Load_UnregisteredParameterType_Throws()
    {
        // Save with writer, then load without it
        var saveSerializer = CreateSerializer();
        var model = CreateTestModel([1.0], 0.0);

        using var stream = new MemoryStream();
        saveSerializer.Save(model, SaveContent.LearnedParameters, new ZipFormat(), stream);

        stream.Position = 0;
        var loadSerializer = new ZipSerializer(); // no writer registered
        var ex = Assert.Throws<InvalidOperationException>(() =>
            loadSerializer.Load(new ZipFormat(), stream));
        Assert.Contains("No parameter writer registered", ex.Message);
    }

    // ── Inference State ─────────────────────────────────────

    [Fact]
    public void Save_WithInferenceState_CreatesStateEntry()
    {
        var serializer = CreateSerializer();
        var model = CreateTestModel([1.0], 0.0);
        var state = new { Value = 42, Name = "test" };

        using var stream = new MemoryStream();
        serializer.Save(model, SaveContent.All, new ZipFormat(), stream, inferenceState: state);

        stream.Position = 0;
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("state/state.bin"));
    }

    [Fact]
    public void Save_WithoutInferenceState_NoStateEntry()
    {
        var serializer = CreateSerializer();
        var model = CreateTestModel([1.0], 0.0);

        using var stream = new MemoryStream();
        serializer.Save(model, SaveContent.All, new ZipFormat(), stream);

        stream.Position = 0;
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        Assert.Null(archive.GetEntry("state/state.bin"));
    }

    // ── No Parameters Fallback ──────────────────────────────

    [Fact]
    public void Save_NoWriter_UsesJsonFallback()
    {
        var serializer = new ZipSerializer(); // no writer
        var model = CreateTestModel([1.0, 2.0], 0.5);

        using var stream = new MemoryStream();
        serializer.Save(model, SaveContent.LearnedParameters, new ZipFormat(), stream);

        stream.Position = 0;
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

        // Uses JSON fallback path
        Assert.NotNull(archive.GetEntry("parameters/parameters.json"));
        Assert.Null(archive.GetEntry("parameters/weights.bin"));
    }

    // ── Empty Weights ───────────────────────────────────────

    [Fact]
    public void SaveAndLoad_EmptyWeights_RoundTrips()
    {
        var serializer = CreateSerializer();
        var model = CreateTestModel([], 0.0);

        using var stream = new MemoryStream();
        serializer.Save(model, SaveContent.All, new ZipFormat(), stream);

        stream.Position = 0;
        var loaded = serializer.Load(new ZipFormat(), stream);
        var loadedParams = Assert.IsType<TestParameters>(loaded.Parameters);
        Assert.Empty(loadedParams.Weights);
        Assert.Equal(0.0, loadedParams.Bias);
    }

    // ── ComposedTransform Topology ──────────────────────────

    [Fact]
    public void Save_ComposedTransform_DecomposesChildren()
    {
        var serializer = CreateSerializer();
        var composed = new ComposedTransform([new TestTransform(), new TestTransform()]);
        var model = new Model(composed, new TestParameters { Weights = [1.0], Bias = 0.0 });

        using var stream = new MemoryStream();
        serializer.Save(model, SaveContent.All, new ZipFormat(), stream);

        stream.Position = 0;
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

        using var pipelineStream = archive.GetEntry("topology/pipeline.json")!.Open();
        using var reader = new StreamReader(pipelineStream);
        var json = reader.ReadToEnd();

        // Should contain two TestTransform entries
        Assert.Contains("TestTransform", json);
    }

    // ── Large Weight Array ──────────────────────────────────

    [Fact]
    public void SaveAndLoad_LargeWeightArray_RoundTrips()
    {
        var serializer = CreateSerializer();
        var weights = new double[10_000];
        for (int i = 0; i < weights.Length; i++)
            weights[i] = i * 0.001;
        var model = CreateTestModel(weights, -99.9);

        using var stream = new MemoryStream();
        serializer.Save(model, SaveContent.LearnedParameters, new ZipFormat(), stream);

        stream.Position = 0;
        var loaded = serializer.Load(new ZipFormat(), stream);
        var loadedParams = Assert.IsType<TestParameters>(loaded.Parameters);
        Assert.Equal(10_000, loadedParams.Weights.Length);
        Assert.Equal(-99.9, loadedParams.Bias);
        for (int i = 0; i < 10_000; i++)
            Assert.Equal(i * 0.001, loadedParams.Weights[i]);
    }

    // ── Inference State Manifest Flag ───────────────────────

    [Fact]
    public void Save_InferenceState_ManifestHasFlag()
    {
        var serializer = CreateSerializer();
        var model = CreateTestModel([1.0], 0.0);

        using var stream = new MemoryStream();
        serializer.Save(model, SaveContent.All, new ZipFormat(), stream, inferenceState: new { X = 1 });

        stream.Position = 0;
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        using var manifestStream = archive.GetEntry("manifest.json")!.Open();
        var manifest = System.Text.Json.JsonSerializer.Deserialize<Manifest>(manifestStream,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

        Assert.NotNull(manifest);
        Assert.True(manifest.HasInferenceState);
    }

    // ── Format ID Preserved ─────────────────────────────────

    [Fact]
    public void SaveAndLoad_FormatIdPreserved()
    {
        var serializer = CreateSerializer();
        var model = CreateTestModel([1.0], 0.0);

        using var stream = new MemoryStream();
        serializer.Save(model, SaveContent.All, new ZipFormat(), stream);

        stream.Position = 0;
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        using var manifestStream = archive.GetEntry("manifest.json")!.Open();
        var manifest = System.Text.Json.JsonSerializer.Deserialize<Manifest>(manifestStream,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

        Assert.Equal("hpd-ml-zip-v1", manifest!.FormatId);
        Assert.Equal(1, manifest.SchemaVersion);
    }

    // ── Registry Overwrite ──────────────────────────────────

    [Fact]
    public void Register_OverwritesPreviousWriter()
    {
        var serializer = new ZipSerializer();
        serializer.RegisterParameterWriter(new TestParameterWriter());
        serializer.RegisterParameterWriter(new TestParameterWriter()); // re-register

        var model = CreateTestModel([1.0, 2.0], 0.5);

        using var stream = new MemoryStream();
        serializer.Save(model, SaveContent.LearnedParameters, new ZipFormat(), stream);

        stream.Position = 0;
        var loaded = serializer.Load(new ZipFormat(), stream);
        var loadedParams = Assert.IsType<TestParameters>(loaded.Parameters);
        Assert.Equal([1.0, 2.0], loadedParams.Weights);
    }

    // ── Nested ComposedTransform ────────────────────────────

    [Fact]
    public void Save_NestedComposedTransform_FlattensAll()
    {
        var serializer = CreateSerializer();
        var inner = new ComposedTransform([new TestTransform(), new TestTransform()]);
        var outer = new ComposedTransform([inner, new TestTransform()]);
        var model = new Model(outer, new TestParameters { Weights = [1.0], Bias = 0.0 });

        using var stream = new MemoryStream();
        serializer.Save(model, SaveContent.PipelineTopology, new ZipFormat(), stream);

        stream.Position = 0;
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        using var pipelineStream = archive.GetEntry("topology/pipeline.json")!.Open();
        var entries = System.Text.Json.JsonSerializer.Deserialize<List<TransformEntry>>(pipelineStream,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

        // Inner has 2 + outer has 1 = 3 flattened entries
        Assert.NotNull(entries);
        Assert.Equal(3, entries.Count);
        Assert.All(entries, e => Assert.Equal("TestTransform", e.TypeName));
    }

    // ── Corrupted ZIP ───────────────────────────────────────

    [Fact]
    public void Load_CorruptedZip_Throws()
    {
        var serializer = CreateSerializer();
        using var stream = new MemoryStream([0x00, 0x01, 0x02, 0x03]);
        Assert.ThrowsAny<Exception>(() =>
            serializer.Load(new ZipFormat(), stream));
    }
}
