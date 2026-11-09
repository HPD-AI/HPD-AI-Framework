using System.Collections.ObjectModel;

namespace HPD.Agent.Audio.ProviderContracts.VoiceActivity;

public enum VoiceActivityInputOwnership : ushort
{
    BorrowedSynchronous = 1,
    IsolatedTransferred = 2,
    ProviderOpaque = 3,
}

public enum VoiceActivitySourceStateModel : ushort
{
    Stateless = 1,
    GenerationLocal = 2,
    StreamLocal = 3,
    ProviderOpaque = 4,
}

public enum VoiceActivitySourceConcurrency : ushort
{
    Serial = 1,
    ParallelWindows = 2,
    ProviderManaged = 3,
}

public enum VoiceActivitySourceControl : ushort
{
    Unsupported = 1,
    Sequenced = 2,
    ReplacementRequired = 3,
}

public enum VoiceActivitySampleEncoding : ushort
{
    SignedPcm16 = 1,
    Float32 = 2,
    ProviderOpaque = 3,
}

public sealed record VoiceActivityInputFormat
{
    public VoiceActivityInputFormat(
        VoiceActivitySampleEncoding encoding,
        int sampleRate,
        int channels)
    {
        if (!Enum.IsDefined(encoding)) throw new ArgumentOutOfRangeException(nameof(encoding));
        if (encoding == VoiceActivitySampleEncoding.ProviderOpaque)
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

    public VoiceActivitySampleEncoding Encoding { get; }
    public int SampleRate { get; }
    public int Channels { get; }
}

public sealed record VoiceActivityWindowCapability
{
    public VoiceActivityWindowCapability(
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

public sealed record VoiceActivitySourceCapabilities
{
    private readonly VoiceActivityInputFormat[] _formats;

    public VoiceActivitySourceCapabilities(
        VoiceActivityInputOwnership inputOwnership,
        IReadOnlyList<VoiceActivityInputFormat> formats,
        VoiceActivityWindowCapability window,
        VoiceActivityMeasurementDescriptor measurement,
        VoiceActivitySourceStateModel stateModel,
        VoiceActivitySourceConcurrency concurrency,
        VoiceActivitySourceControl dynamicUpdate,
        VoiceActivitySourceControl reset,
        VoiceActivitySourceControl transfer,
        VoiceActivitySourceControl replacement,
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

        var hasOpaqueFormat = _formats.Any(static format => format.Encoding == VoiceActivitySampleEncoding.ProviderOpaque);
        if ((inputOwnership == VoiceActivityInputOwnership.ProviderOpaque) != hasOpaqueFormat ||
            hasOpaqueFormat && _formats.Length != 1)
            throw new ArgumentException("Opaque ownership requires exactly one opaque format; decoded ownership forbids it.", nameof(formats));
        if (inputOwnership == VoiceActivityInputOwnership.BorrowedSynchronous &&
            (maximumPendingOperations != 1 || concurrency != VoiceActivitySourceConcurrency.Serial ||
             transfer != VoiceActivitySourceControl.Unsupported))
            throw new ArgumentException("Borrowed synchronous input is serial, receipt-free and nontransferring.");
        if (inputOwnership != VoiceActivityInputOwnership.BorrowedSynchronous &&
            transfer == VoiceActivitySourceControl.Unsupported)
            throw new ArgumentException("Outliving or opaque work must declare sequenced transfer or replacement.", nameof(transfer));

        InputOwnership = inputOwnership;
        Formats = new ReadOnlyCollection<VoiceActivityInputFormat>(_formats);
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

    public VoiceActivityInputOwnership InputOwnership { get; }
    public IReadOnlyList<VoiceActivityInputFormat> Formats { get; }
    public VoiceActivityWindowCapability Window { get; }
    public VoiceActivityMeasurementDescriptor Measurement { get; }
    public VoiceActivitySourceStateModel StateModel { get; }
    public VoiceActivitySourceConcurrency Concurrency { get; }
    public VoiceActivitySourceControl DynamicUpdate { get; }
    public VoiceActivitySourceControl Reset { get; }
    public VoiceActivitySourceControl Transfer { get; }
    public VoiceActivitySourceControl Replacement { get; }
    public bool SupportsCancellation { get; }
    public bool SupportsWarmup { get; }
    public int MaximumPendingOperations { get; }
}
