using System.Formats.Cbor;

namespace HPD.Agent.Authority;

internal static class SessionLifecycleBodyCodecsV1
{
    internal const int MaximumCommandBytes = 87;
    internal const int MaximumFactBytes = 276;

    internal static byte[] Encode(SessionLifecycleCommandBodyV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(4);
        writer.WriteUInt64(1); writer.WriteUInt64((ushort)value.Kind);
        writer.WriteUInt64(2); WriteOperation(writer, value.OperationId);
        writer.WriteUInt64(3); WritePositionOption(writer, value.ExpectedLifecycleFact);
        writer.WriteUInt64(4); WriteCommandPayload(writer, value);
        writer.WriteEndMap();
        return writer.Encode();
    }

    internal static bool TryDecodeCommand(ReadOnlyMemory<byte> encoded, out SessionLifecycleCommandBodyV1? value)
    {
        value = null;
        if (encoded.Length > MaximumCommandBytes) return false;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 4 || reader.ReadUInt64() != 1) return false;
            var kind = (SessionLifecycleCommandKindV1)ReadClosed(reader, 6, allowZero: false);
            if (reader.ReadUInt64() != 2) return false;
            var operation = ReadOperation(reader);
            if (reader.ReadUInt64() != 3) return false;
            var expected = ReadPositionOption(reader);
            if (reader.ReadUInt64() != 4) return false;
            value = ReadCommandPayload(reader, kind, operation, expected);
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0) { value = null; return false; }
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        {
            value = null;
            return false;
        }
    }

    internal static byte[] Encode(SessionLifecycleFactBodyV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(7);
        writer.WriteUInt64(1); WriteOperation(writer, value.OperationId);
        writer.WriteUInt64(2); AuthorityPositionCodecsV1.Write(writer, value.CommandPosition);
        writer.WriteUInt64(3); WritePositionOption(writer, value.CommandExpectedLifecycleFact);
        writer.WriteUInt64(4); WritePositionOption(writer, value.PreviousLifecycleFact);
        writer.WriteUInt64(5); writer.WriteUInt64((ushort)value.Outcome);
        writer.WriteUInt64(6); WriteSnapshot(writer, value.Snapshot);
        writer.WriteUInt64(7); WriteSafeCodeOption(writer, value.SafeCode);
        writer.WriteEndMap();
        return writer.Encode();
    }

    internal static bool TryDecodeFact(ReadOnlyMemory<byte> encoded, out SessionLifecycleFactBodyV1? value)
    {
        value = null;
        if (encoded.Length > MaximumFactBytes) return false;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 7 || reader.ReadUInt64() != 1) return false;
            var operation = ReadOperation(reader);
            if (reader.ReadUInt64() != 2) return false;
            var commandPosition = AuthorityPositionCodecsV1.ReadJournal(reader);
            if (reader.ReadUInt64() != 3) return false;
            var commandExpected = ReadPositionOption(reader);
            if (reader.ReadUInt64() != 4) return false;
            var previous = ReadPositionOption(reader);
            if (reader.ReadUInt64() != 5) return false;
            var outcome = (SessionLifecycleOutcomeV1)ReadClosed(reader, 3, allowZero: false);
            if (reader.ReadUInt64() != 6) return false;
            var snapshot = ReadSnapshot(reader);
            if (reader.ReadUInt64() != 7) return false;
            var safeCode = ReadSafeCodeOption(reader);
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0) return false;
            value = new(operation, commandPosition, commandExpected, previous, outcome, snapshot, safeCode);
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        {
            value = null;
            return false;
        }
    }

    private static void WriteCommandPayload(CborWriter writer, SessionLifecycleCommandBodyV1 value)
    {
        switch (value)
        {
            case SessionLifecycleCommandBodyV1.ReserveStarting reserve:
                writer.WriteStartMap(1); writer.WriteUInt64(1); WriteHash(writer, reserve.AdmissionFingerprint); writer.WriteEndMap();
                break;
            case SessionLifecycleCommandBodyV1.PublishReady ready:
                writer.WriteStartMap(1); writer.WriteUInt64(1); writer.WriteUInt64((ushort)ready.Availability); writer.WriteEndMap();
                break;
            case SessionLifecycleCommandBodyV1.BeginDrain:
                writer.WriteStartMap(0); writer.WriteEndMap();
                break;
            case SessionLifecycleCommandBodyV1.BeginTermination terminal:
                writer.WriteStartMap(4);
                writer.WriteUInt64(1); writer.WriteUInt64((ushort)terminal.Intent);
                writer.WriteUInt64(2); writer.WriteUInt64((ushort)terminal.Cause);
                writer.WriteUInt64(3); writer.WriteUInt64((ushort)terminal.Severity);
                writer.WriteUInt64(4); writer.WriteUInt64((ushort)terminal.Phase);
                writer.WriteEndMap();
                break;
            case SessionLifecycleCommandBodyV1.AdvanceTermination advance:
                writer.WriteStartMap(5);
                writer.WriteUInt64(1); writer.WriteUInt64((ushort)advance.Phase);
                writer.WriteUInt64(2); writer.WriteUInt64((ushort)advance.Intent);
                writer.WriteUInt64(3); writer.WriteUInt64((ushort)advance.Cause);
                writer.WriteUInt64(4); writer.WriteUInt64((ushort)advance.Severity);
                writer.WriteUInt64(5); writer.WriteBoolean(advance.ConversationStopped);
                writer.WriteEndMap();
                break;
            case SessionLifecycleCommandBodyV1.Complete complete:
                writer.WriteStartMap(1); writer.WriteUInt64(1); writer.WriteBoolean(complete.ConversationStopped); writer.WriteEndMap();
                break;
            default: throw new ArgumentException("The lifecycle command is not registered.", nameof(value));
        }
    }

    private static SessionLifecycleCommandBodyV1 ReadCommandPayload(
        CborReader reader,
        SessionLifecycleCommandKindV1 kind,
        OperationId operation,
        JournalPositionV1? expected) => kind switch
    {
        SessionLifecycleCommandKindV1.ReserveStarting => ReadReserve(reader, operation, expected),
        SessionLifecycleCommandKindV1.PublishReady => ReadReady(reader, operation, expected),
        SessionLifecycleCommandKindV1.BeginDrain => ReadDrain(reader, operation, expected),
        SessionLifecycleCommandKindV1.BeginTermination => ReadBeginTermination(reader, operation, expected),
        SessionLifecycleCommandKindV1.AdvanceTermination => ReadAdvance(reader, operation, expected),
        SessionLifecycleCommandKindV1.Complete => ReadComplete(reader, operation, expected),
        _ => throw new CborContentException("The lifecycle command kind is not registered."),
    };

    private static SessionLifecycleCommandBodyV1 ReadReserve(CborReader reader, OperationId operation, JournalPositionV1? expected)
    {
        if (expected is not null || reader.ReadStartMap() != 1 || reader.ReadUInt64() != 1) throw new CborContentException("ReserveStarting has one fingerprint and no predecessor.");
        var hash = ReadHash(reader); reader.ReadEndMap();
        return new SessionLifecycleCommandBodyV1.ReserveStarting(operation, hash);
    }

    private static SessionLifecycleCommandBodyV1 ReadReady(CborReader reader, OperationId operation, JournalPositionV1? expected)
    {
        var position = Require(expected);
        if (reader.ReadStartMap() != 1 || reader.ReadUInt64() != 1) throw new CborContentException("PublishReady has one availability.");
        var availability = (SessionAvailabilityWireV1)ReadClosed(reader, 5, false); reader.ReadEndMap();
        return new SessionLifecycleCommandBodyV1.PublishReady(operation, position, availability);
    }

    private static SessionLifecycleCommandBodyV1 ReadDrain(CborReader reader, OperationId operation, JournalPositionV1? expected)
    {
        var position = Require(expected);
        if (reader.ReadStartMap() != 0) throw new CborContentException("BeginDrain has an empty payload.");
        reader.ReadEndMap();
        return new SessionLifecycleCommandBodyV1.BeginDrain(operation, position);
    }

    private static SessionLifecycleCommandBodyV1 ReadBeginTermination(CborReader reader, OperationId operation, JournalPositionV1? expected)
    {
        var position = Require(expected);
        if (reader.ReadStartMap() != 4 || reader.ReadUInt64() != 1) throw new CborContentException("BeginTermination requires four fields.");
        var intent = (SessionTerminalIntentWireV1)ReadClosed(reader, 5, false);
        if (reader.ReadUInt64() != 2) throw InvalidTag();
        var cause = (SessionTerminalCauseWireV1)ReadClosed(reader, 7, false);
        if (reader.ReadUInt64() != 3) throw InvalidTag();
        var severity = (SessionTerminalSeverityWireV1)ReadClosed(reader, 3, false);
        if (reader.ReadUInt64() != 4) throw InvalidTag();
        var phase = (SessionConvergencePhaseWireV1)ReadClosed(reader, 8, false); reader.ReadEndMap();
        return new SessionLifecycleCommandBodyV1.BeginTermination(operation, position, intent, cause, severity, phase);
    }

    private static SessionLifecycleCommandBodyV1 ReadAdvance(CborReader reader, OperationId operation, JournalPositionV1? expected)
    {
        var position = Require(expected);
        if (reader.ReadStartMap() != 5 || reader.ReadUInt64() != 1) throw new CborContentException("AdvanceTermination requires five fields.");
        var phase = (SessionConvergencePhaseWireV1)ReadClosed(reader, 8, false);
        if (reader.ReadUInt64() != 2) throw InvalidTag();
        var intent = (SessionTerminalIntentWireV1)ReadClosed(reader, 5, false);
        if (reader.ReadUInt64() != 3) throw InvalidTag();
        var cause = (SessionTerminalCauseWireV1)ReadClosed(reader, 7, false);
        if (reader.ReadUInt64() != 4) throw InvalidTag();
        var severity = (SessionTerminalSeverityWireV1)ReadClosed(reader, 3, false);
        if (reader.ReadUInt64() != 5) throw InvalidTag();
        var stopped = reader.ReadBoolean(); reader.ReadEndMap();
        return new SessionLifecycleCommandBodyV1.AdvanceTermination(operation, position, phase, intent, cause, severity, stopped);
    }

    private static SessionLifecycleCommandBodyV1 ReadComplete(CborReader reader, OperationId operation, JournalPositionV1? expected)
    {
        var position = Require(expected);
        if (reader.ReadStartMap() != 1 || reader.ReadUInt64() != 1) throw new CborContentException("Complete requires one field.");
        var stopped = reader.ReadBoolean(); reader.ReadEndMap();
        return new SessionLifecycleCommandBodyV1.Complete(operation, position, stopped);
    }

    private static void WriteSnapshot(CborWriter writer, SessionLifecycleSnapshotBodyV1 value)
    {
        writer.WriteStartMap(12);
        writer.WriteUInt64(1); writer.WriteUInt64((ushort)value.State);
        writer.WriteUInt64(2); writer.WriteUInt64((ushort)value.Admission);
        writer.WriteUInt64(3); writer.WriteUInt64((ushort)value.Availability);
        writer.WriteUInt64(4); writer.WriteUInt64((ushort)value.Readiness);
        writer.WriteUInt64(5); writer.WriteUInt64((ushort)value.EstablishingTerminalIntent);
        writer.WriteUInt64(6); writer.WriteUInt64((ushort)value.EstablishingTerminalCause);
        writer.WriteUInt64(7); writer.WriteUInt64((ushort)value.CurrentTerminalIntent);
        writer.WriteUInt64(8); writer.WriteUInt64((ushort)value.CurrentTerminalCause);
        writer.WriteUInt64(9); writer.WriteUInt64((ushort)value.TerminalSeverity);
        writer.WriteUInt64(10); writer.WriteUInt64((ushort)value.ConvergencePhase);
        writer.WriteUInt64(11); writer.WriteUInt64((ushort)value.MutationFence);
        writer.WriteUInt64(12); writer.WriteBoolean(value.ConversationStopped);
        writer.WriteEndMap();
    }

    private static SessionLifecycleSnapshotBodyV1 ReadSnapshot(CborReader reader)
    {
        if (reader.ReadStartMap() != 12 || reader.ReadUInt64() != 1) throw new CborContentException("A lifecycle snapshot requires twelve fields.");
        var state = (SessionLifecycleStateWireV1)ReadClosed(reader, 5, false);
        if (reader.ReadUInt64() != 2) throw InvalidTag(); var admission = (SessionAdmissionWireV1)ReadClosed(reader, 2, false);
        if (reader.ReadUInt64() != 3) throw InvalidTag(); var availability = (SessionAvailabilityWireV1)ReadClosed(reader, 5, false);
        if (reader.ReadUInt64() != 4) throw InvalidTag(); var readiness = (SessionReadinessWireV1)ReadClosed(reader, 3, false);
        if (reader.ReadUInt64() != 5) throw InvalidTag(); var establishingIntent = (SessionTerminalIntentWireV1)ReadClosed(reader, 5, true);
        if (reader.ReadUInt64() != 6) throw InvalidTag(); var establishingCause = (SessionTerminalCauseWireV1)ReadClosed(reader, 7, true);
        if (reader.ReadUInt64() != 7) throw InvalidTag(); var currentIntent = (SessionTerminalIntentWireV1)ReadClosed(reader, 5, true);
        if (reader.ReadUInt64() != 8) throw InvalidTag(); var currentCause = (SessionTerminalCauseWireV1)ReadClosed(reader, 7, true);
        if (reader.ReadUInt64() != 9) throw InvalidTag(); var severity = (SessionTerminalSeverityWireV1)ReadClosed(reader, 3, true);
        if (reader.ReadUInt64() != 10) throw InvalidTag(); var phase = (SessionConvergencePhaseWireV1)ReadClosed(reader, 8, true);
        if (reader.ReadUInt64() != 11) throw InvalidTag(); var fence = (SessionMutationFenceWireV1)ReadClosed(reader, 2, false);
        if (reader.ReadUInt64() != 12) throw InvalidTag(); var stopped = reader.ReadBoolean(); reader.ReadEndMap();
        return new(state, admission, availability, readiness, establishingIntent, establishingCause,
            currentIntent, currentCause, severity, phase, fence, stopped);
    }

    private static void WritePositionOption(CborWriter writer, JournalPositionV1? value)
    {
        writer.WriteStartMap(value is null ? 1 : 2);
        writer.WriteUInt64(1); writer.WriteUInt64(value is null ? 0UL : 1UL);
        if (value is { } position) { writer.WriteUInt64(2); AuthorityPositionCodecsV1.Write(writer, position); }
        writer.WriteEndMap();
    }

    private static JournalPositionV1? ReadPositionOption(CborReader reader)
    {
        var count = reader.ReadStartMap();
        if (count is not (1 or 2) || reader.ReadUInt64() != 1) throw new CborContentException("An optional position has kind tag 1.");
        var kind = reader.ReadUInt64();
        if (kind == 0 && count == 1) { reader.ReadEndMap(); return null; }
        if (kind != 1 || count != 2 || reader.ReadUInt64() != 2) throw new CborContentException("An optional position is None or Some.");
        var value = AuthorityPositionCodecsV1.ReadJournal(reader); reader.ReadEndMap(); return value;
    }

    private static void WriteSafeCodeOption(CborWriter writer, BoundedAscii? value)
    {
        writer.WriteStartMap(value is null ? 1 : 2);
        writer.WriteUInt64(1); writer.WriteUInt64(value is null ? 0UL : 1UL);
        if (value is { } code) { writer.WriteUInt64(2); BoundedAsciiCodec.Write(writer, code); }
        writer.WriteEndMap();
    }

    private static BoundedAscii? ReadSafeCodeOption(CborReader reader)
    {
        var count = reader.ReadStartMap();
        if (count is not (1 or 2) || reader.ReadUInt64() != 1) throw new CborContentException("An optional safe code has kind tag 1.");
        var kind = reader.ReadUInt64();
        if (kind == 0 && count == 1) { reader.ReadEndMap(); return null; }
        if (kind != 1 || count != 2 || reader.ReadUInt64() != 2) throw new CborContentException("An optional safe code is None or Some.");
        var code = BoundedAsciiCodec.Read(reader); reader.ReadEndMap();
        if (code.ToString().Length > 64) throw new CborContentException("A safe code cannot exceed 64 ASCII bytes.");
        return code;
    }

    private static void WriteOperation(CborWriter writer, OperationId value)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!value.TryWriteBytes(bytes)) throw new ArgumentException("The lifecycle operation identity is invalid.", nameof(value));
        writer.WriteByteString(bytes);
    }

    private static OperationId ReadOperation(CborReader reader)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!reader.TryReadByteString(bytes, out var written) || written != 16) throw new CborContentException("An operation identity is exactly 16 bytes.");
        return OperationId.FromValue(StableId128.FromBytes(bytes));
    }

    private static void WriteHash(CborWriter writer, Hash256 value)
    {
        Span<byte> bytes = stackalloc byte[32];
        if (!value.TryWriteBytes(bytes)) throw new ArgumentException("The hash is invalid.", nameof(value));
        writer.WriteByteString(bytes);
    }

    private static Hash256 ReadHash(CborReader reader)
    {
        Span<byte> bytes = stackalloc byte[32];
        if (!reader.TryReadByteString(bytes, out var written) || written != 32) throw new CborContentException("A hash is exactly 32 bytes.");
        return Hash256.FromBytes(bytes);
    }

    private static ushort ReadClosed(CborReader reader, ushort maximum, bool allowZero)
    {
        var raw = reader.ReadUInt64();
        if (raw > maximum || !allowZero && raw == 0)
            throw new CborContentException("The enum value is not registered.");
        return (ushort)raw;
    }

    private static JournalPositionV1 Require(JournalPositionV1? value) =>
        value is { IsValid: true } position ? position : throw new CborContentException("The command requires a lifecycle predecessor.");

    private static CborContentException InvalidTag() => new("A lifecycle body contains an unexpected field tag.");
}
