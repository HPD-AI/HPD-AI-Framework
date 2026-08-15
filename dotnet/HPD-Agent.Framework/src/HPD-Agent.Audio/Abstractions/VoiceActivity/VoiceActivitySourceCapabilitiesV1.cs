using System.Collections.ObjectModel;

namespace HPD.Agent.Audio.ProviderContracts.VoiceActivity;

public enum VoiceActivityInputOwnershipV1 : ushort
{
    BorrowedSynchronous = 1,
    IsolatedTransferred = 2,
    ProviderOpaque = 3,
}

public enum VoiceActivitySourceStateModelV1 : ushort
{
    Stateless = 1,
    GenerationLocal = 2,
    StreamLocal = 3,
    ProviderOpaque = 4,
}

public enum VoiceActivitySourceConcurrencyV1 : ushort
{
    Serial = 1,
    ParallelWindows = 2,
    ProviderManaged = 3,
}

public enum VoiceActivitySourceControlV1 : ushort
{
    Unsupported = 1,
    Sequenced = 2,
    ReplacementRequired = 3,
}

public enum VoiceActivitySampleEncodingV1 : ushort
{
    SignedPcm16 = 1,
    Float32 = 2,
    ProviderOpaque = 3,
}

public sealed record VoiceActivityInputFormatV1
{
    public VoiceActivityInputFormatV1(
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

    public VoiceActivitySampleEncodingV1 Encoding { get; }
    public int SampleRate { get; }
    public int Channels { get; }
}

public sealed record VoiceActivityWindowCapabilityV1
{
    public VoiceActivityWindowCapabilityV1(
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

    public TimeSpan MinimumWindow { get; }
    public TimeSpan MaximumWindow { get; }
    public TimeSpan Stride { get; }
    public int MaximumBatchSize { get; }
}

public sealed record VoiceActivitySourceCapabilitiesV1
{
    private readonly VoiceActivityInputFormatV1[] _formats;

    public VoiceActivitySourceCapabilitiesV1(
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

    public VoiceActivityInputOwnershipV1 InputOwnership { get; }
    public IReadOnlyList<VoiceActivityInputFormatV1> Formats { get; }
    public VoiceActivityWindowCapabilityV1 Window { get; }
    public VoiceActivityMeasurementDescriptorV1 Measurement { get; }
    public VoiceActivitySourceStateModelV1 StateModel { get; }
    public VoiceActivitySourceConcurrencyV1 Concurrency { get; }
    public VoiceActivitySourceControlV1 DynamicUpdate { get; }
    public VoiceActivitySourceControlV1 Reset { get; }
    public VoiceActivitySourceControlV1 Transfer { get; }
    public VoiceActivitySourceControlV1 Replacement { get; }
    public bool SupportsCancellation { get; }
    public bool SupportsWarmup { get; }
    public int MaximumPendingOperations { get; }
}
