using System.Collections.ObjectModel;

namespace HPD.Agent.Audio.VoiceActivity;

internal enum VoiceActivityInputOwnershipV1 : ushort
{
    BorrowedSynchronous = 1,
    IsolatedTransferred = 2,
    ProviderOpaque = 3,
}

internal enum VoiceActivitySourceStateModelV1 : ushort
{
    Stateless = 1,
    GenerationLocal = 2,
    StreamLocal = 3,
    ProviderOpaque = 4,
}

internal enum VoiceActivitySourceConcurrencyV1 : ushort
{
    Serial = 1,
    ParallelWindows = 2,
    ProviderManaged = 3,
}

internal enum VoiceActivitySourceControlV1 : ushort
{
    Unsupported = 1,
    Sequenced = 2,
    ReplacementRequired = 3,
}

internal enum VoiceActivitySampleEncodingV1 : ushort
{
    SignedPcm16 = 1,
    Float32 = 2,
    ProviderOpaque = 3,
}

internal sealed record VoiceActivityInputFormatV1
{
    internal VoiceActivityInputFormatV1(
        VoiceActivitySampleEncodingV1 encoding,
        int sampleRate,
        int channels)
    {
        if (!Enum.IsDefined(encoding)) throw new ArgumentOutOfRangeException(nameof(encoding));
        if (encoding == VoiceActivitySampleEncodingV1.ProviderOpaque)
        {
            if (sampleRate != 0 || channels != 0)
                throw new ArgumentException("Opaque input cannot claim decoded audio geometry.");
        }
        else
        {
            if (sampleRate is < 8_000 or > 192_000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(channels));
        }

        Encoding = encoding;
        SampleRate = sampleRate;
        Channels = channels;
    }

    internal VoiceActivitySampleEncodingV1 Encoding { get; }
    internal int SampleRate { get; }
    internal int Channels { get; }
}

internal sealed record VoiceActivityWindowCapabilityV1
{
    internal VoiceActivityWindowCapabilityV1(
        TimeSpan minimumWindow,
        TimeSpan maximumWindow,
        TimeSpan stride,
        int maximumBatchSize)
    {
        if (minimumWindow <= TimeSpan.Zero || maximumWindow < minimumWindow || maximumWindow > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(maximumWindow));
        if (stride <= TimeSpan.Zero || stride > maximumWindow) throw new ArgumentOutOfRangeException(nameof(stride));
        if (maximumBatchSize is < 1 or > 4_096) throw new ArgumentOutOfRangeException(nameof(maximumBatchSize));
        MinimumWindow = minimumWindow;
        MaximumWindow = maximumWindow;
        Stride = stride;
        MaximumBatchSize = maximumBatchSize;
    }

    internal TimeSpan MinimumWindow { get; }
    internal TimeSpan MaximumWindow { get; }
    internal TimeSpan Stride { get; }
    internal int MaximumBatchSize { get; }
}

internal sealed record VoiceActivitySourceCapabilitiesV1
{
    private readonly VoiceActivityInputFormatV1[] _formats;

    internal VoiceActivitySourceCapabilitiesV1(
        VoiceActivityInputOwnershipV1 inputOwnership,
        IReadOnlyList<VoiceActivityInputFormatV1> formats,
        VoiceActivityWindowCapabilityV1 window,
        VoiceActivityMeasurementDescriptorV1 measurement,
        VoiceActivitySourceStateModelV1 stateModel,
        VoiceActivitySourceConcurrencyV1 concurrency,
        VoiceActivitySourceControlV1 dynamicUpdate,
        VoiceActivitySourceControlV1 reset,
        VoiceActivitySourceControlV1 transfer,
        VoiceActivitySourceControlV1 replacement,
        bool supportsCancellation,
        bool supportsWarmup,
        int maximumPendingOperations)
    {
        if (!Enum.IsDefined(inputOwnership)) throw new ArgumentOutOfRangeException(nameof(inputOwnership));
        ArgumentNullException.ThrowIfNull(formats);
        _formats = formats.ToArray();
        if (_formats.Length is 0 or > 32 || _formats.Any(static format => format is null))
            throw new ArgumentOutOfRangeException(nameof(formats));
        if (_formats.Distinct().Count() != _formats.Length)
            throw new ArgumentException("Input formats must be unique.", nameof(formats));
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(measurement);
        if (!Enum.IsDefined(stateModel)) throw new ArgumentOutOfRangeException(nameof(stateModel));
        if (!Enum.IsDefined(concurrency)) throw new ArgumentOutOfRangeException(nameof(concurrency));
        if (!Enum.IsDefined(dynamicUpdate)) throw new ArgumentOutOfRangeException(nameof(dynamicUpdate));
        if (!Enum.IsDefined(reset)) throw new ArgumentOutOfRangeException(nameof(reset));
        if (!Enum.IsDefined(transfer)) throw new ArgumentOutOfRangeException(nameof(transfer));
        if (!Enum.IsDefined(replacement)) throw new ArgumentOutOfRangeException(nameof(replacement));
        if (maximumPendingOperations is < 1 or > 65_536) throw new ArgumentOutOfRangeException(nameof(maximumPendingOperations));

        var hasOpaqueFormat = _formats.Any(static format => format.Encoding == VoiceActivitySampleEncodingV1.ProviderOpaque);
        if ((inputOwnership == VoiceActivityInputOwnershipV1.ProviderOpaque) != hasOpaqueFormat ||
            hasOpaqueFormat && _formats.Length != 1)
            throw new ArgumentException("Opaque ownership requires exactly one opaque format; decoded ownership forbids it.", nameof(formats));
        if (inputOwnership == VoiceActivityInputOwnershipV1.BorrowedSynchronous &&
            (maximumPendingOperations != 1 || concurrency != VoiceActivitySourceConcurrencyV1.Serial ||
             transfer != VoiceActivitySourceControlV1.Unsupported))
            throw new ArgumentException("Borrowed synchronous input is serial, receipt-free and nontransferring.");
        if (inputOwnership != VoiceActivityInputOwnershipV1.BorrowedSynchronous &&
            transfer == VoiceActivitySourceControlV1.Unsupported)
            throw new ArgumentException("Outliving or opaque work must declare sequenced transfer or replacement.", nameof(transfer));

        InputOwnership = inputOwnership;
        Formats = new ReadOnlyCollection<VoiceActivityInputFormatV1>(_formats);
        Window = window;
        Measurement = measurement;
        StateModel = stateModel;
        Concurrency = concurrency;
        DynamicUpdate = dynamicUpdate;
        Reset = reset;
        Transfer = transfer;
        Replacement = replacement;
        SupportsCancellation = supportsCancellation;
        SupportsWarmup = supportsWarmup;
        MaximumPendingOperations = maximumPendingOperations;
    }

    internal VoiceActivityInputOwnershipV1 InputOwnership { get; }
    internal IReadOnlyList<VoiceActivityInputFormatV1> Formats { get; }
    internal VoiceActivityWindowCapabilityV1 Window { get; }
    internal VoiceActivityMeasurementDescriptorV1 Measurement { get; }
    internal VoiceActivitySourceStateModelV1 StateModel { get; }
    internal VoiceActivitySourceConcurrencyV1 Concurrency { get; }
    internal VoiceActivitySourceControlV1 DynamicUpdate { get; }
    internal VoiceActivitySourceControlV1 Reset { get; }
    internal VoiceActivitySourceControlV1 Transfer { get; }
    internal VoiceActivitySourceControlV1 Replacement { get; }
    internal bool SupportsCancellation { get; }
    internal bool SupportsWarmup { get; }
    internal int MaximumPendingOperations { get; }
}
