using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal sealed record BaseSubjectRetirementEvidencePayload(
    BaseSubjectRetirementParticipation Participation,
    string ConsumerId,
    int ConsumerVersion,
    string ConsumerChecksum,
    string ContractId,
    int ContractVersion,
    string ContractChecksum,
    string StoreInstanceId,
    long RestoreEpoch,
    long DeliveryEpoch,
    long ProjectionGeneration,
    long CheckpointGeneration,
    BaseSubjectLifecycleOrderingBoundary OrderingBoundary,
    BaseSubjectId SubjectId,
    BaseSubjectAuthorityEpoch AuthorityEpoch,
    BaseSubjectIncarnation Incarnation,
    long ThroughSequence,
    byte[] FactChecksum,
    byte[] MembershipChecksum,
    byte[] GrantAuthorityDigest,
    byte AllowedDispositions,
    byte TokenKeyId,
    BaseSubjectRequiredBarrierExpectation? Barrier,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);

internal sealed class BaseSubjectRetirementEvidenceCodec(BaseOpaqueTokenProtector tokens, TimeProvider timeProvider)
{
    private const byte Version = 2;

    internal byte[] Protect(BaseSubjectRetirementEvidencePayload payload, ReadOnlySpan<byte> binding)
    {
        string purpose = Purpose(payload.Participation);
        if(payload.TokenKeyId!=tokens.ActiveKeyId)throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ContractInvalid);return Encoding.ASCII.GetBytes(tokens.Protect(purpose, Version, Encode(payload), binding));
    }

    internal bool TryRead(ReadOnlySpan<byte> encoded, BaseSubjectRetirementParticipation expected, ReadOnlySpan<byte> binding, BaseSubjectIdKind idKind, out BaseSubjectRetirementEvidencePayload? payload)
    {
        payload = null;
        if (encoded.Length is < 1 or > 2048) return false;
        string tokenText=Encoding.ASCII.GetString(encoded);BaseOpaqueTokenResult result = tokens.Unprotect(Purpose(expected), Version, tokenText, 80, 2048, binding);
        if (result.Status != BaseOpaqueTokenStatus.Valid || result.Plaintext is null) return false;
        try
        {
            payload = Decode(result.Plaintext, idKind);
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (payload.Participation != expected || payload.IssuedAtUtc > now || payload.ExpiresAtUtc <= now||!tokens.HasEncodedKeyId(tokenText,payload.TokenKeyId)) { payload = null; return false; }
            return true;
        }
        catch { payload = null; return false; }
    }

    internal static byte[] Binding(string applicationId, BaseInstalledSubjectRetirementConsumer installed, BaseOwnedSubjectScopeEvidence scope) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"base.subjectRetirement.evidence.binding.v1\0{applicationId}\0{installed.Definition.OwningModuleId}\0{installed.Definition.ConsumerId}\0{installed.Definition.ConsumerVersion}\0{installed.Checksum}\0{installed.Definition.RetirementProfileId}\0{installed.Definition.RetirementProfileVersion}\0{installed.Definition.RetirementProfileChecksum}\0{(int)scope.Kind}\0{scope.Value}"));

    private static string Purpose(BaseSubjectRetirementParticipation participation) => participation switch
    {
        BaseSubjectRetirementParticipation.AdvisoryAcknowledgement => "hpd.base.subject-retirement.advisory-evidence.v1",
        BaseSubjectRetirementParticipation.RequiredBeforePurge => "hpd.base.subject-retirement.required-evidence.v1",
        _ => throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ContractInvalid),
    };

    private static byte[] Encode(BaseSubjectRetirementEvidencePayload value)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((byte)value.Participation); Write(writer,value.ConsumerId);writer.Write(value.ConsumerVersion);Write(writer,value.ConsumerChecksum);Write(writer,value.ContractId);writer.Write(value.ContractVersion);Write(writer,value.ContractChecksum);Write(writer,value.StoreInstanceId);writer.Write(value.RestoreEpoch);writer.Write(value.DeliveryEpoch);writer.Write(value.ProjectionGeneration);writer.Write(value.CheckpointGeneration);
        writer.Write(value.OrderingBoundary.CommitPosition.Value);Write(writer,value.OrderingBoundary.SubjectId.Value);writer.Write(value.OrderingBoundary.AuthorityEpoch.ToArray());writer.Write(value.OrderingBoundary.Incarnation.ToArray());writer.Write(value.OrderingBoundary.SubjectSequence);Write(writer,value.SubjectId.Value);writer.Write(value.AuthorityEpoch.ToArray());writer.Write(value.Incarnation.ToArray());writer.Write(value.ThroughSequence);WriteBytes(writer,value.FactChecksum);WriteBytes(writer,value.MembershipChecksum);WriteBytes(writer,value.GrantAuthorityDigest);writer.Write(value.AllowedDispositions);writer.Write(value.TokenKeyId);writer.Write(value.IssuedAtUtc.UtcTicks);writer.Write(value.ExpiresAtUtc.UtcTicks);
        writer.Write(value.Barrier is not null);if(value.Barrier is { } barrier){writer.Write(barrier.Generation);Write(writer,barrier.Checksum);}writer.Flush();return stream.ToArray();
    }

    private static BaseSubjectRetirementEvidencePayload Decode(byte[] bytes, BaseSubjectIdKind idKind)
    {
        using var stream=new MemoryStream(bytes,false);using var reader=new BinaryReader(stream,Encoding.UTF8,true);
        var participation=(BaseSubjectRetirementParticipation)reader.ReadByte();string consumer=Read(reader);int consumerVersion=reader.ReadInt32();string consumerChecksum=Read(reader);string contract=Read(reader);int contractVersion=reader.ReadInt32();string contractChecksum=Read(reader);string store=Read(reader);long restoreEpoch=reader.ReadInt64();long deliveryEpoch=reader.ReadInt64();long projectionGeneration=reader.ReadInt64();long checkpointGeneration=reader.ReadInt64();
        var boundary=new BaseSubjectLifecycleOrderingBoundary{CommitPosition=new(reader.ReadInt64()),SubjectId=BaseSubjectId.Create(Read(reader),idKind),AuthorityEpoch=new(reader.ReadBytes(16)),Incarnation=new(reader.ReadBytes(24)),SubjectSequence=reader.ReadInt64()};BaseSubjectId subject=BaseSubjectId.Create(Read(reader),idKind);var epoch=new BaseSubjectAuthorityEpoch(reader.ReadBytes(16));var incarnation=new BaseSubjectIncarnation(reader.ReadBytes(24));long sequence=reader.ReadInt64();byte[] factChecksum=ReadBytes(reader,32);byte[] membershipChecksum=ReadBytes(reader,32);byte[] grantDigest=ReadBytes(reader,32);byte dispositions=reader.ReadByte();byte tokenKeyId=reader.ReadByte();var issued=new DateTimeOffset(reader.ReadInt64(),TimeSpan.Zero);var expires=new DateTimeOffset(reader.ReadInt64(),TimeSpan.Zero);
        BaseSubjectRequiredBarrierExpectation? barrier=reader.ReadBoolean()?new(){Generation=reader.ReadInt64(),Checksum=Read(reader)}:null;
        if(stream.Position!=stream.Length||consumerVersion<1||contractVersion<1||restoreEpoch<0||deliveryEpoch<1||projectionGeneration<1||checkpointGeneration<0||boundary.CommitPosition.Value<=0||boundary.SubjectSequence<1||sequence<1||!boundary.SubjectId.Equals(subject)||!boundary.AuthorityEpoch.Equals(epoch)||!boundary.Incarnation.Equals(incarnation)||boundary.SubjectSequence!=sequence||dispositions is 0 or >3||!Enum.IsDefined(participation))throw new FormatException();
        return new(participation,consumer,consumerVersion,consumerChecksum,contract,contractVersion,contractChecksum,store,restoreEpoch,deliveryEpoch,projectionGeneration,checkpointGeneration,boundary,subject,epoch,incarnation,sequence,factChecksum,membershipChecksum,grantDigest,dispositions,tokenKeyId,barrier,issued,expires);
    }

    private static void Write(BinaryWriter writer,string value){byte[] bytes=Encoding.UTF8.GetBytes(value);writer.Write(bytes.Length);writer.Write(bytes);}
    private static void WriteBytes(BinaryWriter writer,byte[] value){writer.Write(value.Length);writer.Write(value);}
    private static string Read(BinaryReader reader){int length=reader.ReadInt32();if(length is <1 or >512)throw new FormatException();byte[] bytes=reader.ReadBytes(length);if(bytes.Length!=length)throw new EndOfStreamException();return Encoding.UTF8.GetString(bytes);}
    private static byte[] ReadBytes(BinaryReader reader,int expected){int length=reader.ReadInt32();if(length!=expected)throw new FormatException();byte[] bytes=reader.ReadBytes(length);if(bytes.Length!=length)throw new EndOfStreamException();return bytes;}
}
