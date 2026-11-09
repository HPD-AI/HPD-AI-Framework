using HPD.Agent.Authority;

namespace HPD.Agent.Audio.ProviderContracts.VoiceActivity;

public enum VoiceActivityMeasurementKind : ushort
{
    CalibratedLikelihood = 1,
    EngineScore = 2,
    BinaryDecision = 3,
    PostProcessedState = 4,
    FeatureEvidence = 5,
    ProviderOpaqueCategory = 6,
}

public enum VoiceActivityNoObservationReason : ushort
{
    Gap = 1,
    Timeout = 2,
    Cancelled = 3,
    Reset = 4,
    Teardown = 5,
    ProviderNotObservable = 6,
    SourceRevoked = 7,
}

public enum VoiceActivityInputInvalidReason : ushort
{
    FormatMismatch = 1,
    MixedGeneration = 2,
    DiscontinuousWindow = 3,
    ExtentInvalid = 4,
}

public enum VoiceActivitySourceUnavailableReason : ushort
{
    ArtifactMissing = 1,
    ModelUnavailable = 2,
    ProviderUnavailable = 3,
    CapacityUnavailable = 4,
    DeadlineReached = 5,
}

public enum VoiceActivitySourceFaultClass : ushort
{
    ContractViolation = 1,
    InferenceFailure = 2,
    NativeFailure = 3,
    ProviderFailure = 4,
    OwnershipAmbiguous = 5,
}

public enum VoiceActivityRetryability : ushort
{
    Never = 1,
    SameGeneration = 2,
    AfterReplacement = 3,
}

public enum VoiceActivityStateValidity : ushort
{
    Preserved = 1,
    ResetRequired = 2,
    Quarantined = 3,
}

public sealed record VoiceActivityMeasurementDescriptor
{
    public VoiceActivityMeasurementDescriptor(
        VoiceActivityMeasurementKind kind,
        BoundedAscii units,
        double minimum,
        double maximum,
        Hash256? calibrationIdentity)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!units.IsValid) throw new ArgumentException("Measurement units are required.", nameof(units));
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum >= maximum)
            throw new ArgumentException("A finite increasing measurement range is required.");
        if (kind == VoiceActivityMeasurementKind.CalibratedLikelihood &&
            (!calibrationIdentity.HasValue || !IsValid(calibrationIdentity.Value)))
            throw new ArgumentException("Calibrated likelihood requires a calibration identity.", nameof(calibrationIdentity));
        if (kind != VoiceActivityMeasurementKind.CalibratedLikelihood && calibrationIdentity.HasValue)
            throw new ArgumentException("Only calibrated likelihood carries a calibration identity.", nameof(calibrationIdentity));
        Kind = kind;
        Units = units;
        Minimum = minimum;
        Maximum = maximum;
        CalibrationIdentity = calibrationIdentity;
    }

    public VoiceActivityMeasurementKind Kind { get; }
    public BoundedAscii Units { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public Hash256? CalibrationIdentity { get; }

    private static bool IsValid(Hash256 value)
    {
        Span<byte> bytes = stackalloc byte[32];
        return value.TryWriteBytes(bytes);
    }
}

public abstract record VoiceActivityMeasurement
{
    private VoiceActivityMeasurement() { }

    public sealed record Numeric : VoiceActivityMeasurement
    {
        public Numeric(double value)
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        public double Value { get; }
    }

    public sealed record Binary(bool Value) : VoiceActivityMeasurement;

    public sealed record Category : VoiceActivityMeasurement
    {
        public Category(BoundedAscii value)
        {
            if (!value.IsValid) throw new ArgumentException("A category is required.", nameof(value));
            Value = value;
        }

        public BoundedAscii Value { get; }
    }
}

public readonly record struct VoiceActivityMediaExtent
{
    public VoiceActivityMediaExtent(
        GraphGenerationId graphGeneration,
        long startInclusive,
        long endExclusive,
        bool exact)
    {
        if (!graphGeneration.IsValid) throw new ArgumentException("A graph generation is required.", nameof(graphGeneration));
        if (startInclusive < 0 || endExclusive <= startInclusive)
            throw new ArgumentOutOfRangeException(nameof(endExclusive), "The media extent must be positive and nonempty.");
        GraphGeneration = graphGeneration;
        StartInclusive = startInclusive;
        EndExclusive = endExclusive;
        Exact = exact;
    }

    public GraphGenerationId GraphGeneration { get; }
    public long StartInclusive { get; }
    public long EndExclusive { get; }
    public bool Exact { get; }
}

public abstract record VoiceActivitySourceOutcome
{
    private VoiceActivitySourceOutcome() { }

    public sealed record Observed : VoiceActivitySourceOutcome
    {
        public Observed(
            VoiceActivityMeasurement measurement,
            VoiceActivityMeasurementDescriptor descriptor,
            VoiceActivityMediaExtent extent,
            ulong sequence,
            MonotonicStampV1 observedAt,
            MonotonicStampV1 processedAt)
        {
            ArgumentNullException.ThrowIfNull(measurement);
            ArgumentNullException.ThrowIfNull(descriptor);
            if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (!observedAt.IsValid || processedAt.CompareTo(observedAt) == ClockComparison.Incomparable ||
                processedAt.CompareTo(observedAt) == ClockComparison.Earlier)
                throw new ArgumentException("Observation timing must be comparable and nondecreasing.");
            ValidateMeasurement(measurement, descriptor);
            Measurement = measurement;
            Descriptor = descriptor;
            Extent = extent;
            Sequence = sequence;
            ObservedAt = observedAt;
            ProcessedAt = processedAt;
        }

        public VoiceActivityMeasurement Measurement { get; }
        public VoiceActivityMeasurementDescriptor Descriptor { get; }
        public VoiceActivityMediaExtent Extent { get; }
        public ulong Sequence { get; }
        public MonotonicStampV1 ObservedAt { get; }
        public MonotonicStampV1 ProcessedAt { get; }
    }

    public sealed record NoObservation : VoiceActivitySourceOutcome
    {
        public NoObservation(VoiceActivityNoObservationReason reason)
        {
            if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
            Reason = reason;
        }
        public VoiceActivityNoObservationReason Reason { get; }
    }

    public sealed record InvalidInput : VoiceActivitySourceOutcome
    {
        public InvalidInput(VoiceActivityInputInvalidReason reason)
        {
            if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
            Reason = reason;
        }
        public VoiceActivityInputInvalidReason Reason { get; }
    }

    public sealed record Unavailable : VoiceActivitySourceOutcome
    {
        public Unavailable(VoiceActivitySourceUnavailableReason reason, VoiceActivityRetryability retryability)
        {
            if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
            if (!Enum.IsDefined(retryability)) throw new ArgumentOutOfRangeException(nameof(retryability));
            Reason = reason;
            Retryability = retryability;
        }
        public VoiceActivitySourceUnavailableReason Reason { get; }
        public VoiceActivityRetryability Retryability { get; }
    }

    public sealed record Fault : VoiceActivitySourceOutcome
    {
        public Fault(VoiceActivitySourceFaultClass classification,
            VoiceActivityStateValidity stateValidity,
            VoiceActivityRetryability retryability)
        {
            if (!Enum.IsDefined(classification)) throw new ArgumentOutOfRangeException(nameof(classification));
            if (!Enum.IsDefined(stateValidity)) throw new ArgumentOutOfRangeException(nameof(stateValidity));
            if (!Enum.IsDefined(retryability)) throw new ArgumentOutOfRangeException(nameof(retryability));
            Classification = classification;
            StateValidity = stateValidity;
            Retryability = retryability;
        }
        public VoiceActivitySourceFaultClass Classification { get; }
        public VoiceActivityStateValidity StateValidity { get; }
        public VoiceActivityRetryability Retryability { get; }
    }

    private static void ValidateMeasurement(
        VoiceActivityMeasurement measurement,
        VoiceActivityMeasurementDescriptor descriptor)
    {
        switch (descriptor.Kind, measurement)
        {
            case (VoiceActivityMeasurementKind.CalibratedLikelihood or
                VoiceActivityMeasurementKind.EngineScore or
                VoiceActivityMeasurementKind.FeatureEvidence,
                VoiceActivityMeasurement.Numeric numeric)
                when numeric.Value >= descriptor.Minimum && numeric.Value <= descriptor.Maximum:
            case (VoiceActivityMeasurementKind.BinaryDecision, VoiceActivityMeasurement.Binary):
            case (VoiceActivityMeasurementKind.PostProcessedState or
                VoiceActivityMeasurementKind.ProviderOpaqueCategory,
                VoiceActivityMeasurement.Category):
                return;
            default:
                throw new ArgumentException("The measurement does not match its descriptor.", nameof(measurement));
        }
    }
}
