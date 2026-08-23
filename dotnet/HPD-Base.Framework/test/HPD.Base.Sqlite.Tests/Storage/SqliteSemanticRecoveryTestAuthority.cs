using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace HPD.Base.Sqlite.Tests.Storage;

internal sealed class SqliteSemanticRecoveryTestAuthority : IBaseSemanticActivationRecoveryAuthority
{
    private readonly byte[] _seed;
    private BaseSemanticRecoveryPublicationEntry? _publication;

    internal SqliteSemanticRecoveryTestAuthority(byte[] seed, BaseSemanticRecoveryAuthorityInstanceDescriptor descriptor)
    { _seed = seed; Descriptor = descriptor; }

    public BaseSemanticRecoveryAuthorityInstanceDescriptor Descriptor { get; }

    internal void Publish(BaseSemanticRecoveryPublicationEntry publication) => _publication = publication;

    public ValueTask<BaseResult<BaseSemanticRecoveryPublishedHead>> ReadHeadAsync(
        BaseSemanticRecoveryHeadRequest request, CancellationToken cancellationToken)
    {
        long count = _publication is null ? 0 : 1;
        ImmutableArray<byte> ordered = _publication is null
            ? BaseSemanticRecoveryAuthorityContract.EmptyPublicationSetChecksum()
            : BaseSemanticRecoveryAuthorityContract.AdvancePublicationSetChecksum(
                BaseSemanticRecoveryAuthorityContract.EmptyPublicationSetChecksum(), 0, _publication);
        var head = new BaseSemanticRecoveryPublishedHead
        {
            RequestChecksum = BaseSemanticRecoveryAuthorityContract.HeadRequestChecksum(request),
            ApplicationId = request.ApplicationId, LogicalStoreId = request.LogicalStoreId,
            PublishedSequence = count, EntryCount = count, HasPendingSuccessor = false,
            OrderedEntrySetChecksum = ordered, SigningKeyId = "signing", SigningKeyVersion = 1,
            Checksum = [], Signature = [],
        };
        head = head with { Checksum = BaseSemanticRecoveryAuthorityContract.PublishedHeadChecksum(head) };
        head = head with { Signature = Sign("base.semanticRecovery.headSignature.v1\0", head.Checksum) };
        return ValueTask.FromResult<BaseResult<BaseSemanticRecoveryPublishedHead>>(
            new BaseSuccess<BaseSemanticRecoveryPublishedHead>(head, OperationStatus.Ok, null, null, null, null));
    }

    public ValueTask<BaseResult<BaseSemanticRecoveryPublicationPage>> ReadPageAsync(
        BaseSemanticRecoveryPageRequest request, CancellationToken cancellationToken)
    {
        ImmutableArray<BaseSemanticRecoveryPublicationEntry> entries =
            _publication is not null && request.AfterSequence == 0 ? [_publication] : [];
        var page = new BaseSemanticRecoveryPublicationPage
        {
            AfterSequence = request.AfterSequence, Entries = entries,
            NextAfterSequence = null, HeadSequence = request.Head.PublishedSequence, Checksum = [],
        };
        page = page with { Checksum = BaseSemanticRecoveryAuthorityContract.PublicationPageChecksum(page) };
        return ValueTask.FromResult<BaseResult<BaseSemanticRecoveryPublicationPage>>(
            new BaseSuccess<BaseSemanticRecoveryPublicationPage>(page, OperationStatus.Ok, null, null, null, null));
    }

    internal ImmutableArray<byte> Sign(string purpose, ImmutableArray<byte> checksum)
    {
        byte[] publicKey = new byte[Ed25519.PublicKeySize]; Ed25519.GeneratePublicKey(_seed, 0, publicKey, 0);
        byte[] digest = SHA512.HashData([.. Encoding.UTF8.GetBytes(purpose), .. checksum]);
        byte[] signature = new byte[Ed25519.SignatureSize];
        Ed25519.Sign(_seed, 0, publicKey, 0, digest, 0, digest.Length, signature, 0);
        return signature.ToImmutableArray();
    }

    public ValueTask<BaseResult<BaseSemanticRecoveryPendingPublication>> BeginAsync(BaseSemanticRecoveryBeginRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<BaseResult<BaseSemanticRecoveryPendingResolution>> ResolvePendingAsync(BaseSemanticRecoveryResolvePendingRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<BaseResult<BaseSemanticRecoveryFinalizationResult>> FinalizeAsync(BaseSemanticRecoveryFinalizeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<BaseResult<BaseSemanticRecoveryCancellationResult>> CancelAsync(BaseSemanticRecoveryCancelRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    internal static (BaseSemanticRecoveryAuthorityRegistry Registry, SqliteSemanticRecoveryTestAuthority Authority,
        BaseSemanticRecoveryAuthorityDefinition Definition) Create()
    {
        byte[] seed = Enumerable.Range(1, Ed25519.SecretKeySize).Select(static value => (byte)value).ToArray();
        byte[] publicKey = new byte[Ed25519.PublicKeySize]; Ed25519.GeneratePublicKey(seed, 0, publicKey, 0);
        var retained = new BaseSemanticRecoveryRetainedKeyAuthority
        {
            SigningKeyId = "signing", SigningKeyVersion = 1, SigningPublicKey = publicKey.ToImmutableArray(),
            EncryptionKeyId = "encryption", EncryptionKeyVersion = 1, NotBefore = DateTimeOffset.UnixEpoch,
            RetainUntil = DateTimeOffset.MaxValue, Checksum = [],
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
            DurablePendingSupported = true, IdentifiedCancellationSupported = true,
            CommitBoundRetentionSupported = true, PermanentPendingKeyRetentionSupported = true,
            MaximumEntries = 16, MaximumPages = 4, MaximumPageEntries = 4,
            MaximumRequestBytes = 1_048_576, MaximumResultBytes = 1_048_576,
            MaximumTransientBytes = 2_097_152, MaximumAcquisitionDuration = TimeSpan.FromSeconds(5),
            MaximumResolutionDuration = TimeSpan.FromSeconds(5), MaximumPublicationDuration = TimeSpan.FromSeconds(5),
            MaximumConcurrentOperations = 1, CapabilityChecksum = [],
        };
        capability = capability with { CapabilityChecksum = BaseSemanticRecoveryAuthorityContract.CapabilityChecksum(capability) };
        var limits = new BaseSemanticRecoveryOperationLimits
        {
            AcquisitionDeadline = TimeSpan.FromSeconds(3), ResolutionDeadline = TimeSpan.FromSeconds(3),
            PublicationDeadline = TimeSpan.FromSeconds(3), MaximumEntries = 8, MaximumPages = 2,
            MaximumPageEntries = 4, MaximumRequestBytes = 524_288, MaximumResultBytes = 524_288,
            MaximumTransientBytes = 1_048_576, MaximumConcurrentOperations = 1,
        };
        var definition = new BaseSemanticRecoveryAuthorityDefinition
        {
            Id = keys.AuthorityId, Version = keys.AuthorityVersion, LogicalStoreId = "module-store",
            OwningModuleId = "test", RecoveryGrantId = "semantic.recovery.recover",
            RequiredCapability = capability, Limits = limits, KeyAuthority = keys, ContractChecksum = [],
        };
        definition = definition with { ContractChecksum = BaseSemanticRecoveryAuthorityContract.DefinitionChecksum(definition) };
        var certification = new BaseSemanticRecoveryAuthorityCertificationReceipt
        {
            AuthorityId = definition.Id, AuthorityVersion = definition.Version,
            ImplementationContractId = "test.semantic.recovery", ImplementationContractVersion = 1,
            NativeDependencyReceiptChecksum = SHA256.HashData("native"u8).ToImmutableArray(),
            CapabilityChecksum = capability.CapabilityChecksum, DefinitionContractChecksum = definition.ContractChecksum,
            ExecutedCertificationReportChecksum = SHA256.HashData("report"u8).ToImmutableArray(),
            ObservationSequence = 1, Checksum = [], Signature = [],
        };
        certification = certification with { Checksum = BaseSemanticRecoveryAuthorityContract.CertificationChecksum(certification) };
        byte[] digest = SHA512.HashData([.. Encoding.UTF8.GetBytes("base.semanticRecovery.certificationSignature.v1\0"), .. certification.Checksum]);
        byte[] certSignature = new byte[Ed25519.SignatureSize]; Ed25519.Sign(seed, 0, publicKey, 0, digest, 0, digest.Length, certSignature, 0);
        certification = certification with { Signature = certSignature.ToImmutableArray() };
        var descriptor = new BaseSemanticRecoveryAuthorityInstanceDescriptor
        {
            ImplementationContractId = certification.ImplementationContractId,
            ImplementationContractVersion = certification.ImplementationContractVersion,
            CapabilityChecksum = capability.CapabilityChecksum, KeyAuthorityChecksum = keys.Checksum,
            DefinitionChecksum = definition.ContractChecksum, CertificationChecksum = certification.Checksum, Checksum = [],
        };
        descriptor = descriptor with { Checksum = BaseSemanticRecoveryAuthorityContract.InstanceDescriptorChecksum(descriptor) };
        var authority = new SqliteSemanticRecoveryTestAuthority(seed, descriptor);
        var registration = new BaseSemanticRecoveryAuthorityRegistration
        { Definition = definition, Certification = certification, Factory = new Factory(authority) };
        var identity = BaseMutationRequestIdentity.Create("control", "restore-mode", "one",
            BaseMutationRequestFingerprint.Create(SHA256.HashData("restore-mode"u8)));
        var selection = new BaseSemanticActivationRestoreSelection
        { LogicalStoreId = "module-store", EnabledRestoreMode = BaseActivationRestoreMode.NewDisasterDomain,
            SelectionGeneration = 1, Identity = identity, Checksum = [] };
        selection = selection with { Checksum = BaseSemanticRecoveryAuthorityRegistry.SelectionChecksum(selection) };
        BaseSemanticActivationCapability provider = BaseSemanticActivationCapabilityContract.BuiltIn(true) with
        { RestoreModes = [BaseActivationRestoreMode.InPlaceRecovery, BaseActivationRestoreMode.NewDisasterDomain], Checksum = [] };
        provider = provider with { Checksum = BaseSemanticActivationCapabilityContract.Checksum(provider) };
        var registry = new BaseSemanticRecoveryAuthorityRegistry([selection], [registration], provider, 1, TimeProvider.System);
        return (registry, authority, definition);
    }

    private sealed class Factory(SqliteSemanticRecoveryTestAuthority authority) : IBaseSemanticRecoveryAuthorityFactory
    { public IBaseSemanticActivationRecoveryAuthority CreateOwned() => authority; }
}
