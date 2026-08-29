using HPD.Base;

namespace HPD.Auth.Base.ConsumerProof;

internal static class LifecycleProof
{
    internal sealed record Observation(BaseSubjectAcknowledgementResult Acknowledgement,
        BaseSubjectLifecycleCheckpointResult Checkpoint);
    internal const string DeliveryGrant = "proof.lifecycle.read";
    internal const string AcknowledgementGrant = "proof.retirement.ack";
    internal const string ConsumerId = "proof.lifecycle.consumer.v1";
    private static readonly System.Collections.Concurrent.ConcurrentQueue<Observation> Observations = new();
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> Errors = new();

    internal static void Observe(BaseSubjectAcknowledgementResult acknowledgement,
        BaseSubjectLifecycleCheckpointResult checkpoint) =>
        Observations.Enqueue(new Observation(acknowledgement, checkpoint));

    internal static Observation[] Drain()
    {
        var values = new List<Observation>();
        while (Observations.TryDequeue(out var value)) values.Add(value);
        return [.. values];
    }
    internal static void ObserveError(string value) => Errors.Enqueue(value);
    internal static string[] DrainErrors()
    {
        var values = new List<string>();
        while (Errors.TryDequeue(out string? value)) values.Add(value);
        return [.. values];
    }

    internal static BaseGeneratedSubjectLifecycleConsumerIdentity<ConsumerSubject> LifecycleIdentity { get; } =
        BaseGeneratedSubjectLifecycleConsumers.Register<ConsumerSubject>(
            ConsumerSubject.HPDBaseSubjectRegistration,
            ConsumerId,
            1,
            "proof.module",
            BaseSubjectLifecycleConsumerAudience.Service,
            [
                BaseSubjectLifecycleState.Active,
                BaseSubjectLifecycleState.Inactive,
                BaseSubjectLifecycleState.Tombstoned,
                BaseSubjectLifecycleState.Retired,
            ],
            DeliveryGrant,
            null,
            new BaseSubjectLifecycleConsumerLimits
            {
                MaximumFactsPerPage = 16,
                MaximumResultBytes = 65_536,
                MaximumCheckpointLag = TimeSpan.FromDays(1),
                ReadTimeout = TimeSpan.FromSeconds(5),
            });

    internal static BaseGeneratedSubjectRetirementConsumerIdentity<ConsumerSubject> RetirementIdentity { get; } =
        BaseGeneratedSubjectRetirementConsumers.RegisterRequired(
            LifecycleIdentity,
            "proof.module",
            BaseSubjectLifecycleConsumerAudience.Service,
            "proof.retirement.profile.v1",
            1,
            new string('a', 64),
            AcknowledgementGrant,
            new BaseSubjectRetirementConsumerLimits
            {
                MaximumAcknowledgementsPerCommit = 16,
                MaximumAcknowledgementRequestBytes = 65_536,
                MaximumReceiptBytes = 65_536,
                AcknowledgementTimeout = TimeSpan.FromSeconds(5),
                ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
            });

    internal static BaseGeneratedSubjectRetirementPolicyIdentity<ConsumerSubject> Policy { get; } =
        BaseGeneratedSubjectRetirementPolicies.Register<ConsumerSubject>(
            ConsumerSubject.HPDBaseSubjectRegistration,
            TimeSpan.FromHours(1),
            BaseSubjectRetirementTimeoutBehavior.Quarantine,
            new BaseSubjectPurgeRetentionPolicy { MinimumTombstoneAge = TimeSpan.Zero },
            BaseSubjectFinalExecutionMode.OrdinaryOrActivationGuarded,
            RetirementIdentity);
}
