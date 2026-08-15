using HPD.Agent.Authority;

namespace HPD.Agent.Audio.ProviderContracts.VoiceActivity;

public enum VoiceActivityMeasurementKindV1 : ushort
{
    CalibratedLikelihood = 1,
    EngineScore = 2,
    BinaryDecision = 3,
    PostProcessedState = 4,
    FeatureEvidence = 5,
    ProviderOpaqueCategory = 6,
}

public enum VoiceActivityNoObservationReasonV1 : ushort
{
    Gap = 1,
    Timeout = 2,
    Cancelled = 3,
    Reset = 4,
    Teardown = 5,
    ProviderNotObservable = 6,
    SourceRevoked = 7,
}

public enum VoiceActivityInputInvalidReasonV1 : ushort
{
    FormatMismatch = 1,
    MixedGeneration = 2,
    DiscontinuousWindow = 3,
    ExtentInvalid = 4,
}

public enum VoiceActivitySourceUnavailableReasonV1 : ushort
{
    ArtifactMissing = 1,
    ModelUnavailable = 2,
    ProviderUnavailable = 3,
    CapacityUnavailable = 4,
    DeadlineReached = 5,
}

public enum VoiceActivitySourceFaultClassV1 : ushort
{
    ContractViolation = 1,
    InferenceFailure = 2,
    NativeFailure = 3,
    ProviderFailure = 4,
    OwnershipAmbiguous = 5,
}

public enum VoiceActivityRetryabilityV1 : ushort
{
    Never = 1,
    SameGeneration = 2,
    AfterReplacement = 3,
}

public enum VoiceActivityStateValidityV1 : ushort
{
    Preserved = 1,
    ResetRequired = 2,
    Quarantined = 3,
}

public sealed record VoiceActivityMeasurementDescriptorV1
{
    public VoiceActivityMeasurementDescriptorV1(
        VoiceActivityMeasurementKindV1 kind,
        BoundedAscii units,
        double minimum,
        double maximum,
        Hash256? calibrationIdentity)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!units.IsValid) throw new ArgumentException("Measurement units are required.", nameof(units));
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum >= maximum)
            throw new ArgumentException("A finite increasing measurement range is required.");
        if (kind == VoiceActivityMeasurementKindV1.CalibratedLikelihood &&
            (!calibrationIdentity.HasValue || !IsValid(calibrationIdentity.Value)))
            throw new ArgumentException("Calibrated likelihood requires a calibration identity.", nameof(calibrationIdentity));
        if (kind != VoiceActivityMeasurementKindV1.CalibratedLikelihood && calibrationIdentity.HasValue)
            throw new ArgumentException("Only calibrated likelihood carries a calibration identity.", nameof(calibrationIdentity));
        Kind = kind;
        Units = units;
        Minimum = minimum;
        Maximum = maximum;
        CalibrationIdentity = calibrationIdentity;
    }

    public VoiceActivityMeasurementKindV1 Kind { get; }
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

public abstract record VoiceActivityMeasurementV1
{
    private VoiceActivityMeasurementV1() { }

    public sealed record Numeric : VoiceActivityMeasurementV1
    {
        public Numeric(double value)
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        public double Value { get; }
    }

    public sealed record Binary(bool Value) : VoiceActivityMeasurementV1;

    public sealed record Category : VoiceActivityMeasurementV1
    {
        public Category(BoundedAscii value)
        {
            if (!value.IsValid) throw new ArgumentException("A category is required.", nameof(value));
            Value = value;
        }

        public BoundedAscii Value { get; }
    }
}

public readonly record struct VoiceActivityMediaExtentV1
{
    public VoiceActivityMediaExtentV1(
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

public abstract record VoiceActivitySourceOutcomeV1
{
    private VoiceActivitySourceOutcomeV1() { }

    public sealed record Observed : VoiceActivitySourceOutcomeV1
    {
        public Observed(
            VoiceActivityMeasurementV1 measurement,
            VoiceActivityMeasurementDescriptorV1 descriptor,
            VoiceActivityMediaExtentV1 extent,
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

        public VoiceActivityMeasurementV1 Measurement { get; }
        public VoiceActivityMeasurementDescriptorV1 Descriptor { get; }
        public VoiceActivityMediaExtentV1 Extent { get; }
        public ulong Sequence { get; }
        public MonotonicStampV1 ObservedAt { get; }
        public MonotonicStampV1 ProcessedAt { get; }
    }

    public sealed record NoObservation : VoiceActivitySourceOutcomeV1
    {
        public NoObservation(VoiceActivityNoObservationReasonV1 reason)
        {
            if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
            Reason = reason;
        }
        public VoiceActivityNoObservationReasonV1 Reason { get; }
    }

    public sealed record InvalidInput : VoiceActivitySourceOutcomeV1
    {
        public InvalidInput(VoiceActivityInputInvalidReasonV1 reason)
        {
            if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
            Reason = reason;
        }
        public VoiceActivityInputInvalidReasonV1 Reason { get; }
    }

    public sealed record Unavailable : VoiceActivitySourceOutcomeV1
    {
        public Unavailable(VoiceActivitySourceUnavailableReasonV1 reason, VoiceActivityRetryabilityV1 retryability)
        {
            if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
            if (!Enum.IsDefined(retryability)) throw new ArgumentOutOfRangeException(nameof(retryability));
            Reason = reason;
            Retryability = retryability;
        }
        public VoiceActivitySourceUnavailableReasonV1 Reason { get; }
        public VoiceActivityRetryabilityV1 Retryability { get; }
    }

    public sealed record Fault : VoiceActivitySourceOutcomeV1
    {
        public Fault(VoiceActivitySourceFaultClassV1 classification,
            VoiceActivityStateValidityV1 stateValidity,
            VoiceActivityRetryabilityV1 retryability)
        {
            if (!Enum.IsDefined(classification)) throw new ArgumentOutOfRangeException(nameof(classification));
            if (!Enum.IsDefined(stateValidity)) throw new ArgumentOutOfRangeException(nameof(stateValidity));
            if (!Enum.IsDefined(retryability)) throw new ArgumentOutOfRangeException(nameof(retryability));
            Classification = classification;
            StateValidity = stateValidity;
            Retryability = retryability;
        }
        public VoiceActivitySourceFaultClassV1 Classification { get; }
        public VoiceActivityStateValidityV1 StateValidity { get; }
        public VoiceActivityRetryabilityV1 Retryability { get; }
    }

    private static void ValidateMeasurement(
        VoiceActivityMeasurementV1 measurement,
        VoiceActivityMeasurementDescriptorV1 descriptor)
    {
        switch (descriptor.Kind, measurement)
        {
            case (VoiceActivityMeasurementKindV1.CalibratedLikelihood or
                VoiceActivityMeasurementKindV1.EngineScore or
                VoiceActivityMeasurementKindV1.FeatureEvidence,
                VoiceActivityMeasurementV1.Numeric numeric)
                when numeric.Value >= descriptor.Minimum && numeric.Value <= descriptor.Maximum:
            case (VoiceActivityMeasurementKindV1.BinaryDecision, VoiceActivityMeasurementV1.Binary):
            case (VoiceActivityMeasurementKindV1.PostProcessedState or
                VoiceActivityMeasurementKindV1.ProviderOpaqueCategory,
                VoiceActivityMeasurementV1.Category):
                return;
            default:
                throw new ArgumentException("The measurement does not match its descriptor.", nameof(measurement));
        }
    }
}
