using System.Buffers.Binary;
using System.Text.Json;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Audio.Silero;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class SileroAudioProviderV1Tests
{
    private static readonly ClockDomainId Clock = ClockDomainId.Create();
    private static readonly BootId Boot = BootId.Create();

    [Fact]
    public void Artifact_and_provider_metadata_are_exact_and_do_not_claim_implicit_download()
    {
        Assert.Equal("6.2", SileroModelArtifactV1.Version);
        Assert.Equal(64, SileroModelArtifactV1.OfficialSha256.Length);
        using var provider = new SileroAudioProvider();
        var family = provider.GetMetadata().Families[ProviderClientFamily.VoiceActivityDetection];
        Assert.Equal(ProviderFamilyLifetime.StatefulPerAudioSession, family.Lifetime);
        Assert.Equal(false, family.Capabilities!["ImplicitModelDownload"]);
        Assert.Equal(new[] { 8_000, 16_000 }, family.Capabilities["InputSampleRates"]);
        Assert.Equal("1.23.0", family.Capabilities["OnnxRuntimeVersion"]);
        Assert.Equal(1, family.Capabilities["MaximumConcurrentInferences"]);
        Assert.Equal(0, family.Capabilities["MaximumPendingInferences"]);
        Assert.Equal(new[] { "linux-arm64", "linux-x64", "osx-arm64", "osx-x64", "win-arm64", "win-x64" },
            family.Capabilities["SupportedRuntimeIdentifiers"]);
        Assert.Equal(new Dictionary<string, string>
        {
            ["linux-arm64"] = "libonnxruntime.so",
            ["linux-x64"] = "libonnxruntime.so",
            ["osx-arm64"] = "libonnxruntime.dylib",
            ["osx-x64"] = "libonnxruntime.dylib",
            ["win-arm64"] = "onnxruntime.dll",
            ["win-x64"] = "onnxruntime.dll",
        }, SileroModelArtifactV1.NativeRuntimeAssets);
    }

    [Fact]
    public void Generated_manifest_and_source_generated_configuration_are_complete()
    {
        var fragment = HPD.Agent.Providers.Generated
            .HPD_Agent_Providers_Audio_Silero_SileroAudioProviderProviderManifest.Fragment;
        var composition = ProviderComposition.Create([fragment]);
        Assert.True(composition.Descriptors.TryGet(SileroAudioProvider.Key, out var descriptor));
        Assert.Equal(ProviderFamilyLifetime.StatefulPerAudioSession,
            descriptor!.Families[ProviderClientFamily.VoiceActivityDetection].Lifetime);
        Assert.IsType<SileroAudioProvider>(composition.Runtime.GetFactory(SileroAudioProvider.Key,
            ProviderClientFamily.VoiceActivityDetection).Factory());

        var options = new SileroVadOptions { ModelPath = "/models/silero.onnx", IntraOpThreads = 2 };
        var json = JsonSerializer.Serialize(options, SileroJsonContext.Default.SileroVadOptions);
        var decoded = JsonSerializer.Deserialize(json, SileroJsonContext.Default.SileroVadOptions)!;
        Assert.Equal(options.ModelPath, decoded.ModelPath);
        Assert.Equal(options.ModelSha256, decoded.ModelSha256);
        Assert.Equal(2, decoded.IntraOpThreads);
    }

    [Fact]
    public void Configuration_rejects_missing_invalid_and_untrusted_artifacts_before_source_creation()
    {
        using var provider = new SileroAudioProvider();
        var missing = Config(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.onnx"));
        Assert.False(provider.ValidateConfiguration(missing, ProviderClientFamily.VoiceActivityDetection).IsValid);
        var wrongFamily = provider.ValidateConfiguration(Config(ModelPath()), ProviderClientFamily.Chat);
        Assert.False(wrongFamily.IsValid);
        var badDigest = Config(ModelPath(), new string('0', 64));
        Assert.Throws<InvalidDataException>(() => provider.CreateVoiceActivitySource(badDigest, Context()));
    }

    [Theory]
    [InlineData(8000, 256)]
    [InlineData(16000, 512)]
    public void Official_model_executes_exact_streaming_windows_and_rejects_wrong_geometry(int sampleRate, int samples)
    {
        using var provider = new SileroAudioProvider();
        var product = Assert.IsType<VoiceActivitySourceProductV1.BorrowedSynchronous>(
            provider.CreateVoiceActivitySource(Config(ModelPath()), Context()));
        var source = product.Source;
        var observed = Assert.IsType<VoiceActivitySourceOutcomeV1.Observed>(
            source.Observe(Window(new byte[samples * 2], sampleRate, 1)));
        var score = Assert.IsType<VoiceActivityMeasurementV1.Numeric>(observed.Measurement).Value;
        Assert.InRange(score, 0, 1);
        Assert.Equal((ulong)1, observed.Sequence);
        Assert.IsType<VoiceActivitySourceOutcomeV1.InvalidInput>(
            source.Observe(Window(new byte[(samples * 2) - 2], sampleRate, 2)));
        (source as IDisposable)?.Dispose();
    }

    [Fact]
    public void Streams_share_the_model_host_but_keep_recurrent_state_and_sequence_isolated()
    {
        using var provider = new SileroAudioProvider();
        var first = Source(provider);
        var second = Source(provider);
        var audio = Pcm16Sine(512, 220, 16_000, 0.25);
        var firstOne = Assert.IsType<VoiceActivitySourceOutcomeV1.Observed>(first.Observe(Window(audio, 16_000, 1)));
        var firstTwo = Assert.IsType<VoiceActivitySourceOutcomeV1.Observed>(first.Observe(Window(audio, 16_000, 2)));
        var secondOne = Assert.IsType<VoiceActivitySourceOutcomeV1.Observed>(second.Observe(Window(audio, 16_000, 1)));
        Assert.Equal((ulong)1, firstOne.Sequence);
        Assert.Equal((ulong)2, firstTwo.Sequence);
        Assert.Equal((ulong)1, secondOne.Sequence);
        Assert.Equal(Score(firstOne), Score(secondOne), precision: 6);
        (first as IDisposable)?.Dispose();
        (second as IDisposable)?.Dispose();
    }

    [Fact]
    public void Provider_disposal_defers_model_teardown_until_existing_session_sources_release_their_leases()
    {
        var provider = new SileroAudioProvider();
        var source = Source(provider);
        provider.Dispose();
        Assert.IsType<VoiceActivitySourceOutcomeV1.Observed>(source.Observe(Window(new byte[1_024], 16_000, 1)));
        Assert.Throws<ObjectDisposedException>(() => provider.CreateVoiceActivitySource(Config(ModelPath()), Context()));
        (source as IDisposable)?.Dispose();
        Assert.IsType<VoiceActivitySourceOutcomeV1.Unavailable>(source.Observe(Window(new byte[1_024], 16_000, 2)));
    }

    [Fact]
    public void Generic_registry_resolves_the_generated_voice_activity_family_without_a_second_registry()
    {
        using var provider = new SileroAudioProvider();
        var registry = new ProviderRegistry();
        registry.Register(provider);
        var resolved = registry.ResolveRequiredFamily<IVoiceActivitySourceProviderV1>(Config(ModelPath()),
            ProviderClientFamily.VoiceActivityDetection, ProviderFamilyLifetime.StatefulPerAudioSession);
        Assert.Same(provider, resolved.Provider);
        var source = Assert.IsType<VoiceActivitySourceProductV1.BorrowedSynchronous>(
            VoiceActivitySourceProviderBindingV1.Create(resolved, Context())).Source;
        Assert.Equal(VoiceActivitySourceStateModelV1.StreamLocal, source.Capabilities.StateModel);
        (source as IDisposable)?.Dispose();
    }

    [Fact]
    public void Pinned_upstream_corpus_contains_both_confident_speech_and_non_speech_windows()
    {
        using var provider = new SileroAudioProvider();
        var source = Source(provider);
        var pcm = ReadPcm16MonoWav(CorpusPath());
        var scores = new List<double>();
        for (var offset = 0; offset + 1_024 <= pcm.Length; offset += 1_024)
        {
            var window = pcm.AsSpan(offset, 1_024).ToArray();
            var observed = Assert.IsType<VoiceActivitySourceOutcomeV1.Observed>(
                source.Observe(Window(window, 16_000, (ulong)(scores.Count + 1))));
            scores.Add(Score(observed));
        }
        Assert.True(scores.Count > 100);
        Assert.Contains(scores, static score => score >= 0.75);
        Assert.Contains(scores, static score => score <= 0.05);
        Assert.True(scores.Count(static score => score >= 0.5) >= 10);
        Assert.True(scores.Count(static score => score < 0.5) >= 10);
        (source as IDisposable)?.Dispose();
    }

    [Fact]
    public void Pinned_corpus_downsampled_to_telephony_rate_preserves_speech_and_silence_separation()
    {
        using var provider = new SileroAudioProvider();
        var source = Source(provider);
        var pcm = DownsamplePcm16ByTwo(ReadPcm16MonoWav(CorpusPath()));
        var scores = new List<double>();
        for (var offset = 0; offset + 512 <= pcm.Length; offset += 512)
        {
            var observed = Assert.IsType<VoiceActivitySourceOutcomeV1.Observed>(source.Observe(
                Window(pcm.AsSpan(offset, 512).ToArray(), 8_000, (ulong)(scores.Count + 1))));
            scores.Add(Score(observed));
        }
        Assert.True(scores.Count > 100);
        Assert.Contains(scores, static score => score >= 0.65);
        Assert.Contains(scores, static score => score <= 0.1);
        Assert.True(scores.Count(static score => score >= 0.5) >= 10);
        Assert.True(scores.Count(static score => score < 0.5) >= 10);
        (source as IDisposable)?.Dispose();
    }

    [Fact]
    public void Sustained_streaming_is_bounded_and_does_not_retain_window_buffers()
    {
        using var provider = new SileroAudioProvider();
        var source = Source(provider);
        var bytes = new byte[1_024];
        _ = source.Observe(Window(bytes, 16_000, 1));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong sequence = 2; sequence <= 1_001; sequence++)
            Assert.IsType<VoiceActivitySourceOutcomeV1.Observed>(source.Observe(Window(bytes, 16_000, sequence)));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 0, 64L * 1_000 * 1_024);
        (source as IDisposable)?.Dispose();
        Assert.IsType<VoiceActivitySourceOutcomeV1.Unavailable>(source.Observe(Window(bytes, 16_000, 1_002)));
    }

    private static IBorrowedSynchronousVoiceActivitySourceV1 Source(SileroAudioProvider provider) =>
        Assert.IsType<VoiceActivitySourceProductV1.BorrowedSynchronous>(
            provider.CreateVoiceActivitySource(Config(ModelPath()), Context())).Source;

    private static ProviderClientConfig Config(string path, string? digest = null) => new()
    {
        ProviderKey = SileroAudioProvider.Key,
        ModelName = "silero-vad-6.2",
        ProviderConfig = new SileroVadOptions
        {
            ModelPath = path,
            ModelSha256 = digest ?? SileroModelArtifactV1.OfficialSha256,
            IntraOpThreads = 1,
        }
    };

    private static ProviderComponentLifetimeContext Context() => new(
        AudioSessionId: Guid.NewGuid().ToString("N"),
        Lifetime: ProviderFamilyLifetime.StatefulPerAudioSession);

    private static VoiceActivityBorrowedWindowV1 Window(byte[] bytes, int sampleRate, ulong sequence) => new(
        bytes, new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, sampleRate, 1),
        new VoiceActivityMediaExtentV1(GraphGenerationId.Create(), (long)sequence * 1_000,
            ((long)sequence * 1_000) + bytes.Length, true),
        new MonotonicStampV1(Clock, Boot, sequence));

    private static byte[] Pcm16Sine(int samples, double frequency, int sampleRate, double amplitude)
    {
        var bytes = new byte[samples * 2];
        for (var index = 0; index < samples; index++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * frequency * index / sampleRate) * short.MaxValue * amplitude);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * 2, 2), value);
        }
        return bytes;
    }

    private static byte[] DownsamplePcm16ByTwo(byte[] input)
    {
        var output = new byte[input.Length / 2];
        for (var source = 0; source + 3 < input.Length; source += 4)
        {
            var destination = source / 2;
            output[destination] = input[source];
            output[destination + 1] = input[source + 1];
        }
        return output;
    }

    private static double Score(VoiceActivitySourceOutcomeV1.Observed observed) =>
        Assert.IsType<VoiceActivityMeasurementV1.Numeric>(observed.Measurement).Value;

    private static string ModelPath()
    {
        var configured = System.Environment.GetEnvironmentVariable("HPD_SILERO_VAD_MODEL_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "artifacts", "silero-vad", "v6.2", "silero_vad.onnx");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new InvalidOperationException("Run eng/fetch-silero-vad-v6.2.sh or set HPD_SILERO_VAD_MODEL_PATH.");
    }

    private static string CorpusPath() => Path.Combine(Path.GetDirectoryName(ModelPath())!, "test.wav");

    private static byte[] ReadPcm16MonoWav(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        Assert.Equal("RIFF", new string(reader.ReadChars(4)));
        _ = reader.ReadUInt32();
        Assert.Equal("WAVE", new string(reader.ReadChars(4)));
        ushort channels = 0;
        uint sampleRate = 0;
        ushort bitsPerSample = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var length = reader.ReadUInt32();
            if (id == "fmt ")
            {
                Assert.Equal((ushort)1, reader.ReadUInt16());
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();
                stream.Position += length - 16;
            }
            else if (id == "data")
            {
                Assert.Equal((ushort)1, channels);
                Assert.Equal((uint)16_000, sampleRate);
                Assert.Equal((ushort)16, bitsPerSample);
                return reader.ReadBytes(checked((int)length));
            }
            else stream.Position += length;
            if ((length & 1) != 0) stream.Position++;
        }
        throw new InvalidDataException("The pinned Silero corpus has no PCM data chunk.");
    }
}
