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
        BaseSemanticRecoveryAuthorityContract.IsValidAt(registration, new DateTimeOffset(2110, 1, 1, 0, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        BaseSemanticRecoveryAuthorityContract.IsValidAt(registration, new DateTimeOffset(2010, 1, 1, 0, 0, 0, TimeSpan.Zero)).Should().BeFalse();
        BaseSemanticRecoveryAuthorityRegistration exactRemaining = Registration(seed);
        DateTimeOffset retainedStart = exactRemaining.Definition.KeyAuthority.RetainedKeys[0].NotBefore;
        BaseSemanticRecoveryAuthorityContract.IsValidAt(exactRemaining, retainedStart).Should().BeTrue();
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

    [Fact]
    public void Pending_resolution_is_signed_and_bound_to_the_complete_begin_request()
    {
        byte[] seed = Enumerable.Range(1, Ed25519.SecretKeySize).Select(static value => (byte)value).ToArray();
        BaseSemanticRecoveryAuthorityDefinition definition = Registration(seed).Definition;
        var intent = new BaseSemanticRecoveryPendingTerminalIntent
        {
            Boundary = new() { DefinitionId = "semantic", ScopeBindingId = new byte[16].ToImmutableArray(),
                Key = BaseSemanticActivationKeyDigest.Create(SHA256.HashData("key"u8)) },
            RetirementOperationFingerprint = SHA256.HashData("retire"u8).ToImmutableArray(),
            SubjectLifetime = null, Checksum = [],
        };
        intent = intent with { Checksum = BaseSemanticRecoveryAuthorityContract.PendingIntentChecksum(intent) };
        var request = new BaseSemanticRecoveryResolvePendingRequest
        {
            ApplicationId = "app", LogicalStoreId = definition.LogicalStoreId, Intent = intent,
            BeginIdentity = BaseMutationRequestIdentity.Create("scope", "begin", "one",
                BaseMutationRequestFingerprint.Create(SHA256.HashData("begin-one"u8))),
            Limits = definition.Limits,
        };
        var resolution = new BaseSemanticRecoveryPendingResolution
        {
            RequestChecksum = BaseSemanticRecoveryAuthorityContract.ResolvePendingRequestChecksum(request),
            Disposition = BaseSemanticRecoveryPendingResolutionDisposition.Missing,
            Pending = null, Checksum = [], Signature = [],
        };
        resolution = resolution with { Checksum = BaseSemanticRecoveryAuthorityContract.PendingResolutionChecksum(resolution) };
        resolution = resolution with { Signature = Sign(seed, "base.semanticRecovery.pendingResolutionSignature.v1\0", resolution.Checksum) };

        BaseSemanticRecoveryAuthorityContract.PendingResolutionIsValid(definition, request, resolution,
            new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        BaseSemanticRecoveryResolvePendingRequest substituted = request with
        {
            BeginIdentity = BaseMutationRequestIdentity.Create("scope", "begin", "two",
                BaseMutationRequestFingerprint.Create(SHA256.HashData("begin-two"u8))),
        };
        BaseSemanticRecoveryAuthorityContract.PendingResolutionIsValid(definition, substituted, resolution,
            new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero)).Should().BeFalse();
    }

    [Fact]
    public void Published_head_is_signed_and_bound_to_the_exact_artifact_request()
    {
        byte[] seed = Enumerable.Range(1, Ed25519.SecretKeySize).Select(static value => (byte)value).ToArray();
        BaseSemanticRecoveryAuthorityDefinition definition = Registration(seed).Definition;
        var request = new BaseSemanticRecoveryHeadRequest
        {
            ApplicationId = "app", LogicalStoreId = definition.LogicalStoreId, ArtifactId = "artifact-one",
            ArtifactChecksum = SHA256.HashData("artifact-one"u8).ToImmutableArray(), Limits = definition.Limits,
        };
        var head = new BaseSemanticRecoveryPublishedHead
        {
            RequestChecksum = BaseSemanticRecoveryAuthorityContract.HeadRequestChecksum(request),
            ApplicationId = request.ApplicationId, LogicalStoreId = request.LogicalStoreId,
            PublishedSequence = 0, HasPendingSuccessor = false, EntryCount = 0,
            OrderedEntrySetChecksum = BaseSemanticRecoveryAuthorityContract.EmptyPublicationSetChecksum(),
            SigningKeyId = definition.KeyAuthority.CurrentSigningKeyId,
            SigningKeyVersion = definition.KeyAuthority.CurrentSigningKeyVersion, Checksum = [], Signature = [],
        };
        head = head with { Checksum = BaseSemanticRecoveryAuthorityContract.PublishedHeadChecksum(head) };
        head = head with { Signature = Sign(seed, "base.semanticRecovery.headSignature.v1\0", head.Checksum) };

        BaseSemanticRecoveryAuthorityContract.PublishedHeadIsValid(definition, request.ApplicationId,
            request.LogicalStoreId, BaseSemanticRecoveryAuthorityContract.HeadRequestChecksum(request), head).Should().BeTrue();
        BaseSemanticRecoveryHeadRequest substituted = request with
        {
            ArtifactId = "artifact-two", ArtifactChecksum = SHA256.HashData("artifact-two"u8).ToImmutableArray(),
        };
        BaseSemanticRecoveryAuthorityContract.PublishedHeadIsValid(definition, substituted.ApplicationId,
            substituted.LogicalStoreId, BaseSemanticRecoveryAuthorityContract.HeadRequestChecksum(substituted), head).Should().BeFalse();
        BaseSemanticRecoveryAuthorityContract.PublishedHeadIsValid(definition, "other-app",
            request.LogicalStoreId, BaseSemanticRecoveryAuthorityContract.HeadRequestChecksum(request), head).Should().BeFalse();
    }

    [Fact]
    public async Task Noncooperative_external_work_quarantines_until_explicit_release()
    {
        byte[] seed = Enumerable.Range(1, Ed25519.SecretKeySize).Select(static value => (byte)value).ToArray();
        BaseSemanticRecoveryAuthorityRegistration registration = Registration(seed);
        var delayed = new DelayedAuthority(((Authority)registration.Factory.CreateOwned()).Descriptor);
        registration = registration with { Factory = new OwnedFactory(delayed) };
        BaseSemanticActivationCapability capability = BaseSemanticActivationCapabilityContract.BuiltIn(durable: true);
        capability = capability with { RestoreModes = [BaseActivationRestoreMode.NewDisasterDomain], Checksum = [] };
        capability = capability with { Checksum = BaseSemanticActivationCapabilityContract.Checksum(capability) };
        var selection = new BaseSemanticActivationRestoreSelection
        {
            LogicalStoreId = "module-store", EnabledRestoreMode = BaseActivationRestoreMode.NewDisasterDomain,
            SelectionGeneration = 1, Identity = BaseMutationRequestIdentity.Create("semantic", "selection", "one",
                BaseMutationRequestFingerprint.Create(SHA256.HashData("selection"u8))), Checksum = [],
        };
        await using var registry = new BaseSemanticRecoveryAuthorityRegistry([selection], [registration], capability, 1,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero)));

        Func<Task> invoke = async () => await registry.InvokeAsync<BaseSemanticRecoveryPendingResolution>(
            "module-store", TimeSpan.FromMilliseconds(10),
            (authority, token) => authority.ResolvePendingAsync(null!, token), default);
        await invoke.Should().ThrowAsync<TimeoutException>();
        registry.IsQuarantined("module-store").Should().BeTrue();
        registry.RecoverQuarantine(new() { LogicalStoreId = "module-store", Principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "operator" }, Identity = selection.Identity })
            .RequireValue().Released.Should().BeFalse();
        delayed.Release();
        await Task.Delay(20);
        registry.RecoverQuarantine(new() { LogicalStoreId = "module-store", Principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "operator" }, Identity = selection.Identity })
            .RequireValue().Released.Should().BeTrue();
        registry.IsQuarantined("module-store").Should().BeFalse();
        delayed.Reset();
        await invoke.Should().ThrowAsync<TimeoutException>();
        await registry.DisposeAsync();
        delayed.DisposeCount.Should().Be(0);
        delayed.Release();
        await Task.Delay(20);
        delayed.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Disposal_drains_an_admitted_cooperative_call_before_disposing_authority()
    {
        byte[] seed = Enumerable.Range(1, Ed25519.SecretKeySize).Select(static value => (byte)value).ToArray();
        BaseSemanticRecoveryAuthorityRegistration registration = Registration(seed);
        var delayed = new DelayedAuthority(((Authority)registration.Factory.CreateOwned()).Descriptor);
        registration = registration with { Factory = new OwnedFactory(delayed) };
        BaseSemanticActivationCapability capability = BaseSemanticActivationCapabilityContract.BuiltIn(durable: true);
        capability = capability with { RestoreModes = [BaseActivationRestoreMode.NewDisasterDomain], Checksum = [] };
        capability = capability with { Checksum = BaseSemanticActivationCapabilityContract.Checksum(capability) };
        var selection = new BaseSemanticActivationRestoreSelection
        {
            LogicalStoreId = "module-store", EnabledRestoreMode = BaseActivationRestoreMode.NewDisasterDomain,
            SelectionGeneration = 1, Identity = BaseMutationRequestIdentity.Create("semantic", "selection", "drain",
                BaseMutationRequestFingerprint.Create(SHA256.HashData("selection-drain"u8))), Checksum = [],
        };
        var registry = new BaseSemanticRecoveryAuthorityRegistry([selection], [registration], capability, 1,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero)));

        Task<BaseResult<BaseSemanticRecoveryPendingResolution>> active = registry.InvokeAsync<BaseSemanticRecoveryPendingResolution>(
            "module-store", TimeSpan.FromSeconds(5),
            (authority, token) => authority.ResolvePendingAsync(null!, token), default).AsTask();
        await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await registry.DisposeAsync();
        delayed.DisposeCount.Should().Be(0);

        delayed.Release();
        await active;
        await Task.Delay(20);
        delayed.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void Pending_ticket_remains_verifiable_after_current_signing_key_rotation()
    {
        byte[] oldSeed = Enumerable.Range(1, Ed25519.SecretKeySize).Select(static value => (byte)value).ToArray();
        byte[] newSeed = Enumerable.Range(33, Ed25519.SecretKeySize).Select(static value => (byte)value).ToArray();
        BaseSemanticRecoveryAuthorityDefinition original = Registration(oldSeed).Definition;
        var intent = new BaseSemanticRecoveryPendingTerminalIntent
        {
            Boundary = new() { DefinitionId = "semantic", ScopeBindingId = new byte[16].ToImmutableArray(),
                Key = BaseSemanticActivationKeyDigest.Create(SHA256.HashData("rotated-key"u8)) },
            RetirementOperationFingerprint = SHA256.HashData("retire"u8).ToImmutableArray(), SubjectLifetime = null,
            Checksum = [],
        };
        intent = intent with { Checksum = BaseSemanticRecoveryAuthorityContract.PendingIntentChecksum(intent) };
        var pending = new BaseSemanticRecoveryPendingPublication
        {
            Sequence = 1, TicketNonce = "ticket", IntentChecksum = intent.Checksum,
            SigningKeyId = "signing", SigningKeyVersion = 1,
            CancellationEligibleAt = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero),
            Checksum = [], Signature = [],
        };
        pending = pending with { Checksum = BaseSemanticRecoveryAuthorityContract.PendingChecksum(pending) };
        pending = pending with { Signature = Sign(oldSeed, "base.semanticRecovery.pendingSignature.v1\0", pending.Checksum) };

        byte[] newPublic = new byte[Ed25519.PublicKeySize]; Ed25519.GeneratePublicKey(newSeed, 0, newPublic, 0);
        BaseSemanticRecoveryRetainedKeyAuthority oldKey = original.KeyAuthority.RetainedKeys[0];
        var newKey = oldKey with
        {
            SigningKeyId = "signing-next", SigningKeyVersion = 2, SigningPublicKey = newPublic.ToImmutableArray(),
            EncryptionKeyId = "encryption-next", EncryptionKeyVersion = 2, Checksum = [],
        };
        newKey = newKey with { Checksum = BaseSemanticRecoveryAuthorityContract.RetainedKeyChecksum(newKey) };
        BaseSemanticRecoveryKeyAuthorityReceipt rotatedKeys = original.KeyAuthority with
        {
            CurrentSigningKeyId = newKey.SigningKeyId, CurrentSigningKeyVersion = newKey.SigningKeyVersion,
            CurrentSigningPublicKey = newKey.SigningPublicKey, CurrentEncryptionKeyId = newKey.EncryptionKeyId,
            CurrentEncryptionKeyVersion = newKey.EncryptionKeyVersion,
            RetainedKeys = [oldKey, newKey], Checksum = [],
        };
        rotatedKeys = rotatedKeys with { Checksum = BaseSemanticRecoveryAuthorityContract.KeyAuthorityChecksum(rotatedKeys) };
        BaseSemanticRecoveryAuthorityDefinition rotated = original with { KeyAuthority = rotatedKeys, ContractChecksum = [] };
        rotated = rotated with { ContractChecksum = BaseSemanticRecoveryAuthorityContract.DefinitionChecksum(rotated) };

        BaseSemanticRecoveryAuthorityContract.PendingCommitIsValid(rotated, intent, pending).Should().BeTrue();
    }

    private static ImmutableArray<byte> Sign(byte[] seed, string purpose, ImmutableArray<byte> checksum)
    {
        byte[] publicKey = new byte[Ed25519.PublicKeySize]; Ed25519.GeneratePublicKey(seed, 0, publicKey, 0);
        byte[] digest = SHA512.HashData([.. Encoding.UTF8.GetBytes(purpose), .. checksum]);
        byte[] signature = new byte[Ed25519.SignatureSize];
        Ed25519.Sign(seed, 0, publicKey, 0, digest, 0, digest.Length, signature, 0);
        return signature.ToImmutableArray();
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
                ? DateTimeOffset.MaxValue
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
            PermanentPendingKeyRetentionSupported = true,
            MaximumEntries = 1024, MaximumPages = 8, MaximumPageEntries = 256,
            MaximumRequestBytes = 1_048_576, MaximumResultBytes = 1_048_576, MaximumTransientBytes = 2_097_152,
            MaximumAcquisitionDuration = TimeSpan.FromSeconds(5), MaximumResolutionDuration = TimeSpan.FromSeconds(5),
            MaximumPublicationDuration = TimeSpan.FromSeconds(10),
            MaximumConcurrentOperations = 2,
            CapabilityChecksum = [],
        };
        capability = capability with { CapabilityChecksum = BaseSemanticRecoveryAuthorityContract.CapabilityChecksum(capability) };
        var definition = new BaseSemanticRecoveryAuthorityDefinition
        {
            Id = keys.AuthorityId, Version = keys.AuthorityVersion, LogicalStoreId = "module-store", OwningModuleId = "module",
            RecoveryGrantId = "semantic.recovery.recover",
            RequiredCapability = capability, Limits = new BaseSemanticRecoveryOperationLimits
            {
                AcquisitionDeadline = TimeSpan.FromSeconds(3), ResolutionDeadline = TimeSpan.FromSeconds(3),
                PublicationDeadline = TimeSpan.FromSeconds(8), MaximumEntries = 512, MaximumPages = 4,
                MaximumPageEntries = 128, MaximumRequestBytes = 524_288, MaximumResultBytes = 524_288,
                MaximumTransientBytes = 1_048_576, MaximumConcurrentOperations = 1,
            }, KeyAuthority = keys, ContractChecksum = [],
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
        public ValueTask<BaseResult<BaseSemanticRecoveryPendingResolution>> ResolvePendingAsync(BaseSemanticRecoveryResolvePendingRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryFinalizationResult>> FinalizeAsync(BaseSemanticRecoveryFinalizeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
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
        public ValueTask<BaseResult<BaseSemanticRecoveryPendingResolution>> ResolvePendingAsync(BaseSemanticRecoveryResolvePendingRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryFinalizationResult>> FinalizeAsync(BaseSemanticRecoveryFinalizeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryCancellationResult>> CancelAsync(BaseSemanticRecoveryCancelRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPublishedHead>> ReadHeadAsync(BaseSemanticRecoveryHeadRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPublicationPage>> ReadPageAsync(BaseSemanticRecoveryPageRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Authority(BaseSemanticRecoveryAuthorityInstanceDescriptor descriptor) : IBaseSemanticActivationRecoveryAuthority
    {
        public BaseSemanticRecoveryAuthorityInstanceDescriptor Descriptor { get; } = descriptor;
        public ValueTask<BaseResult<BaseSemanticRecoveryPendingPublication>> BeginAsync(BaseSemanticRecoveryBeginRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPendingResolution>> ResolvePendingAsync(BaseSemanticRecoveryResolvePendingRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryFinalizationResult>> FinalizeAsync(BaseSemanticRecoveryFinalizeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryCancellationResult>> CancelAsync(BaseSemanticRecoveryCancelRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPublishedHead>> ReadHeadAsync(BaseSemanticRecoveryHeadRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPublicationPage>> ReadPageAsync(BaseSemanticRecoveryPageRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class DelayedAuthority(BaseSemanticRecoveryAuthorityInstanceDescriptor descriptor) : IBaseSemanticActivationRecoveryAuthority, IDisposable
    {
        private TaskCompletionSource<BaseResult<BaseSemanticRecoveryPendingResolution>> completion = NewCompletion();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount { get; private set; }
        public BaseSemanticRecoveryAuthorityInstanceDescriptor Descriptor => descriptor;
        public void Release() => completion.TrySetResult(new BaseFailure<BaseSemanticRecoveryPendingResolution>(OperationStatus.StoreError,
            new BaseError { Code = "late", Message = "late", Category = ErrorCategory.Store }, null, null));
        public void Reset() => completion = NewCompletion();
        public void Dispose() => DisposeCount++;
        public ValueTask<BaseResult<BaseSemanticRecoveryPendingPublication>> BeginAsync(BaseSemanticRecoveryBeginRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPendingResolution>> ResolvePendingAsync(BaseSemanticRecoveryResolvePendingRequest request, CancellationToken cancellationToken)
        { Started.TrySetResult(); return new(completion.Task); }
        public ValueTask<BaseResult<BaseSemanticRecoveryFinalizationResult>> FinalizeAsync(BaseSemanticRecoveryFinalizeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryCancellationResult>> CancelAsync(BaseSemanticRecoveryCancelRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPublishedHead>> ReadHeadAsync(BaseSemanticRecoveryHeadRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSemanticRecoveryPublicationPage>> ReadPageAsync(BaseSemanticRecoveryPageRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        private static TaskCompletionSource<BaseResult<BaseSemanticRecoveryPendingResolution>> NewCompletion() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
