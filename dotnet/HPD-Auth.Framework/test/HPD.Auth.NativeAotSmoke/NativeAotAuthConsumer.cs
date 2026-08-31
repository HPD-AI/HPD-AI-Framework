using System.Text.Json.Serialization;
using HPD.Auth.Base;
using HPD.Base;

namespace HPD.Auth.NativeAotSmoke;

/// <summary>Owns the external proving module that references an exported Auth user.</summary>
internal static class NativeAotAuthConsumer
{
    internal const string ModuleId = "hpd.auth.native-aot.consumer";
    internal const string ConsumerId = "hpd.auth.native-aot.consumer.users.v1";
    internal const string DeliveryGrantId = "hpd.auth.native-aot.consumer.users.delivery";
    internal const string AcknowledgementGrantId = "hpd.auth.native-aot.consumer.users.acknowledge";

    internal static BaseGeneratedSubjectLifecycleConsumerIdentity<AuthUserSubject> LifecycleIdentity { get; } =
        BaseGeneratedSubjectLifecycleConsumers.Register<AuthUserSubject>(
            AuthUserSubject.HPDBaseSubjectRegistration,
            ConsumerId,
            1,
            ModuleId,
            BaseSubjectLifecycleConsumerAudience.Service,
            [
                BaseSubjectLifecycleState.Active,
                BaseSubjectLifecycleState.Inactive,
                BaseSubjectLifecycleState.Tombstoned,
                BaseSubjectLifecycleState.Retired,
            ],
            DeliveryGrantId,
            null,
            new BaseSubjectLifecycleConsumerLimits
            {
                MaximumFactsPerPage = 16,
                MaximumResultBytes = 65_536,
                MaximumCheckpointLag = TimeSpan.FromDays(30),
                ReadTimeout = TimeSpan.FromSeconds(5),
            });

    internal static BaseGeneratedSubjectRetirementConsumerIdentity<AuthUserSubject> RetirementIdentity { get; } =
        BaseGeneratedSubjectRetirementConsumers.RegisterRequired(
            LifecycleIdentity,
            ModuleId,
            BaseSubjectLifecycleConsumerAudience.Service,
            "hpd.auth.native-aot.consumer.users.retirement.v1",
            1,
            new string('a', 64),
            AcknowledgementGrantId,
            new BaseSubjectRetirementConsumerLimits
            {
                MaximumAcknowledgementsPerCommit = 16,
                MaximumAcknowledgementRequestBytes = 65_536,
                MaximumReceiptBytes = 65_536,
                AcknowledgementTimeout = TimeSpan.FromSeconds(5),
                ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
            });

    private static BaseGeneratedSubjectRetirementPolicyIdentity<AuthUserSubject> RetirementPolicy { get; } =
        BaseGeneratedSubjectRetirementPolicies.Register(
            AuthUserSubject.HPDBaseSubjectRegistration,
            TimeSpan.FromHours(24),
            BaseSubjectRetirementTimeoutBehavior.Quarantine,
            new BaseSubjectPurgeRetentionPolicy { MinimumTombstoneAge = TimeSpan.FromDays(30) },
            BaseSubjectFinalExecutionMode.OrdinaryOrActivationGuarded,
            RetirementIdentity);

    /// <summary>Installs the consumer-owned graph and Auth's exact bilateral acceptance.</summary>
    /// <param name="builder">The shared application graph builder.</param>
    internal static void Install(HPDBaseBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddCollection(NativeAotCustomerProfile.Collection);
        builder.AddSubjectLifecycleConsumer(LifecycleIdentity);
        builder.AddSubjectRetirementConsumer(RetirementIdentity);
        builder.AddSubjectRetirementPolicy(RetirementPolicy);
        AddGrant(builder, DeliveryGrantId, ConsumerId, 1);
        AddGrant(builder, AcknowledgementGrantId, ConsumerId, 1);
        AddGrant(builder, "base.subjectLifecycle.feed.read", "base.subjectLifecycle.feed.read", 2);
        AddGrant(builder, "base.subjectLifecycle.feed.checkpoint", "base.subjectLifecycle.feed.checkpoint", 2);
        AddGrant(builder, "base.subjectRetirement.acknowledge", "base.subjectRetirement.acknowledge", 2);
    }

    private static void AddGrant(HPDBaseBuilder builder, string grantId, string action, int version) =>
        builder.AddStaticGrantAuthority(
            new BaseGrantAuthorityDefinition
            {
                Id = grantId,
                Version = version,
                OwningModuleId = ModuleId,
                SourceContractId = "hpd.auth.native-aot.consumer.grants",
                SourceContractVersion = 1,
            },
            new AccessGrant
            {
                Id = grantId,
                ApplicationId = "hpd.auth.identity.v1",
                ModuleId = ModuleId,
                Audience = HPDBaseEndpointAudience.Application,
                Subject = new AccessSubject
                {
                    Kind = AccessSubjectKind.System,
                    Id = "hpd.auth",
                    TenantId = Guid.Empty.ToString("D"),
                },
                Action = action,
                Scope = new ResourceScope
                {
                    Kind = ResourceScopeKind.SubjectContract,
                    SubjectContractId = "hpd.auth.user-subject",
                    SubjectContractVersion = 1,
                    TenantId = Guid.Empty.ToString("D"),
                },
            });
}

/// <summary>Represents consumer-owned application state associated with an Auth user.</summary>
[BaseCollection("nativeAot.customerProfiles", typeof(NativeAotConsumerJsonContext))]
internal sealed partial record NativeAotCustomerProfile
{
    /// <summary>Gets the consumer-owned profile identity.</summary>
    [BaseField("nativeAot.customerProfiles.id")]
    public required Guid Id { get; init; }

    /// <summary>Gets the consumer-owned tenant identity.</summary>
    [BaseField("nativeAot.customerProfiles.tenantId")]
    public required Guid TenantId { get; init; }

    /// <summary>Gets the typed reference to the exported Auth user.</summary>
    [BaseField("nativeAot.customerProfiles.user")]
    [BaseSubjectReference(
        typeof(AuthUserSubject),
        Requirement = BaseSubjectReferenceRequirement.Active,
        Guarantee = BaseSubjectValidationGuarantee.TransactionSnapshot)]
    public required BaseSubjectReference<AuthUserSubject> User { get; init; }
}

/// <summary>Provides reflection-free JSON metadata for the external consumer record.</summary>
[JsonSerializable(typeof(NativeAotCustomerProfile))]
internal sealed partial class NativeAotConsumerJsonContext : JsonSerializerContext;
