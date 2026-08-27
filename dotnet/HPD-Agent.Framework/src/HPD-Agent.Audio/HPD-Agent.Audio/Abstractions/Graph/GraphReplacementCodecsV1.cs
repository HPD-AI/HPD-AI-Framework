using System.Buffers;
using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal static class GraphReplacementCodecsV1
{
    internal const string CommandOuterSchemaId = "hpd.authority-payload-graph-mutation-command.v1";
    internal const string InstalledOuterSchemaId = "hpd.authority-payload-graph-topology-installed-fact.v1";
    internal const string FactOuterSchemaId = "hpd.authority-payload-graph-replacement-fact.v1";
    internal const string InstalledSchemaId = "hpd.graph-topology-installed.v1";
    internal const string CommandSchemaId = "hpd.graph-replacement-command.v1";
    internal const string SnapshotSchemaId = "hpd.graph-replacement-snapshot.v1";
    internal const string FactSchemaId = "hpd.graph-replacement-fact.v1";
    internal const ushort Major = 1;
    internal const ushort Minor = 0;
    internal const int MaximumBodyBytes = 65_536;
    internal const int MaximumEncodedOuterBytes = 65_920;

    internal static byte[] EncodeCommand(GraphReplacementJournalCommandV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var body = Writer(); body.WriteStartMap(value switch
        { GraphReplacementJournalCommandV1.Prepare => 8, GraphReplacementJournalCommandV1.Commit => 2, _ => 3 });
        body.WriteUInt64(1); WriteOperation(body, value.OperationId);
        body.WriteUInt64(2); WritePosition(body, value.ExpectedPredecessor);
        switch (value)
        {
            case GraphReplacementJournalCommandV1.Prepare prepare:
                body.WriteUInt64(3); WriteHash(body, prepare.SourceFingerprint);
                body.WriteUInt64(4); body.WriteEncodedValue(GraphTopologyPlanCodecV1.Encode(prepare.TargetTopology));
                body.WriteUInt64(5); WritePosition(body, prepare.TargetGrantFact);
                body.WriteUInt64(6); body.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(prepare.CurrentAuthority));
                body.WriteUInt64(7); body.WriteEncodedValue(MonotonicStampV1Codec.Encode(prepare.ObservedAt));
                body.WriteUInt64(8); body.WriteEncodedValue(MonotonicStampV1Codec.Encode(prepare.OverlapDeadline));
                break;
            case GraphReplacementJournalCommandV1.SettleSource settle:
                body.WriteUInt64(3); WritePosition(body, settle.SourceSettlementFact); break;
        }
        body.WriteEndMap();
        var writer = Writer(); writer.WriteStartMap(2); writer.WriteUInt64(1); writer.WriteUInt64((ushort)value.Kind);
        writer.WriteUInt64(2); writer.WriteByteString(body.Encode()); writer.WriteEndMap(); return writer.Encode();
    }

    internal static bool TryDecodeCommand(ReadOnlyMemory<byte> encoded, out GraphReplacementJournalCommandV1? value) =>
        TryDecode(encoded, reader =>
        {
            RequireMap(reader, 2, 1); var kind = (GraphReplacementJournalCommandKindV1)ReadClosed(reader, 3);
            RequireTag(reader, 2); var bodyBytes = ReadBoundedBstr(reader, 65_536); reader.ReadEndMap();
            var body = Reader(bodyBytes); var count = body.ReadStartMap();
            if (count is null || body.ReadUInt64() != 1) throw Invalid();
            var operation = ReadOperation(body); RequireTag(body, 2); var predecessor = ReadPosition(body);
            GraphReplacementJournalCommandV1 result = kind switch
            {
                GraphReplacementJournalCommandKindV1.Prepare when count == 8 => ReadPrepare(body, operation, predecessor),
                GraphReplacementJournalCommandKindV1.Commit when count == 2 => new GraphReplacementJournalCommandV1.Commit(operation, predecessor),
                GraphReplacementJournalCommandKindV1.SettleSource when count == 3 => ReadSettle(body, operation, predecessor),
                _ => throw Invalid(),
            };
            body.ReadEndMap(); if (body.BytesRemaining != 0) throw Invalid(); return result;
        }, EncodeCommand, out value);

    internal static byte[] EncodeInstalled(GraphTopologyInstalledV1 value)
    {
        ValidateInstalled(value);
        var writer = Writer(); writer.WriteStartMap(5);
        writer.WriteUInt64(1); writer.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(value.Topology.Session));
        writer.WriteUInt64(2); writer.WriteEncodedValue(GraphTopologyPlanCodecV1.Encode(value.Topology));
        writer.WriteUInt64(3); WriteHash(writer, value.TopologyFingerprint);
        writer.WriteUInt64(4); WritePosition(writer, value.ActiveSourceGrantFact);
        writer.WriteUInt64(5); writer.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(value.CurrentAuthority));
        writer.WriteEndMap(); return writer.Encode();
    }

    internal static bool TryDecodeInstalled(ReadOnlyMemory<byte> encoded, out GraphTopologyInstalledV1? value) =>
        TryDecode(encoded, reader =>
        {
            RequireMap(reader, 5, 1); if (!SessionAuthorityStampV1Codec.TryDecode(reader.ReadEncodedValue(), out var session)) throw Invalid();
            RequireTag(reader, 2); if (!GraphTopologyPlanCodecV1.TryDecode(reader.ReadEncodedValue(), out var topology)) throw Invalid();
            RequireTag(reader, 3); var fingerprint = ReadHash(reader); RequireTag(reader, 4); var grant = ReadPosition(reader);
            RequireTag(reader, 5); if (!AuthorityVectorCodecsV1.TryDecodeVector(reader.ReadEncodedValue(), out var authority)) throw Invalid();
            reader.ReadEndMap();
            if (topology!.Session != session || topology.Fingerprint != fingerprint || grant.Session != session || authority!.Session != session) throw Invalid();
            var result = new GraphTopologyInstalledV1(topology, fingerprint, grant, authority);
            ValidateInstalled(result);
            return result;
        }, EncodeInstalled, out value);

    internal static byte[] EncodeFact(GraphReplacementFactV1 value)
    {
        ArgumentNullException.ThrowIfNull(value); ValidateFact(value);
        var writer = Writer(); writer.WriteStartMap(6);
        writer.WriteUInt64(1); WritePosition(writer, value.CommandFact);
        writer.WriteUInt64(2); WritePosition(writer, value.ExpectedPredecessor);
        writer.WriteUInt64(3); WritePosition(writer, value.ActualPredecessor);
        writer.WriteUInt64(4); writer.WriteUInt64((ushort)value.Outcome);
        writer.WriteUInt64(5); WriteSnapshot(writer, value.ResultingSnapshot);
        writer.WriteUInt64(6); WriteSafeCode(writer, value.SafeCode); writer.WriteEndMap(); return writer.Encode();
    }

    internal static bool TryDecodeFact(ReadOnlyMemory<byte> encoded, out GraphReplacementFactV1? value) =>
        TryDecode(encoded, reader =>
        {
            RequireMap(reader, 6, 1); var command = ReadPosition(reader); RequireTag(reader, 2); var expected = ReadPosition(reader);
            RequireTag(reader, 3); var actual = ReadPosition(reader); RequireTag(reader, 4);
            var outcome = (GraphReplacementJournalOutcomeV1)ReadClosed(reader, 6); RequireTag(reader, 5);
            var snapshot = ReadSnapshot(reader); RequireTag(reader, 6); var code = ReadSafeCode(reader); reader.ReadEndMap();
            var result = new GraphReplacementFactV1(command, expected, actual, outcome, snapshot, code); ValidateFact(result); return result;
        }, EncodeFact, out value);

    internal static byte[] EncodeOuter(GraphOwnerPayloadV1 value)
    {
        ArgumentNullException.ThrowIfNull(value); var writer = Writer(); writer.WriteStartMap(3);
        writer.WriteUInt64(1); writer.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(value.Session));
        writer.WriteUInt64(2); writer.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(value.ExpectedAuthority));
        writer.WriteUInt64(3); writer.WriteByteString(value.Body.Span); writer.WriteEndMap(); return writer.Encode();
    }

    internal static bool TryDecodeOuter(ReadOnlyMemory<byte> encoded, out GraphOwnerPayloadV1? value) =>
        TryDecode(encoded, reader =>
        {
            RequireMap(reader, 3, 1); if (!SessionAuthorityStampV1Codec.TryDecode(reader.ReadEncodedValue(), out var session)) throw Invalid();
            RequireTag(reader, 2); if (!AuthorityVectorCodecsV1.TryDecodeVector(reader.ReadEncodedValue(), out var authority)) throw Invalid();
            RequireTag(reader, 3); var body = ReadBoundedBstr(reader, MaximumBodyBytes); reader.ReadEndMap();
            return new GraphOwnerPayloadV1(session, authority!, body.Span);
        }, EncodeOuter, out value);

    internal static Hash256 Hash(string schema, ReadOnlySpan<byte> canonical) =>
        AuthorityIntegrityHashV1.Compute(schema, Major, Minor, canonical);
    internal static byte[] EncodeSnapshot(GraphReplacementSnapshotV1 value){var writer=Writer();WriteSnapshot(writer,value);return writer.Encode();}
    internal static Hash256 ComputeHash(GraphTopologyInstalledV1 value)=>Hash(InstalledSchemaId,EncodeInstalled(value));
    internal static Hash256 ComputeHash(GraphReplacementJournalCommandV1 value)=>Hash(CommandSchemaId,EncodeCommand(value));
    internal static Hash256 ComputeHash(GraphReplacementSnapshotV1 value)=>Hash(SnapshotSchemaId,EncodeSnapshot(value));
    internal static Hash256 ComputeHash(GraphReplacementFactV1 value)=>Hash(FactSchemaId,EncodeFact(value));

    private static GraphReplacementJournalCommandV1 ReadPrepare(CborReader body, OperationId operation, JournalPositionV1 predecessor)
    {
        RequireTag(body, 3); var source = ReadHash(body); RequireTag(body, 4);
        if (!GraphTopologyPlanCodecV1.TryDecode(body.ReadEncodedValue(), out var topology)) throw Invalid();
        RequireTag(body, 5); var grant = ReadPosition(body); RequireTag(body, 6);
        if (!AuthorityVectorCodecsV1.TryDecodeVector(body.ReadEncodedValue(), out var authority)) throw Invalid();
        RequireTag(body, 7); if (!MonotonicStampV1Codec.TryDecode(body.ReadEncodedValue(), out var observed)) throw Invalid();
        RequireTag(body, 8); if (!MonotonicStampV1Codec.TryDecode(body.ReadEncodedValue(), out var deadline)) throw Invalid();
        var session = predecessor.Session;
        if (topology!.Session != session || grant.Session != session || authority!.Session != session) throw Invalid();
        return new GraphReplacementJournalCommandV1.Prepare(operation, predecessor, source, topology, grant, authority, observed, deadline);
    }

    private static GraphReplacementJournalCommandV1 ReadSettle(CborReader body, OperationId operation, JournalPositionV1 predecessor)
    { RequireTag(body, 3); var settlement = ReadPosition(body); if (settlement.Session != predecessor.Session) throw Invalid(); return new GraphReplacementJournalCommandV1.SettleSource(operation, predecessor, settlement); }

    private static void WriteSnapshot(CborWriter writer, GraphReplacementSnapshotV1 value)
    {
        ValidateSnapshot(value); writer.WriteStartMap(9); writer.WriteUInt64(1); writer.WriteUInt64((ushort)value.Phase);
        writer.WriteUInt64(2); writer.WriteEncodedValue(GraphTopologyPlanCodecV1.Encode(value.SourceTopology));
        writer.WriteUInt64(3); WritePosition(writer, value.SourceGrantFact);
        writer.WriteUInt64(4); writer.WriteByteString(EncodeOptional(value.Target, WriteTarget));
        writer.WriteUInt64(5); writer.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(value.CurrentAuthority));
        writer.WriteUInt64(6); WritePosition(writer, value.LastGraphFact);
        writer.WriteUInt64(7); writer.WriteByteString(EncodeOptional(value.Replacement, WriteReplacement));
        writer.WriteUInt64(8); writer.WriteByteString(EncodeOptional(value.Commit, WriteCommit));
        writer.WriteUInt64(9); writer.WriteByteString(EncodeOptional(value.Settlement, WriteSettlement)); writer.WriteEndMap();
    }

    private static GraphReplacementSnapshotV1 ReadSnapshot(CborReader reader)
    {
        RequireMap(reader, 9, 1); var phase = (GraphReplacementPhaseV1)ReadClosed(reader, 4); RequireTag(reader, 2);
        if (!GraphTopologyPlanCodecV1.TryDecode(reader.ReadEncodedValue(), out var source)) throw Invalid();
        RequireTag(reader, 3); var sourceGrant = ReadPosition(reader); RequireTag(reader, 4);
        var target = ReadOptional(ReadBoundedBstr(reader, 65_536), ReadTarget); RequireTag(reader, 5);
        if (!AuthorityVectorCodecsV1.TryDecodeVector(reader.ReadEncodedValue(), out var authority)) throw Invalid();
        RequireTag(reader, 6); var last = ReadPosition(reader); RequireTag(reader, 7);
        var replacement = ReadOptional(ReadBoundedBstr(reader, 65_536), ReadReplacement); RequireTag(reader, 8);
        var commit = ReadOptional(ReadBoundedBstr(reader, 4_096), ReadCommit); RequireTag(reader, 9);
        var settlement = ReadOptional(ReadBoundedBstr(reader, 4_096), ReadSettlement); reader.ReadEndMap();
        var result = new GraphReplacementSnapshotV1(phase, source!, sourceGrant, target, authority!, last, replacement, commit, settlement);
        ValidateSnapshot(result); return result;
    }

    private static byte[] EncodeOptional<T>(T? value, Action<CborWriter,T> write) where T : class
    { var writer=Writer(); writer.WriteStartMap(2); writer.WriteUInt64(1); writer.WriteUInt64(value is null?0UL:1UL); writer.WriteUInt64(2); if(value is null)writer.WriteByteString([]);else write(writer,value); writer.WriteEndMap(); return writer.Encode(); }
    private static T? ReadOptional<T>(ReadOnlyMemory<byte> bytes, Func<CborReader,T> read) where T : class
    { var reader=Reader(bytes); if(reader.ReadStartMap()!=2||reader.ReadUInt64()!=1)throw Invalid();var kind=reader.ReadUInt64();if(reader.ReadUInt64()!=2)throw Invalid();T? value;if(kind==0){if(reader.ReadByteString().Length!=0)throw Invalid();value=null;}else if(kind==1)value=read(reader);else throw Invalid();reader.ReadEndMap();if(reader.BytesRemaining!=0)throw Invalid();return value; }

    private static void WriteTarget(CborWriter w, GraphReplacementTargetArmV1 v){w.WriteStartMap(2);w.WriteUInt64(1);w.WriteEncodedValue(GraphTopologyPlanCodecV1.Encode(v.Topology));w.WriteUInt64(2);WritePosition(w,v.GrantFact);w.WriteEndMap();}
    private static GraphReplacementTargetArmV1 ReadTarget(CborReader r){RequireMap(r,2,1);if(!GraphTopologyPlanCodecV1.TryDecode(r.ReadEncodedValue(),out var p))throw Invalid();RequireTag(r,2);var f=ReadPosition(r);r.ReadEndMap();return new(p!,f);}
    private static void WriteReplacement(CborWriter w, GraphReplacementIdentityArmV1 v){w.WriteStartMap(2);w.WriteUInt64(1);WriteOperation(w,v.OperationId);w.WriteUInt64(2);WritePosition(w,v.PrepareCommandFact);w.WriteEndMap();}
    private static GraphReplacementIdentityArmV1 ReadReplacement(CborReader r){RequireMap(r,2,1);var o=ReadOperation(r);RequireTag(r,2);var f=ReadPosition(r);r.ReadEndMap();return new(o,f);}
    private static void WriteCommit(CborWriter w, GraphReplacementCommitArmV1 v){w.WriteStartMap(2);w.WriteUInt64(1);WritePosition(w,v.CommitCommandFact);w.WriteUInt64(2);WritePosition(w,v.GenerationChangedFact);w.WriteEndMap();}
    private static GraphReplacementCommitArmV1 ReadCommit(CborReader r){RequireMap(r,2,1);var c=ReadPosition(r);RequireTag(r,2);var g=ReadPosition(r);r.ReadEndMap();return new(c,g);}
    private static void WriteSettlement(CborWriter w, GraphReplacementSettlementArmV1 v){w.WriteStartMap(2);w.WriteUInt64(1);WritePosition(w,v.SettleCommandFact);w.WriteUInt64(2);WritePosition(w,v.SourceSettlementFact);w.WriteEndMap();}
    private static GraphReplacementSettlementArmV1 ReadSettlement(CborReader r){RequireMap(r,2,1);var c=ReadPosition(r);RequireTag(r,2);var s=ReadPosition(r);r.ReadEndMap();return new(c,s);}

    private static void ValidateSnapshot(GraphReplacementSnapshotV1 value)
    {
        ArgumentNullException.ThrowIfNull(value); var session=value.SourceTopology?.Session ?? default;
        if(!session.IsValid||value.SourceGrantFact.Session!=session||value.CurrentAuthority is null||value.CurrentAuthority.Session!=session||value.LastGraphFact.Session!=session)throw Invalid();
        if(value.Target is { } t&&(t.Topology.Session!=session||t.GrantFact.Session!=session)||value.Replacement is { } r&&r.PrepareCommandFact.Session!=session||value.Commit is { } c&&(c.CommitCommandFact.Session!=session||c.GenerationChangedFact.Session!=session)||value.Settlement is { } s&&(s.SettleCommandFact.Session!=session||s.SourceSettlementFact.Session!=session))throw Invalid();
        var valid=value.Phase switch{GraphReplacementPhaseV1.None=>value.Target is null&&value.Replacement is null&&value.Commit is null&&value.Settlement is null,GraphReplacementPhaseV1.Prepared=>value.Target is not null&&value.Replacement is not null&&value.Commit is null&&value.Settlement is null,GraphReplacementPhaseV1.Committed=>value.Target is not null&&value.Replacement is not null&&value.Commit is not null&&value.Settlement is null,GraphReplacementPhaseV1.SourceSettled=>value.Target is not null&&value.Replacement is not null&&value.Commit is not null&&value.Settlement is not null,_=>false};if(!valid)throw Invalid();
    }

    private static void ValidateInstalled(GraphTopologyInstalledV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Topology is null || !value.Topology.Session.IsValid ||
            value.TopologyFingerprint != value.Topology.Fingerprint ||
            value.ActiveSourceGrantFact.Session != value.Topology.Session ||
            value.CurrentAuthority is null || value.CurrentAuthority.Session != value.Topology.Session)
            throw Invalid();
    }

    private static void ValidateFact(GraphReplacementFactV1 value)
    { var session=value.CommandFact.Session;if(!session.IsValid||!Enum.IsDefined(value.Outcome)||value.ExpectedPredecessor.Session!=session||value.ActualPredecessor.Session!=session||value.ResultingSnapshot is null||value.ResultingSnapshot.SourceTopology.Session!=session)throw Invalid(); var success=value.Outcome is GraphReplacementJournalOutcomeV1.Prepared or GraphReplacementJournalOutcomeV1.Committed or GraphReplacementJournalOutcomeV1.SourceSettled;if(success!=(value.SafeCode is null))throw Invalid();if(value.Outcome==GraphReplacementJournalOutcomeV1.GenerationReplaced&&value.SafeCode?.ToString()!="generation-replaced")throw Invalid(); }
    private static void WriteSafeCode(CborWriter w,BoundedAscii? v){w.WriteStartMap(2);w.WriteUInt64(1);w.WriteUInt64(v is null?0UL:1UL);w.WriteUInt64(2);if(v is null)w.WriteByteString([]);else BoundedAsciiCodec.Write(w,v.Value);w.WriteEndMap();}
    private static BoundedAscii? ReadSafeCode(CborReader r){if(r.ReadStartMap()!=2||r.ReadUInt64()!=1)throw Invalid();var k=r.ReadUInt64();if(r.ReadUInt64()!=2)throw Invalid();BoundedAscii? v;if(k==0){if(r.ReadByteString().Length!=0)throw Invalid();v=null;}else if(k==1)v=BoundedAsciiCodec.Read(r);else throw Invalid();r.ReadEndMap();return v;}
    private static void WriteOperation(CborWriter w,OperationId v)=>WriteId(w,v.TryWriteBytes);
    private static OperationId ReadOperation(CborReader r)=>OperationId.FromValue(ReadId(r));
    private static void WriteHash(CborWriter w,Hash256 v){Span<byte>b=stackalloc byte[32];if(!v.TryWriteBytes(b))throw Invalid();w.WriteByteString(b);}
    private static Hash256 ReadHash(CborReader r){Span<byte>b=stackalloc byte[32];if(!r.TryReadByteString(b,out var n)||n!=32)throw Invalid();return Hash256.FromBytes(b);}
    private static void WritePosition(CborWriter w,JournalPositionV1 v)=>w.WriteEncodedValue(AuthorityPositionCodecsV1.Encode(v));
    private static JournalPositionV1 ReadPosition(CborReader r){if(!AuthorityPositionCodecsV1.TryDecodeJournal(r.ReadEncodedValue(),out var v))throw Invalid();return v;}
    private delegate bool IdWriter(Span<byte> destination);
    private static void WriteId(CborWriter w,IdWriter write){Span<byte>b=stackalloc byte[16];if(!write(b))throw Invalid();w.WriteByteString(b);}
    private static StableId128 ReadId(CborReader r){Span<byte>b=stackalloc byte[16];if(!r.TryReadByteString(b,out var n)||n!=16||b.IndexOfAnyExcept((byte)0)<0)throw Invalid();return StableId128.FromBytes(b);}
    private static ReadOnlyMemory<byte> ReadBoundedBstr(CborReader r,int max)
    {
        var rented=ArrayPool<byte>.Shared.Rent(max);
        try
        {
            if(!r.TryReadByteString(rented.AsSpan(0,max),out var written)||written is 0||written>max)throw Invalid();
            return rented.AsMemory(0,written).ToArray();
        }
        finally { ArrayPool<byte>.Shared.Return(rented); }
    }
    private static ushort ReadClosed(CborReader r,ushort max){var v=r.ReadUInt64();if(v is 0||v>max)throw Invalid();return(ushort)v;}
    private static CborWriter Writer()=>new(CborConformanceMode.Ctap2Canonical);
    private static CborReader Reader(ReadOnlyMemory<byte>b)=>new(b,CborConformanceMode.Ctap2Canonical,false);
    private static void RequireMap(CborReader r,int count,ulong first){if(r.ReadStartMap()!=count||r.ReadUInt64()!=first)throw Invalid();}
    private static void RequireTag(CborReader r,ulong tag){if(r.ReadUInt64()!=tag)throw Invalid();}
    private static CborContentException Invalid()=>new("Invalid canonical graph replacement payload.");
    private static bool TryDecode<T>(ReadOnlyMemory<byte> encoded,Func<CborReader,T> read,Func<T,byte[]> encode,out T? value) where T:class
    {value=null;if(encoded.Length is 0 or>MaximumEncodedOuterBytes)return false;try{var r=Reader(encoded);var v=read(r);if(r.BytesRemaining!=0||!encode(v).AsSpan().SequenceEqual(encoded.Span))return false;value=v;return true;}catch(Exception e)when(e is CborContentException or InvalidOperationException or ArgumentException or OverflowException){return false;}}
}

internal static class GraphReplacementPayloadRegistrationsV1
{
    internal static AuthorityPayloadRegistrationV1 Command { get; } = Register(GraphReplacementCodecsV1.CommandOuterSchemaId,
        static (body, session) => GraphReplacementCodecsV1.TryDecodeOuter(body, out var outer) && outer!.Session == session && HasGraph(outer.ExpectedAuthority) &&
            GraphReplacementCodecsV1.TryDecodeCommand(outer.Body, out var command) && command!.ExpectedPredecessor.Session == outer.Session &&
            (command is not GraphReplacementJournalCommandV1.Prepare prepare || prepare.CurrentAuthority == outer.ExpectedAuthority));
    internal static AuthorityPayloadRegistrationV1 Installed { get; } = Register(GraphReplacementCodecsV1.InstalledOuterSchemaId,
        static (body, session) => GraphReplacementCodecsV1.TryDecodeOuter(body, out var outer) && outer!.Session == session && HasGraph(outer.ExpectedAuthority) &&
            GraphReplacementCodecsV1.TryDecodeInstalled(outer.Body, out var installed) && installed!.CurrentAuthority == outer.ExpectedAuthority &&
            GraphReplacementReducerV1.HasExactGraph(installed.CurrentAuthority, installed.Topology.GraphGeneration));
    internal static AuthorityPayloadRegistrationV1 Fact { get; } = Register(GraphReplacementCodecsV1.FactOuterSchemaId,
        static (body, session) => GraphReplacementCodecsV1.TryDecodeOuter(body, out var outer) && outer!.Session == session && HasGraph(outer.ExpectedAuthority) &&
            GraphReplacementCodecsV1.TryDecodeFact(outer.Body, out var fact) && fact!.CommandFact.Session == outer.Session);
    private static bool HasGraph(ExpectedAuthorityVectorV1 authority) =>
        authority.Axes.Count(static entry => entry.AxisId == AuthorityAxisId.Graph) == 1;
    private static AuthorityPayloadRegistrationV1 Register(string token,Func<ReadOnlyMemory<byte>,SessionAuthorityStampV1,bool> validator)=>
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(new BoundedAscii(token),1,0,OwnerSliceId.S2,GraphReplacementCodecsV1.MaximumEncodedOuterBytes,validator);
}
