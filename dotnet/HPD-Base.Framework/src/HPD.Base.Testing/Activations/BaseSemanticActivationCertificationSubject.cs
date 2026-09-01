using System.Collections.Immutable;

namespace HPD.Base.Testing;

internal sealed class BaseSemanticActivationCertificationLifecycleSubject;

internal static class BaseSemanticActivationCertificationSubjectAuthority
{
    internal const string CollectionId = "certification.subjects";
    internal const string ContractId = "certification.subject";
    internal static ImmutableArray<byte> ScopeBindingId { get; } =
        System.Security.Cryptography.SHA256.HashData(
            "certification-scope-binding"u8).ToImmutableArray();

    internal static CollectionDefinition Collection { get; } = new()
    {
        Id = CollectionId,
        Name = CollectionId,
        Kind = BaseCollectionKinds.Document,
        System = true,
        Exposed = false,
        SystemOwnerModuleId = "certification",
        SchemaMode = SchemaMode.Strict,
        UnknownFields = UnknownFieldPolicy.Reject,
        MutationMode = BaseCollectionMutationMode.Mutable,
        Fields =
        [
            new FieldDefinition
            {
                Id = "certification.subject.active", ApplicationName = "active", WireName = "active",
                Type = BaseFieldTypes.Boolean, Presence = BaseFieldPresence.Required,
                Nullability = BaseFieldNullability.NonNullable,
            },
            new FieldDefinition
            {
                Id = "certification.subject.tombstoned", ApplicationName = "tombstoned", WireName = "tombstoned",
                Type = BaseFieldTypes.Boolean, Presence = BaseFieldPresence.Required,
                Nullability = BaseFieldNullability.NonNullable,
            },
            new FieldDefinition
            {
                Id = "certification.subject.deletedAt", ApplicationName = "deletedAt", WireName = "deletedAt",
                Type = BaseFieldTypes.DateTime, Presence = BaseFieldPresence.Optional,
                Nullability = BaseFieldNullability.NonNullable,
            },
            new FieldDefinition
            {
                Id = "certification.subject.tombstoneSequence", ApplicationName = "tombstoneSequence",
                WireName = "tombstoneSequence", Type = BaseFieldTypes.Integer,
                Presence = BaseFieldPresence.Required, Nullability = BaseFieldNullability.NonNullable,
                ScalarKind = BaseScalarKind.Int64,
                ScalarConstraints = new BaseScalarConstraintSet { MinimumInt64 = 0 },
            },
        ],
    };

#pragma warning disable HPDBASE0461 // The provider certification graph is trusted framework-owned generated-equivalent authority.
    internal static BaseGeneratedSubjectRegistration Registration { get; } =
        BaseGeneratedSubjects.Register<BaseSemanticActivationCertificationLifecycleSubject>(new()
        {
            Id = ContractId,
            Version = 1,
            OwningModuleId = "certification",
            SubjectIdKind = BaseSubjectIdKind.OrdinalString,
            MaximumSubjectIdUtf8Bytes = 64,
            Scope = BaseSubjectScopeKind.Global,
            AcquisitionGrantId = "certification.subject.acquire",
            ValidationGrantId = "certification.subject.validate",
            AdministrationGrantId = "certification.subject.admin",
            TombstoneFieldId = "certification.subject.tombstoned",
            TombstoneMetadata = new BaseSubjectTombstoneMetadataDefinition
            {
                Instant = new BaseSubjectTombstoneInstantBinding
                {
                    Kind = BaseSubjectTombstoneMetadataBindingKind.RequiredField,
                    FieldId = "certification.subject.deletedAt",
                },
                Sequence = new BaseSubjectTombstoneSequenceBinding
                {
                    Kind = BaseSubjectTombstoneMetadataBindingKind.RequiredField,
                    FieldId = "certification.subject.tombstoneSequence",
                },
            },
            FinalRetirementExecutionMode = BaseSubjectFinalExecutionMode.OrdinaryOrActivationGuarded,
            SupportsCoordinatedRetirement = true,
            Audiences = [HPDBaseEndpointAudience.Application],
            ValidationPlan = new BaseSubjectValidationPlanDefinition
            {
                Id = "certification.subject.validate.v1",
                Version = 1,
                ContractId = ContractId,
                ContractVersion = 1,
                ContractChecksum = new string('0', 64),
                PrivateCollectionId = CollectionId,
                SubjectId = BaseSubjectIdBinding.RecordId,
                Active = new BaseSubjectActiveBinding
                {
                    Kind = BaseSubjectActiveBindingKind.RequiredBooleanField,
                    FieldId = "certification.subject.active",
                    ActiveValue = true,
                },
                Scope = new BaseSubjectScopeBinding { Kind = BaseSubjectScopeBindingKind.Global },
                Access = BaseSubjectValidationAccessShape.ContractAndSubjectPrimaryKeys,
                Limits = BaseSubjectValidationLimits.Default,
            },
        });

    internal static BaseGeneratedSubjectLifecycleConsumerIdentity<BaseSemanticActivationCertificationLifecycleSubject>
        Lifecycle { get; } = BaseGeneratedSubjectLifecycleConsumers.Register<BaseSemanticActivationCertificationLifecycleSubject>(
            Registration,
            "certification.subject.lifecycle",
            1,
            "certification",
            BaseSubjectLifecycleConsumerAudience.Service,
            [BaseSubjectLifecycleState.Tombstoned],
            "certification.subject.lifecycle.read",
            null,
            new BaseSubjectLifecycleConsumerLimits
            {
                MaximumFactsPerPage = 16,
                MaximumResultBytes = 65_536,
                MaximumCheckpointLag = TimeSpan.FromDays(1),
                ReadTimeout = TimeSpan.FromSeconds(5),
            });

    internal static BaseGeneratedSubjectRetirementConsumerIdentity<BaseSemanticActivationCertificationLifecycleSubject>
        Retirement { get; } = BaseGeneratedSubjectRetirementConsumers.RegisterRequired(
            Lifecycle,
            "certification",
            BaseSubjectLifecycleConsumerAudience.Service,
            "certification.subject.retirement",
            1,
            new string('a', 64),
            "certification.subject.retirement.acknowledge",
            new BaseSubjectRetirementConsumerLimits
            {
                MaximumAcknowledgementsPerCommit = 16,
                MaximumAcknowledgementRequestBytes = 65_536,
                MaximumReceiptBytes = 65_536,
                AcknowledgementTimeout = TimeSpan.FromSeconds(5),
                ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
            });

    internal static BaseGeneratedSubjectRetirementPolicyIdentity<BaseSemanticActivationCertificationLifecycleSubject>
        RetirementPolicy { get; } = BaseGeneratedSubjectRetirementPolicies.Register(
            Registration,
            TimeSpan.FromMinutes(1),
            BaseSubjectRetirementTimeoutBehavior.Quarantine,
            new BaseSubjectPurgeRetentionPolicy { MinimumTombstoneAge = TimeSpan.Zero },
            BaseSubjectFinalExecutionMode.OrdinaryOrActivationGuarded,
            Retirement);

    internal static BaseSemanticActivationSubjectLifetimeBinding Bind(
        BaseSubjectReference<BaseSemanticActivationCertificationLifecycleSubject> subject)
    {
        var value = new BaseSemanticActivationSubjectLifetimeBinding
        {
            ContractId = ContractId,
            ContractVersion = 1,
            ContractChecksum = Convert.FromHexString(Registration.Checksum).ToImmutableArray(),
            SubjectId = subject.SubjectId,
            AuthorityEpoch = subject.AuthorityEpoch,
            Incarnation = subject.Incarnation,
            ScopeBindingId = ScopeBindingId,
            Checksum = [],
        };
        return value with
        {
            Checksum = BaseSemanticActivationEvidenceContract.SubjectLifetimeChecksum(value),
        };
    }
#pragma warning restore HPDBASE0461
}
