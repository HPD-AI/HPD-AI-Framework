using System.Collections.Immutable;
using HPD.Base;

namespace HPD.Base.Tests.Abstractions.Schema;

public sealed class BaseLogicalIndexProviderContractTests
{
    [Fact]
    public void BuiltInAndUnsupportedCapabilitiesAreCanonical()
    {
        BaseLogicalIndexProviderCapability supported =
            BaseLogicalIndexProviderContract.BuiltInCapability();
        BaseLogicalIndexProviderCapability unsupported =
            BaseLogicalIndexProviderContract.UnsupportedCapability();

        Assert.True(BaseLogicalIndexProviderContract.ValidateCapability(supported));
        Assert.True(BaseLogicalIndexProviderContract.ValidateCapability(unsupported));
        Assert.True(supported.Supported);
        Assert.False(unsupported.Supported);
        Assert.True(supported.AccessShapes.SequenceEqual(
            [BaseIndexAccessShape.LogicalIndexPoint, BaseIndexAccessShape.CollectionGenerationScan]));
    }

    [Fact]
    public void ReportIsDeeplyOwnedAndRejectsRegistryReordering()
    {
        byte[] authority = Enumerable.Repeat((byte)0x41, 32).ToArray();
        BaseLogicalIndexCertificationReport sealedReport =
            BaseLogicalIndexProviderContract.SealReport(Report(authority));

        authority[0] ^= 0xff;

        Assert.True(BaseLogicalIndexProviderContract.ValidateReport(sealedReport));
        Assert.Equal(0x41, sealedReport.Cases[0].BeforeMemberSetChecksum[0]);

        BaseLogicalIndexCertificationReport reordered = sealedReport with
        {
            Cases = sealedReport.Cases.SetItem(0, sealedReport.Cases[0] with
            {
                Id = BaseLogicalIndexProviderContract.CaseIds[1],
            }),
        };
        Assert.False(BaseLogicalIndexProviderContract.ValidateReport(reordered));
    }

    [Fact]
    public void UnsupportedProfileRejectsSubstitutedAuthority()
    {
        BaseLogicalIndexProviderProfile profile =
            BaseLogicalIndexProviderContract.UnsupportedProfile("inmemory");

        Assert.True(BaseLogicalIndexProviderContract.ValidateProfile(profile));
        Assert.False(BaseLogicalIndexProviderContract.ValidateProfile(profile with
        {
            ExecutedReportChecksum = Enumerable.Repeat((byte)0x22, 32).ToImmutableArray(),
        }));
    }

    [Fact]
    public void SupportedProfileRequiresExactCapabilitiesAndExpectedCaseOutcomes()
    {
        BaseLogicalIndexProviderCapability production =
            BaseLogicalIndexProviderContract.BuiltInCapability();
        BaseLogicalIndexCertificationReport report = ExactReport(production);

        BaseLogicalIndexProviderProfile profile = BaseLogicalIndexProviderContract
            .SealSupportedProfile(report, production, ["runtime:test"]);

        Assert.True(BaseLogicalIndexProviderContract.ValidateProfile(profile));
        Assert.True(profile.Supported);
        Assert.Throws<ArgumentException>(() => BaseLogicalIndexProviderContract
            .SealSupportedProfile(report with
            {
                Cases = report.Cases.SetItem(0, report.Cases[0] with
                {
                    ObservedStatus = OperationStatus.StoreError,
                    ObservedErrorCode = BaseSchemaErrorCodes.ProviderEvidenceInvalid,
                }),
                Checksum = [],
            }, production));
    }

    [Theory]
    [InlineData("inmemory")]
    [InlineData("sqlite")]
    public void FrozenExecutedReportsRoundTripAndRejectHostileBytes(string kind)
    {
        BaseLogicalIndexCertificationReport report =
            BaseLogicalIndexBuiltInCertification.LoadFrozenExecutedReport(kind);
        ImmutableArray<byte> encoded = BaseLogicalIndexFrozenReportCodec.Encode(report);

        BaseLogicalIndexCertificationReport decoded =
            BaseLogicalIndexFrozenReportCodec.Decode(encoded.AsSpan());
        ImmutableArray<byte> reencoded = BaseLogicalIndexFrozenReportCodec.Encode(decoded);
        int firstDifference = Enumerable.Range(0, Math.Min(encoded.Length, reencoded.Length))
            .FirstOrDefault(index => encoded[index] != reencoded[index], -1);
        Assert.True(encoded.SequenceEqual(reencoded),
            $"firstDifference={firstDifference}; encoded={encoded.Length}; reencoded={reencoded.Length}");

        Assert.Throws<InvalidOperationException>(() =>
            BaseLogicalIndexFrozenReportCodec.Decode(encoded.AsSpan()[..^1]));
        byte[] hostile = encoded.ToArray();
        hostile[^1] ^= 0xff;
        Assert.Throws<InvalidOperationException>(() =>
            BaseLogicalIndexFrozenReportCodec.Decode(hostile));
    }

    [Fact]
    public void ProviderFactoryRejectsRequiredIndexAndL43ShapeSubstitution()
    {
        HPDBaseStoreProvider provider = InMemoryProviderInstaller.Create(null);

        BaseStoreProviderDescriptor missingBit = Descriptor(
            provider,
            provider.Capabilities & ~BaseStoreProviderCapabilities.RequiredIndexes,
            provider.SelectionMutationIndexShapes);
        Assert.Throws<InvalidOperationException>(() =>
            HPDBaseStoreProviderFactory.Create(missingBit, provider.Installer));

        BaseStoreProviderDescriptor reversedShapes = Descriptor(
            provider,
            provider.Capabilities,
            provider.SelectionMutationIndexShapes.Reverse().ToImmutableArray());
        Assert.Throws<InvalidOperationException>(() =>
            HPDBaseStoreProviderFactory.Create(reversedShapes, provider.Installer));
    }

    private static BaseStoreProviderDescriptor Descriptor(
        HPDBaseStoreProvider provider,
        BaseStoreProviderCapabilities capabilities,
        ImmutableArray<BaseIndexAccessShape> shapes) => new()
    {
        Kind = provider.Kind,
        ProtocolVersion = provider.ProtocolVersion,
        Capabilities = capabilities,
        RegistrationIds = provider.RegistrationIds.ToArray(),
        StorageProtectionCapabilities = provider.StorageProtectionCapabilities.ToArray(),
        MaximumBinaryFieldBytes = provider.MaximumBinaryFieldBytes,
        RelationalReads = provider.RelationalReads,
        SubjectReferences = provider.SubjectReferences,
        SubjectLifecycle = provider.SubjectLifecycle,
        SubjectRetirement = provider.SubjectRetirement,
        ModuleMutations = provider.ModuleMutations,
        TextSearch = provider.TextSearch,
        Activations = provider.Activations,
        SemanticActivations = provider.SemanticActivations,
        SemanticActivationCertification = provider.SemanticActivationCertification,
        LogicalIndexes = provider.LogicalIndexes,
        SelectionMutationIndexShapes = shapes,
    };

    private static BaseLogicalIndexCertificationReport Report(byte[] authority)
    {
        ImmutableArray<BaseLogicalIndexCertificationCaseResult> cases =
            BaseLogicalIndexProviderContract.CaseIds.Select((id, ordinal) => new BaseLogicalIndexCertificationCaseResult
            {
                Id = id,
                Ordinal = ordinal,
                ObservedStatus = OperationStatus.Ok,
                Accounting = new BaseLogicalIndexCertificationAccounting
                {
                    Records = 0,
                    PredicateEvaluations = 0,
                    Keys = 0,
                    KeyBytes = 0,
                    PostingKeys = 0,
                    Postings = 0,
                    ComparatorEntries = 0,
                    Comparisons = 0,
                    EvidenceBytes = 0,
                    RetainedDirectoryBytes = 0,
                    TransientBytes = 0,
                },
                BeforeMemberSetChecksum = authority.ToImmutableArray(),
                AfterMemberSetChecksum = authority.ToImmutableArray(),
                BeforePublicationChecksum = authority.ToImmutableArray(),
                AfterPublicationChecksum = authority.ToImmutableArray(),
                EvidenceChecksum = authority.ToImmutableArray(),
            }).ToImmutableArray();
        return new BaseLogicalIndexCertificationReport
        {
            ProviderId = "hpd.base.test.logicalIndexes",
            ProviderVersion = 1,
            StoreProviderKind = "inmemory",
            StoreProviderProtocolVersion = HPDBaseStoreProviderFactory.ProtocolVersion,
            ProductionCapabilityChecksum = authority.ToImmutableArray(),
            BoundedCertificationCapabilityChecksum = authority.ToImmutableArray(),
            Cases = cases,
            ContractChecksum = BaseLogicalIndexProviderContract.ContractChecksum(),
            Checksum = [],
        };
    }

    private static BaseLogicalIndexCertificationReport ExactReport(
        BaseLogicalIndexProviderCapability production)
    {
        byte[] authority = Enumerable.Repeat((byte)0x51, 32).ToArray();
        ImmutableArray<BaseLogicalIndexCertificationCaseResult> cases =
            BaseLogicalIndexProviderContract.CaseIds.Select((id, ordinal) =>
            {
                (OperationStatus status, string? error) =
                    BaseLogicalIndexProviderContract.ExpectedOutcome(id);
                return new BaseLogicalIndexCertificationCaseResult
                {
                    Id = id,
                    Ordinal = ordinal,
                    ObservedStatus = status,
                    ObservedErrorCode = error,
                    Accounting = new BaseLogicalIndexCertificationAccounting
                    {
                        Records = 0, PredicateEvaluations = 0, Keys = 0, KeyBytes = 0,
                        PostingKeys = 0, Postings = 0, ComparatorEntries = 0,
                        Comparisons = 0, EvidenceBytes = 0,
                        RetainedDirectoryBytes = 0, TransientBytes = 0,
                    },
                    BeforeMemberSetChecksum = authority.ToImmutableArray(),
                    AfterMemberSetChecksum = authority.ToImmutableArray(),
                    BeforePublicationChecksum = authority.ToImmutableArray(),
                    AfterPublicationChecksum = authority.ToImmutableArray(),
                    EvidenceChecksum = authority.ToImmutableArray(),
                };
            }).ToImmutableArray();
        return BaseLogicalIndexProviderContract.SealReport(new BaseLogicalIndexCertificationReport
        {
            ProviderId = "hpd.base.test.logicalIndexes",
            ProviderVersion = 1,
            StoreProviderKind = "inmemory",
            StoreProviderProtocolVersion = HPDBaseStoreProviderFactory.ProtocolVersion,
            ProductionCapabilityChecksum = production.Checksum,
            BoundedCertificationCapabilityChecksum = BaseLogicalIndexProviderContract
                .BoundedCertificationCapability().Checksum,
            Cases = cases,
            ContractChecksum = BaseLogicalIndexProviderContract.ContractChecksum(),
            Checksum = [],
        });
    }
}
