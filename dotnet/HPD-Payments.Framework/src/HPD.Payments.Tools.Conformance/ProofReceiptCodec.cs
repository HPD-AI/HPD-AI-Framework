using System.Globalization;
using System.Text;

namespace HPD.Payments.Tools.Conformance;

/// <summary>Strictly decodes the canonical length-prefixed proof receipt format.</summary>
internal static class ProofReceiptCodec
{
    /// <summary>Parses one bounded receipt and rejects unknown, missing, malformed, or inconsistent fields.</summary>
    internal static ProofReceipt Parse(string canonical)
    {
        var fields = ProofCanonical.Split(canonical, 40);
        var cell = ProofCellKeyCodec.Parse(fields[0]);
        var dependencies = fields[14].Length == 0 ? [] : ProofCanonical.Split(fields[14], maximumFields: 4096, requireExact: false);
        if (!int.TryParse(fields[30], NumberStyles.Integer, CultureInfo.InvariantCulture, out var exitStatus) ||
            !DateTimeOffset.TryParseExact(fields[31], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var started) ||
            !DateTimeOffset.TryParseExact(fields[32], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ended) ||
            !long.TryParse(fields[33], NumberStyles.Integer, CultureInfo.InvariantCulture, out var durationTicks) ||
            ended - started != TimeSpan.FromTicks(durationTicks) ||
            !Enum.TryParse<ProofState>(fields[38], ignoreCase: false, out var state) || !Enum.IsDefined(state) ||
            !Enum.TryParse<ReceiptLifecycle>(fields[39], ignoreCase: false, out var lifecycle) || !Enum.IsDefined(lifecycle))
            throw new InvalidDataException("Receipt scalar, time, duration, proof-state, or lifecycle field is invalid.");
        return new()
        {
            Cell = cell,
            SchemaVersion = fields[1], ReceiptId = fields[2], RunId = fields[3], RouteId = fields[4],
            SourceRevision = fields[5], WholeTreeDigest = fields[6], DirtyState = fields[7], AdapterTreeDigest = fields[8],
            CanonicalRegistryDigest = fields[9], ClaimMatrixDigest = fields[10], PredecessorDigest = fields[11],
            SupersedesDigest = EmptyToNull(fields[12]), InvalidatesDigest = EmptyToNull(fields[13]),
            DependencyDigests = dependencies, CommandBinding = fields[15], AssertionsDigest = fields[16],
            OracleBinding = fields[17], CodeRevision = fields[18], ConfigurationRevision = fields[19],
            CredentialRevision = fields[20], ProtocolRevision = fields[21], PolicyRevision = fields[22],
            CorpusDigest = fields[23], RootSeed = fields[24], DerivedSeed = fields[25],
            VirtualTimeTraceDigest = fields[26], FaultScheduleDigest = fields[27], StandardOutputDigest = fields[28],
            StandardErrorDigest = fields[29], ExitStatus = exitStatus, StartedAtUtc = started, EndedAtUtc = ended,
            ResourceObservations = fields[34], Limitations = fields[35], CleanupAttestation = fields[36],
            Provenance = fields[37], State = state, Lifecycle = lifecycle,
        };
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;
}

/// <summary>Strictly decodes one exact 23-dimension proof-cell key.</summary>
internal static class ProofCellKeyCodec
{
    internal static ProofCellKey Parse(string canonical)
    {
        var cell = ProofCanonical.Split(canonical, 23);
        return new(cell[0], cell[1], cell[2], cell[3], cell[4], cell[5], cell[6], cell[7], cell[8], cell[9],
            cell[10], cell[11], cell[12], cell[13], cell[14], cell[15], cell[16], cell[17], cell[18], cell[19],
            cell[20], cell[21], cell[22]);
    }
}

internal static partial class ProofCanonical
{
    internal static string[] Split(string canonical, int maximumFields, bool requireExact = true)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        if (canonical.Length > 4_194_304 || maximumFields is < 1 or > 4096)
            throw new InvalidDataException("Canonical receipt is over-bound.");
        var bytes = new UTF8Encoding(false, true).GetBytes(canonical);
        var values = new List<string>();
        var offset = 0;
        while (offset < bytes.Length)
        {
            if (values.Count == maximumFields) throw new InvalidDataException("Canonical receipt has too many fields.");
            var length = 0;
            var digits = 0;
            while (offset < bytes.Length && bytes[offset] != (byte)':')
            {
                var digit = bytes[offset++] - (byte)'0';
                if (digit > 9 || ++digits > 10) throw new InvalidDataException("Canonical length prefix is invalid.");
                length = checked(length * 10 + digit);
            }
            if (digits == 0 || offset >= bytes.Length || bytes[offset++] != (byte)':' || length < 0 || offset + length > bytes.Length)
                throw new InvalidDataException("Canonical field is truncated.");
            values.Add(new UTF8Encoding(false, true).GetString(bytes, offset, length));
            offset += length;
        }
        if (requireExact && values.Count != maximumFields) throw new InvalidDataException("Canonical receipt field count is incomplete.");
        return values.ToArray();
    }
}
