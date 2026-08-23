using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace HPD.Base.Tests.Application.Activations;

public sealed class SemanticRecoveryAuthorityTests
{
    [Fact]
    public async Task Registry_requires_selection_and_exact_owned_instance_descriptor()
    {
        byte[] seed = Enumerable.Range(1, Ed25519.SecretKeySize).Select(static value => (byte)value).ToArray();
        BaseSemanticRecoveryAuthorityRegistration registration = Registration(seed);
        BaseSemanticActivationCapability capability = BaseSemanticActivationCapabilityContract.BuiltIn(durable: true);
        capability = capability with { RestoreModes = [BaseActivationRestoreMode.InPlaceRecovery, BaseActivationRestoreMode.NewDisasterDomain], Checksum = [] };
        capability = capability with { Checksum = BaseSemanticActivationCapabilityContract.Checksum(capability) };
        var identity = BaseMutationRequestIdentity.Create("semantic", "restore-selection", "selection-1",
            BaseMutationRequestFingerprint.Create(SHA256.HashData("selection"u8)));
        var selection = new BaseSemanticActivationRestoreSelection
        {
            LogicalStoreId = "module-store", EnabledRestoreMode = BaseActivationRestoreMode.NewDisasterDomain,
            SelectionGeneration = 1, Identity = identity, Checksum = [],
        };
        await using var registry = new BaseSemanticRecoveryAuthorityRegistry([selection], [registration], capability, 1,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero)));
        registry.Selections.Should().ContainKey("module-store");
        Action missing = () => _ = new BaseSemanticRecoveryAuthorityRegistry([], [], capability, 1, TimeProvider.System);
        missing.Should().Throw<InvalidOperationException>();

        BaseSemanticRecoveryAuthorityInstanceDescriptor hostile = ((Authority)registration.Factory.CreateOwned()).Descriptor with
        {
            ImplementationContractId = "substituted",
        };
        BaseSemanticRecoveryAuthorityRegistration substituted = registration with { Factory = new Factory(hostile) };
        Action mismatch = () => _ = new BaseSemanticRecoveryAuthorityRegistry([selection], [substituted], capability, 1,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero)));
        mismatch.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Registration_binds_complete_capability_key_and_certification_authority()
    {
        byte[] seed = Enumerable.Range(1, Ed25519.SecretKeySize).Select(static value => (byte)value).ToArray();
        BaseSemanticRecoveryAuthorityRegistration registration = Registration(seed);

        BaseSemanticRecoveryAuthorityContract.IsValid(registration).Should().BeTrue();
        BaseSemanticRecoveryAuthorityContract.IsValidAt(registration, new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        BaseSemanticRecoveryAuthorityContract.IsValid(registration with
        {
            Definition = registration.Definition with
            {
                KeyAuthority = registration.Definition.KeyAuthority with
                {
                    CurrentSigningPublicKey = Enumerable.Repeat((byte)0x55, Ed25519.PublicKeySize).ToImmutableArray(),
                },
            },
        }).Should().BeFalse();
        BaseSemanticRecoveryAuthorityContract.IsValid(Registration(seed, TimeSpan.FromDays(30) - TimeSpan.FromTicks(1))).Should().BeFalse();
        BaseSemanticRecoveryAuthorityContract.IsValidAt(registration, new DateTimeOffset(2110, 1, 1, 0, 0, 0, TimeSpan.Zero)).Should().BeFalse();
        BaseSemanticRecoveryAuthorityContract.IsValidAt(registration, new DateTimeOffset(2010, 1, 1, 0, 0, 0, TimeSpan.Zero)).Should().BeFalse();
        BaseSemanticRecoveryAuthorityRegistration exactRemaining = Registration(seed, TimeSpan.FromDays(30));
        DateTimeOffset retainedStart = exactRemaining.Definition.KeyAuthority.RetainedKeys[0].NotBefore;
        BaseSemanticRecoveryAuthorityContract.IsValidAt(exactRemaining, retainedStart).Should().BeTrue();
        BaseSemanticRecoveryAuthorityContract.IsValidAt(exactRemaining, retainedStart.AddTicks(1)).Should().BeFalse();
        BaseSemanticRecoveryAuthorityContract.IsValid(registration with
        {
            Definition = registration.Definition with
            {
                RequiredCapability = registration.Definition.RequiredCapability with { MaximumPages = 2 },
            },
        }).Should().BeFalse();
        BaseSemanticRecoveryAuthorityContract.IsValid(registration with
        {
            Certification = registration.Certification with { Signature = new byte[Ed25519.SignatureSize].ToImmutableArray() },
        }).Should().BeFalse();
    }

    [Fact]
    public void Registry_disposes_hostile_owned_instance_when_descriptor_access_fails()
    {
        byte[] seed = Enumerable.Range(1, Ed25519.SecretKeySize).Select(static value => (byte)value).ToArray();
        BaseSemanticRecoveryAuthorityRegistration registration = Registration(seed);
        BaseSemanticActivationCapability capability = BaseSemanticActivationCapabilityContract.BuiltIn(durable: true);
        capability = capability with { RestoreModes = [BaseActivationRestoreMode.NewDisasterDomain], Checksum = [] };
        capability = capability with { Checksum = BaseSemanticActivationCapabilityContract.Checksum(capability) };
        var selection = new BaseSemanticActivationRestoreSelection
        {
            LogicalStoreId = "module-store", EnabledRestoreMode = BaseActivationRestoreMode.NewDisasterDomain,
            SelectionGeneration = 1,
            Identity = BaseMutationRequestIdentity.Create("semantic", "restore-selection", "hostile",
                BaseMutationRequestFingerprint.Create(SHA256.HashData("hostile"u8))), Checksum = [],
        };
        var hostile = new ThrowingDescriptorAuthority();
        registration = registration with { Factory = new OwnedFactory(hostile) };

        Action act = () => _ = new BaseSemanticRecoveryAuthorityRegistry([selection], [registration], capability, 1,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero)));

        act.Should().Throw<InvalidOperationException>().WithMessage(BaseSemanticActivationErrorCodes.CapabilityUnavailable);
        hostile.DisposeCount.Should().Be(1);

        var nullDescriptor = new NullDescriptorAsyncAuthority();
        registration = Registration(seed) with { Factory = new OwnedFactory(nullDescriptor) };
        Action nullAct = () => _ = new BaseSemanticRecoveryAuthorityRegistry([selection], [registration], capability, 1,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero)));
        nullAct.Should().Throw<InvalidOperationException>().WithMessage(BaseSemanticActivationErrorCodes.CapabilityUnavailable);
        nullDescriptor.DisposeCount.Should().Be(1);
    }

    private static BaseSemanticRecoveryAuthorityRegistration Registration(byte[] seed, TimeSpan? retainedFor = null)
    {
        byte[] publicKey = new byte[Ed25519.PublicKeySize]; Ed25519.GeneratePublicKey(seed, 0, publicKey, 0);
        var retained = new BaseSemanticRecoveryRetainedKeyAuthority
        {
            SigningKeyId = "signing", SigningKeyVersion = 1, SigningPublicKey = publicKey.ToImmutableArray(),
            EncryptionKeyId = "encryption", EncryptionKeyVersion = 1,
            NotBefore = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            RetainUntil = retainedFor is null
                ? new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero)
                : new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).Add(retainedFor.Value), Checksum = [],
        };
        retained = retained with { Checksum = BaseSemanticRecoveryAuthorityContract.RetainedKeyChecksum(retained) };
        var keys = new BaseSemanticRecoveryKeyAuthorityReceipt
        {
            AuthorityId = "semantic.recovery", AuthorityVersion = 1,
            SigningAlgorithm = BaseSemanticRecoverySigningAlgorithm.Ed25519,
            EncryptionAlgorithm = BaseSemanticRecoveryEncryptionAlgorithm.Aes256Gcm,
            CurrentSigningKeyId = retained.SigningKeyId, CurrentSigningKeyVersion = retained.SigningKeyVersion,
            CurrentSigningPublicKey = retained.SigningPublicKey, CurrentEncryptionKeyId = retained.EncryptionKeyId,
            CurrentEncryptionKeyVersion = retained.EncryptionKeyVersion, RetainedKeys = [retained],
            MinimumKeyRetention = TimeSpan.FromDays(30), Checksum = [],
        };
        keys = keys with { Checksum = BaseSemanticRecoveryAuthorityContract.KeyAuthorityChecksum(keys) };
        var capability = new BaseSemanticRecoveryAuthorityCapability
        {
            DurablePendingSupported = true, IdentifiedCancellationSupported = true, CommitBoundRetentionSupported = true,
            MaximumEntries = 1024, MaximumPages = 8, MaximumPageEntries = 256,
            MaximumRequestBytes = 1_048_576, MaximumResultBytes = 1_048_576, MaximumTransientBytes = 2_097_152,
            MaximumAcquisitionDuration = TimeSpan.FromSeconds(5), MaximumPublicationDuration = TimeSpan.FromSeconds(10),
            CapabilityChecksum = [],
        };
        capability = capability with { CapabilityChecksum = BaseSemanticRecoveryAuthorityContract.CapabilityChecksum(capability) };
        var definition = new BaseSemanticRecoveryAuthorityDefinition
        {
            Id = keys.AuthorityId, Version = keys.AuthorityVersion, LogicalStoreId = "module-store", OwningModuleId = "module",
            RequiredCapability = capability, KeyAuthority = keys, ContractChecksum = [],
        };
        definition = definition with { ContractChecksum = BaseSemanticRecoveryAuthorityContract.DefinitionChecksum(definition) };
        var certification = new BaseSemanticRecoveryAuthorityCertificationReceipt
        {
            AuthorityId = definition.Id, AuthorityVersion = definition.Version,
            ImplementationContractId = "test.recovery", ImplementationContractVersion = 1,
            NativeDependencyReceiptChecksum = SHA256.HashData("native"u8).ToImmutableArray(),
            CapabilityChecksum = capability.CapabilityChecksum, DefinitionContractChecksum = definition.ContractChecksum,
            ExecutedCertificationReportChecksum = SHA256.HashData("report"u8).ToImmutableArray(), ObservationSequence = 1,
            Checksum = [], Signature = [],
        };
        certification = certification with { Checksum = BaseSemanticRecoveryAuthorityContract.CertificationChecksum(certification) };
        byte[] digest = SHA512.HashData([.. Encoding.UTF8.GetBytes("base.semanticRecovery.certificationSignature.v1\0"), .. certification.Checksum]);
        byte[] signature = new byte[Ed25519.SignatureSize]; Ed25519.Sign(seed, 0, publicKey, 0, digest, 0, digest.Length, signature, 0);
        certification = certification with { Signature = signature.ToImmutableArray() };
        var descriptor = new BaseSemanticRecoveryAuthorityInstanceDescriptor
        {
            ImplementationContractId = certification.ImplementationContractId,
            ImplementationContractVersion = certification.ImplementationContractVersion,
            CapabilityChecksum = certification.CapabilityChecksum,
            KeyAuthorityChecksum = definition.KeyAuthority.Checksum,
            DefinitionChecksum = definition.ContractChecksum,
            CertificationChecksum = certification.Checksum,
            Checksum = [],
        };
        descriptor = descriptor with { Checksum = BaseSemanticRecoveryAuthorityContract.InstanceDescriptorChecksum(descriptor) };
        return new BaseSemanticRecoveryAuthorityRegistration { Definition = definition, Certification = certification, Factory = new Factory(descriptor) };
    }

    private sealed class Factory(BaseSemanticRecoveryAuthorityInstanceDescriptor descriptor) : IBaseSemanticRecoveryAuthorityFactory
    {
        public IBaseSemanticActivationRecoveryAuthority CreateOwned() => new Authority(descriptor);
    }

    private sealed class OwnedFactory(IBaseSemanticActivationRecoveryAuthority instance) : IBaseSemanticRecoveryAuthorityFactory
    {
        public IBaseSemanticActivationRecoveryAuthority CreateOwned() => instance;
    }

    private sealed class ThrowingDescriptorAuthority : IBaseSemanticActivationRecoveryAuthority, IDisposable
    {
        public int DisposeCount { get; private set; }
        public BaseSemanticRecoveryAuthorityInstanceDescriptor Descriptor => throw new InvalidOperationException("hostile");
        public void Dispose() => DisposeCount++;
        public ValueTask<BaseResult<BaseSemanticRecoveryPendingPublication>> BeginAsync(BaseSemanticRecoveryBeginRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPublishedHead>> FinalizeAsync(BaseSemanticRecoveryFinalizeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryCancellationResult>> CancelAsync(BaseSemanticRecoveryCancelRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPublishedHead>> ReadHeadAsync(BaseSemanticRecoveryHeadRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPublicationPage>> ReadPageAsync(BaseSemanticRecoveryPageRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NullDescriptorAsyncAuthority : IBaseSemanticActivationRecoveryAuthority, IAsyncDisposable
    {
        public int DisposeCount { get; private set; }
        public BaseSemanticRecoveryAuthorityInstanceDescriptor Descriptor => null!;
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
        public ValueTask<BaseResult<BaseSemanticRecoveryPendingPublication>> BeginAsync(BaseSemanticRecoveryBeginRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPublishedHead>> FinalizeAsync(BaseSemanticRecoveryFinalizeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryCancellationResult>> CancelAsync(BaseSemanticRecoveryCancelRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPublishedHead>> ReadHeadAsync(BaseSemanticRecoveryHeadRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPublicationPage>> ReadPageAsync(BaseSemanticRecoveryPageRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Authority(BaseSemanticRecoveryAuthorityInstanceDescriptor descriptor) : IBaseSemanticActivationRecoveryAuthority
    {
        public BaseSemanticRecoveryAuthorityInstanceDescriptor Descriptor { get; } = descriptor;
        public ValueTask<BaseResult<BaseSemanticRecoveryPendingPublication>> BeginAsync(BaseSemanticRecoveryBeginRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPublishedHead>> FinalizeAsync(BaseSemanticRecoveryFinalizeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryCancellationResult>> CancelAsync(BaseSemanticRecoveryCancelRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPublishedHead>> ReadHeadAsync(BaseSemanticRecoveryHeadRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPublicationPage>> ReadPageAsync(BaseSemanticRecoveryPageRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
