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
    BaseSubjectId SubjectId,
    BaseSubjectAuthorityEpoch AuthorityEpoch,
    BaseSubjectIncarnation Incarnation,
    long ThroughSequence,
    BaseSubjectRequiredBarrierExpectation? Barrier,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);

internal sealed class BaseSubjectRetirementEvidenceCodec(BaseOpaqueTokenProtector tokens, TimeProvider timeProvider)
{
    private const byte Version = 1;

    internal byte[] Protect(BaseSubjectRetirementEvidencePayload payload, ReadOnlySpan<byte> binding)
    {
        string purpose = Purpose(payload.Participation);
        return Encoding.ASCII.GetBytes(tokens.Protect(purpose, Version, Encode(payload), binding));
    }

    internal bool TryRead(ReadOnlySpan<byte> encoded, BaseSubjectRetirementParticipation expected, ReadOnlySpan<byte> binding, BaseSubjectIdKind idKind, out BaseSubjectRetirementEvidencePayload? payload)
    {
        payload = null;
        if (encoded.Length is < 1 or > 2048) return false;
        BaseOpaqueTokenResult result = tokens.Unprotect(Purpose(expected), Version, Encoding.ASCII.GetString(encoded), 80, 2048, binding);
        if (result.Status != BaseOpaqueTokenStatus.Valid || result.Plaintext is null) return false;
        try
        {
            payload = Decode(result.Plaintext, idKind);
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (payload.Participation != expected || payload.IssuedAtUtc > now || payload.ExpiresAtUtc <= now) { payload = null; return false; }
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
        writer.Write((byte)value.Participation); Write(writer,value.ConsumerId);writer.Write(value.ConsumerVersion);Write(writer,value.ConsumerChecksum);Write(writer,value.ContractId);writer.Write(value.ContractVersion);
        Write(writer,value.SubjectId.Value);writer.Write(value.AuthorityEpoch.ToArray());writer.Write(value.Incarnation.ToArray());writer.Write(value.ThroughSequence);writer.Write(value.IssuedAtUtc.UtcTicks);writer.Write(value.ExpiresAtUtc.UtcTicks);
        writer.Write(value.Barrier is not null);if(value.Barrier is { } barrier){writer.Write(barrier.Generation);Write(writer,barrier.Checksum);}writer.Flush();return stream.ToArray();
    }

    private static BaseSubjectRetirementEvidencePayload Decode(byte[] bytes, BaseSubjectIdKind idKind)
    {
        using var stream=new MemoryStream(bytes,false);using var reader=new BinaryReader(stream,Encoding.UTF8,true);
        var participation=(BaseSubjectRetirementParticipation)reader.ReadByte();string consumer=Read(reader);int consumerVersion=reader.ReadInt32();string consumerChecksum=Read(reader);string contract=Read(reader);int contractVersion=reader.ReadInt32();
        BaseSubjectId subject=BaseSubjectId.Create(Read(reader),idKind);var epoch=new BaseSubjectAuthorityEpoch(reader.ReadBytes(16));var incarnation=new BaseSubjectIncarnation(reader.ReadBytes(24));long sequence=reader.ReadInt64();var issued=new DateTimeOffset(reader.ReadInt64(),TimeSpan.Zero);var expires=new DateTimeOffset(reader.ReadInt64(),TimeSpan.Zero);
        BaseSubjectRequiredBarrierExpectation? barrier=reader.ReadBoolean()?new(){Generation=reader.ReadInt64(),Checksum=Read(reader)}:null;
        if(stream.Position!=stream.Length||consumerVersion<1||contractVersion<1||sequence<1||!Enum.IsDefined(participation))throw new FormatException();
        return new(participation,consumer,consumerVersion,consumerChecksum,contract,contractVersion,subject,epoch,incarnation,sequence,barrier,issued,expires);
    }

    private static void Write(BinaryWriter writer,string value){byte[] bytes=Encoding.UTF8.GetBytes(value);writer.Write(bytes.Length);writer.Write(bytes);}
    private static string Read(BinaryReader reader){int length=reader.ReadInt32();if(length is <1 or >512)throw new FormatException();byte[] bytes=reader.ReadBytes(length);if(bytes.Length!=length)throw new EndOfStreamException();return Encoding.UTF8.GetString(bytes);}
}
