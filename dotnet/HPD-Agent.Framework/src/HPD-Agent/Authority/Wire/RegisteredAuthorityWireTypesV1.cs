namespace HPD.Agent.Authority;

internal enum DataClassificationV1 : ushort { Public=1,Internal=2,Confidential=3,Restricted=4,Secret=5 }
internal enum RetentionBasisV1 : ushort { Purpose=1,Contract=2,Legal=3,Settlement=4,NonReplay=5 }
internal enum TfmId : ushort { Net8=8,Net9=9,Net10=10 }
internal enum RidId : ushort { OsxArm64=1,OsxX64=2,LinuxArm64=3,LinuxX64=4,WinArm64=5,WinX64=6 }
internal enum QualificationDeclarationV1 : ushort { AdvertisedPositive=1,AdvertisedTypedNegative=2,NotAdvertised=3 }
internal enum EmulationKindV1 : ushort { None=0,Rosetta=1,Qemu=2,OtherRegistered=3 }

internal abstract record CapacitySubjectValueV1
{
    private CapacitySubjectValueV1(){}
    internal sealed record StableId : CapacitySubjectValueV1
    { internal StableId(StableId128 value){Span<byte>b=stackalloc byte[16];if(!value.TryWriteBytes(b))throw new ArgumentException("A stable subject identity is required.");Value=value;}internal StableId128 Value{get;} }
    internal sealed record OwnerSlice : CapacitySubjectValueV1
    { internal OwnerSlice(OwnerSliceId value){if(!Enum.IsDefined(value))throw new ArgumentException("A registered owner slice is required.");Value=value;}internal OwnerSliceId Value{get;} }
}

internal sealed record FactRangeV1
{
    internal FactRangeV1(JournalPositionV1 first,JournalPositionV1 last){if(!first.IsValid||!last.IsValid||first.Session!=last.Session||first.Sequence>last.Sequence)throw new ArgumentException("Invalid fact range.");First=first;Last=last;}
    internal JournalPositionV1 First{get;} internal JournalPositionV1 Last{get;}
}
internal sealed record ResidencyRuleV1
{
    internal ResidencyRuleV1(IEnumerable<BoundedAscii> regions,bool crossRegionTransfer){ArgumentNullException.ThrowIfNull(regions);var a=regions.ToArray();if(a.Length>256||a.Any(x=>!x.IsValid||x.ToString().Length>32)||!Strict(a))throw new ArgumentException("Invalid residency regions.");AllowedRegions=Array.AsReadOnly(a);CrossRegionTransfer=crossRegionTransfer;}
    internal IReadOnlyList<BoundedAscii> AllowedRegions{get;} internal bool CrossRegionTransfer{get;}
    private static bool Strict(BoundedAscii[] a){for(var i=1;i<a.Length;i++)if(a[i-1].CompareTo(a[i])>=0)return false;return true;}
}
internal sealed record RetentionIntervalV1
{
    internal RetentionIntervalV1(UtcInstant minimumUntil,UtcInstant maximumUntil,RetentionBasisV1 basis){if(minimumUntil.NanosecondsSinceUnixEpoch>maximumUntil.NanosecondsSinceUnixEpoch||!Enum.IsDefined(basis))throw new ArgumentException("Invalid retention interval.");MinimumUntil=minimumUntil;MaximumUntil=maximumUntil;Basis=basis;}
    internal UtcInstant MinimumUntil{get;} internal UtcInstant MaximumUntil{get;} internal RetentionBasisV1 Basis{get;}
}
internal sealed record SchemaVersionV1
{
    internal SchemaVersionV1(ushort major,ushort minor){if(major==0)throw new ArgumentOutOfRangeException(nameof(major));Major=major;Minor=minor;} internal ushort Major{get;}internal ushort Minor{get;}
}
internal sealed record EvidenceReferenceV1
{
    internal EvidenceReferenceV1(ContentId contentId,Hash256 sha256,ulong byteLength,BoundedAscii mediaType){if(!contentId.IsValid||!HashValid(sha256)||!mediaType.IsValid||mediaType.ToString().Length>128)throw new ArgumentException("Invalid evidence reference.");ContentId=contentId;Sha256=sha256;ByteLength=byteLength;MediaType=mediaType;}
    internal ContentId ContentId{get;}internal Hash256 Sha256{get;}internal ulong ByteLength{get;}internal BoundedAscii MediaType{get;}
    private static bool HashValid(Hash256 h){Span<byte>b=stackalloc byte[32];return h.TryWriteBytes(b);}
}
internal sealed record ParticipantDescriptorV1
{
    internal ParticipantDescriptorV1(ParticipantId participantId,OwnerSliceId owner,SchemaReferenceV1 descriptorSchema,IEnumerable<ParticipantId> dependencies,Hash256 descriptorHash){ArgumentNullException.ThrowIfNull(dependencies);var a=dependencies.ToArray();if(!participantId.IsValid||!Enum.IsDefined(owner)||!descriptorSchema.IsValid||a.Length>256||a.Any(x=>!x.IsValid)||!Strict(a)||!HashValid(descriptorHash))throw new ArgumentException("Invalid participant descriptor.");ParticipantId=participantId;Owner=owner;DescriptorSchema=descriptorSchema;Dependencies=Array.AsReadOnly(a);DescriptorHash=descriptorHash;}
    internal ParticipantId ParticipantId{get;}internal OwnerSliceId Owner{get;}internal SchemaReferenceV1 DescriptorSchema{get;}internal IReadOnlyList<ParticipantId> Dependencies{get;}internal Hash256 DescriptorHash{get;}
    private static bool Strict(ParticipantId[] a){Span<byte>x=stackalloc byte[16];Span<byte>y=stackalloc byte[16];for(var i=1;i<a.Length;i++){a[i-1].TryWriteBytes(x);a[i].TryWriteBytes(y);if(x.SequenceCompareTo(y)>=0)return false;}return true;}private static bool HashValid(Hash256 h){Span<byte>b=stackalloc byte[32];return h.TryWriteBytes(b);}
}
internal sealed record LoweredConstraintSetV1
{
    private readonly byte[] _bytes;
    internal LoweredConstraintSetV1(SchemaReferenceV1 schema,ReadOnlySpan<byte> canonicalBytes,Hash256 hash){Span<byte>b=stackalloc byte[32];if(!schema.IsValid||canonicalBytes.Length>65_536||!hash.TryWriteBytes(b)||Hash256.Compute(canonicalBytes)!=hash)throw new ArgumentException("Invalid lowered constraints.");Schema=schema;_bytes=canonicalBytes.ToArray();CanonicalBytes=Array.AsReadOnly(_bytes);Hash=hash;}
    internal SchemaReferenceV1 Schema{get;}internal IReadOnlyList<byte> CanonicalBytes{get;}internal ReadOnlySpan<byte> Bytes=>_bytes;internal Hash256 Hash{get;}
}
internal sealed record CapacityChargeTemplateV1
{
    internal CapacityChargeTemplateV1(ushort dimensionId,CapacityScopeKindV1 scopeKind,long maximumAmount,CapacityPurposeId purpose){if(dimensionId==0||!Enum.IsDefined(scopeKind)||maximumAmount<=0||!purpose.IsValid)throw new ArgumentException("Invalid capacity charge template.");DimensionId=dimensionId;ScopeKind=scopeKind;MaximumAmount=maximumAmount;Purpose=purpose;}
    internal ushort DimensionId{get;}internal CapacityScopeKindV1 ScopeKind{get;}internal long MaximumAmount{get;}internal CapacityPurposeId Purpose{get;}
}
