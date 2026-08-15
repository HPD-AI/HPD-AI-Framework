using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Audio.Primitives;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityPcm16WindowAssemblerV1Tests
{
    [Fact]
    public void Stereo_is_downmixed_and_resampled_into_an_exact_mono_window()
    {
        var assembler = Assembler(Format(48_000, 2), 16_000);
        var bytes = new byte[480 * 2 * sizeof(short)];
        for (var sample = 0; sample < 480; sample++)
        {
            Write(bytes, sample * 2, 3_000);
            Write(bytes, sample * 2 + 1, 1_000);
        }

        var window = Assert.Single(Produced(assembler.Process(bytes, Format(48_000, 2), 480,
            AudioRecoveryKind.None, AudioFrameFlags.None, Range(10, bytes.Length))).Windows);

        Assert.Equal(320, window.Bytes.Length);
        Assert.Equal(2_000, Read(window.Bytes.Span, 0));
        Assert.Equal(10, window.Extent.StartInclusive);
        Assert.Equal(11, window.Extent.EndExclusive);
        Assert.True(window.Extent.Exact);
        Assert.Equal(0, assembler.RetainedSamples);
    }

    [Fact]
    public void Nonintegral_rate_accounting_has_no_cumulative_drift()
    {
        var assembler = Assembler(Format(44_100, 1), 16_000, maximumBatchSize: 4);
        var bytes = new byte[100 * sizeof(short)];
        var windows = 0;
        for (ulong frame = 0; frame < 441; frame++)
            windows += Produced(assembler.Process(bytes, Format(44_100, 1), 100,
                AudioRecoveryKind.None, AudioFrameFlags.None, Range(frame, bytes.Length))).Windows.Count;

        Assert.Equal(100, windows);
        Assert.Equal(0, assembler.RetainedSamples);
    }

    [Fact]
    public void Partial_input_is_bounded_and_extent_spans_every_contributing_frame()
    {
        var assembler = Assembler(Format(16_000, 1), 16_000);
        var half = new byte[80 * sizeof(short)];
        Assert.Empty(Produced(assembler.Process(half, Format(16_000, 1), 80,
            AudioRecoveryKind.None, AudioFrameFlags.None, Range(20, half.Length))).Windows);
        Assert.Equal(80, assembler.RetainedSamples);

        var window = Assert.Single(Produced(assembler.Process(half, Format(16_000, 1), 80,
            AudioRecoveryKind.None, AudioFrameFlags.None, Range(21, half.Length))).Windows);
        Assert.Equal(20, window.Extent.StartInclusive);
        Assert.Equal(22, window.Extent.EndExclusive);
        Assert.Equal(0, assembler.RetainedSamples);
    }

    [Fact]
    public void Forked_candidate_isolated_until_the_caller_adopts_it()
    {
        var assembler = Assembler(Format(16_000, 1), 16_000);
        var candidate = assembler.Fork();
        var half = new byte[80 * sizeof(short)];

        Assert.Empty(Produced(candidate.Process(half, Format(16_000, 1), 80,
            AudioRecoveryKind.None, AudioFrameFlags.None, Range(25, half.Length))).Windows);

        Assert.Equal(0, assembler.RetainedSamples);
        Assert.Equal(80, candidate.RetainedSamples);
        Assert.Empty(Produced(assembler.Process(half, Format(16_000, 1), 80,
            AudioRecoveryKind.None, AudioFrameFlags.None, Range(25, half.Length))).Windows);
        Assert.Equal(80, assembler.RetainedSamples);
    }

    [Fact]
    public void Recovery_marks_extent_inexact_and_discontinuity_releases_partial_state()
    {
        var assembler = Assembler(Format(16_000, 1), 16_000);
        var half = new byte[80 * sizeof(short)];
        Assert.Empty(Produced(assembler.Process(half, Format(16_000, 1), 80,
            AudioRecoveryKind.PacketLossConcealment, AudioFrameFlags.None, Range(30, half.Length))).Windows);
        var recovered = Assert.Single(Produced(assembler.Process(half, Format(16_000, 1), 80,
            AudioRecoveryKind.None, AudioFrameFlags.None, Range(31, half.Length))).Windows);
        Assert.False(recovered.Extent.Exact);

        Assert.Empty(Produced(assembler.Process(half, Format(16_000, 1), 80,
            AudioRecoveryKind.None, AudioFrameFlags.None, Range(32, half.Length))).Windows);
        Assert.Equal(VoiceActivityInputInvalidReasonV1.DiscontinuousWindow,
            Assert.IsType<VoiceActivityWindowAssemblyResultV1.Rejected>(assembler.Process(
                half, Format(16_000, 1), 80, AudioRecoveryKind.None, AudioFrameFlags.Discontinuity,
                Range(33, half.Length))).Reason);
        Assert.Equal(0, assembler.RetainedSamples);
        Assert.Empty(Produced(assembler.Process(half, Format(16_000, 1), 80,
            AudioRecoveryKind.None, AudioFrameFlags.None, Range(100, half.Length))).Windows);
    }

    [Fact]
    public void Scope_change_and_batch_overflow_fail_before_state_mutation()
    {
        var assembler = Assembler(Format(16_000, 1), 16_000, maximumBatchSize: 1);
        var tooLarge = new byte[320 * sizeof(short)];
        Assert.Equal(VoiceActivityInputInvalidReasonV1.ExtentInvalid,
            Assert.IsType<VoiceActivityWindowAssemblyResultV1.Rejected>(assembler.Process(
                tooLarge, Format(16_000, 1), 320, AudioRecoveryKind.None, AudioFrameFlags.None,
                Range(40, tooLarge.Length))).Reason);
        Assert.Equal(0, assembler.RetainedSamples);

        var half = new byte[80 * sizeof(short)];
        Assert.Empty(Produced(assembler.Process(half, Format(16_000, 1), 80,
            AudioRecoveryKind.None, AudioFrameFlags.None, Range(40, half.Length))).Windows);
        Assert.Equal(VoiceActivityInputInvalidReasonV1.MixedGeneration,
            Assert.IsType<VoiceActivityWindowAssemblyResultV1.Rejected>(assembler.Process(
                half, Format(16_000, 1), 80, AudioRecoveryKind.None, AudioFrameFlags.None,
                Range(42, half.Length))).Reason);
        Assert.Equal(0, assembler.RetainedSamples);
    }

    private static VoiceActivityPcm16WindowAssemblerV1 Assembler(
        AudioFormat input, int outputRate, int maximumBatchSize = 16) =>
        new(input, new VoiceActivityInputFormatV1(
                VoiceActivitySampleEncodingV1.SignedPcm16, outputRate, 1),
            TimeSpan.FromMilliseconds(10), maximumBatchSize);

    private static VoiceActivityWindowAssemblyResultV1.Produced Produced(
        VoiceActivityWindowAssemblyResultV1 result) =>
        Assert.IsType<VoiceActivityWindowAssemblyResultV1.Produced>(result);

    private static AudioFormat Format(int rate, int channels) => new()
    {
        SampleRate = rate, ChannelCount = channels, SampleFormat = AudioSampleFormat.Pcm16,
    };

    private static GraphMediaRangeV1 Range(ulong start, int bytes) => new(
        Session, Graph, GraphDirectionV1.IngressForward, GraphTrafficDomainV1.Media,
        new GraphFramePositionV1(start), 1, (ulong)bytes, new DurationNs(10_000_000));

    private static void Write(Span<byte> bytes, int sample, short value)
    {
        bytes[sample * 2] = (byte)value;
        bytes[sample * 2 + 1] = (byte)(value >> 8);
    }

    private static short Read(ReadOnlySpan<byte> bytes, int sample) =>
        (short)(bytes[sample * 2] | bytes[sample * 2 + 1] << 8);

    private static readonly SessionAuthorityStampV1 Session =
        new(RuntimeGenerationId.Create(), LiveSessionId.Create());
    private static readonly GraphGenerationId Graph = GraphGenerationId.Create();
}
