using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal static class StudioEvidenceContract
{
    internal const string RecordMutationPath = "base.studio.evidence.record-mutation.v1";
    internal static StudioEvidenceCapability RecordMutationCapability() => new()
    {
        SupportedKinds = [StudioEvidenceKind.RecordMutation], MaximumItems = 1_024, MaximumRowsRead = 1_025,
        MaximumIntervals = 1, MaximumEvidenceBytes = 4_194_304, MaximumTransientBytes = 4_194_304,
        AcquisitionDeadline = TimeSpan.FromSeconds(5), SessionDeadline = TimeSpan.FromSeconds(15), PageDeadline = TimeSpan.FromSeconds(15),
        BackupIncludedKinds = [StudioEvidenceKind.RecordMutation], RestoreValidatedKinds = [StudioEvidenceKind.RecordMutation],
        CertificationChecksum = CapabilityChecksum([StudioEvidenceKind.RecordMutation], 1_024, 1_025, 1, 4_194_304, 4_194_304,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15), [StudioEvidenceKind.RecordMutation], [StudioEvidenceKind.RecordMutation]),
    };

    internal static bool Valid(StudioEvidenceCapability value)
    {
        return value.SupportedKinds.Length > 0 && value.SupportedKinds.Distinct().Count() == value.SupportedKinds.Length && value.SupportedKinds.All(Enum.IsDefined) &&
            value.MaximumItems > 0 && value.MaximumRowsRead > 0 && value.MaximumIntervals > 0 && value.MaximumEvidenceBytes > 0 && value.MaximumTransientBytes > 0 &&
            value.AcquisitionDeadline > TimeSpan.Zero && value.SessionDeadline > TimeSpan.Zero && value.PageDeadline > TimeSpan.Zero &&
            value.BackupIncludedKinds.All(value.SupportedKinds.Contains) && value.RestoreValidatedKinds.All(value.SupportedKinds.Contains) &&
            value.CertificationChecksum.Length == 32 && CryptographicOperations.FixedTimeEquals(value.CertificationChecksum.AsSpan(), CapabilityChecksum(
                value.SupportedKinds, value.MaximumItems, value.MaximumRowsRead, value.MaximumIntervals, value.MaximumEvidenceBytes, value.MaximumTransientBytes,
                value.AcquisitionDeadline, value.SessionDeadline, value.PageDeadline, value.BackupIncludedKinds, value.RestoreValidatedKinds).AsSpan());
    }
    private static ImmutableArray<byte> CapabilityChecksum(IEnumerable<StudioEvidenceKind> kinds, int items, long rows, int intervals,
        long evidence, long transient, TimeSpan acquisition, TimeSpan session, TimeSpan page,
        IEnumerable<StudioEvidenceKind> backup, IEnumerable<StudioEvidenceKind> restore) => Hash("capability",
        string.Join(',', kinds.Select(static x => (byte)x)), items.ToString(), rows.ToString(), intervals.ToString(), evidence.ToString(), transient.ToString(),
        acquisition.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture), session.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        page.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Join(',', backup.Select(static x => (byte)x)), string.Join(',', restore.Select(static x => (byte)x)));

    internal static bool Valid(StudioEvidenceRequirement value) =>
        !string.IsNullOrWhiteSpace(value.ApplicationId) && Enum.IsDefined(value.Kind) &&
        value.Parent is StudioCollectionEvidenceSubject or StudioRecordEvidenceSubject && ValidScope(value.Scope) &&
        value.ProtectedScopeSeekChecksum.Length == 32 && Valid(value.Limits) && Valid(value.Parent);

    internal static bool Valid(StudioEvidenceLimits value) => value.MaximumItems > 0 && value.MaximumRowsRead > 0 &&
        value.MaximumIntervals > 0 && value.MaximumEvidenceBytes > 0 && value.MaximumTransientBytes > 0 &&
        value.AcquisitionDeadline > TimeSpan.Zero && value.SessionDeadline > TimeSpan.Zero && value.PageDeadline > TimeSpan.Zero;

    internal static bool Valid(StudioEvidenceSubject value) => value switch
    {
        StudioCollectionEvidenceSubject collection => !string.IsNullOrWhiteSpace(collection.CollectionId) && collection.InstalledCollectionChecksum.Length == 32,
        StudioRecordEvidenceSubject record => !string.IsNullOrWhiteSpace(record.CollectionId) && record.InstalledCollectionChecksum.Length == 32 && !string.IsNullOrWhiteSpace(record.RecordId.Value),
        _ => false,
    };

    internal static bool ValidScope(BaseOwnedSubjectScopeEvidence scope) => scope.Kind switch
    { BaseSubjectScopeKind.Global => scope.Value is null, BaseSubjectScopeKind.Tenant or BaseSubjectScopeKind.Project => !string.IsNullOrWhiteSpace(scope.Value), _ => false };

    internal static ImmutableArray<byte> Tuple(long position)
    {
        byte[] bytes = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, position); return [.. bytes];
    }

    internal static bool Position(StudioEvidenceBoundary? boundary, out long position)
    {
        if (boundary is null) { position = 0; return true; }
        if (boundary.CanonicalTuple.Length != 8 || boundary.Checksum.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(boundary.Checksum.AsSpan(), BoundaryChecksum(boundary.Kind, boundary.CanonicalTuple).AsSpan())) { position = 0; return false; }
        position = BinaryPrimitives.ReadInt64BigEndian(boundary.CanonicalTuple.AsSpan()); return position >= 0;
    }

    internal static StudioEvidenceBoundary Boundary(StudioEvidenceKind kind, ImmutableArray<byte> tuple) => new()
    { Kind = kind, CanonicalTuple = [.. tuple], Checksum = BoundaryChecksum(kind, tuple) };
    internal static ImmutableArray<byte> BoundaryChecksum(StudioEvidenceKind kind, ImmutableArray<byte> tuple) =>
        Hash("boundary", ((byte)kind).ToString(System.Globalization.CultureInfo.InvariantCulture), Convert.ToHexString(tuple.AsSpan()));
    internal static ImmutableArray<byte> IntervalChecksum(string path, ImmutableArray<byte> scope, ImmutableArray<byte> lower, ImmutableArray<byte> upper) =>
        Hash("interval", path, Convert.ToHexString(scope.AsSpan()), Convert.ToHexString(lower.AsSpan()), Convert.ToHexString(upper.AsSpan()), "inclusive", "exclusive");

    internal static ImmutableArray<byte> Hash(params string?[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, "base-studio-evidence-binary-v1"); foreach (string? value in values) Add(hash, value ?? "");
        return [.. hash.GetHashAndReset()];
    }

    internal static ImmutableArray<byte> AuthorityChecksum(StudioEvidenceRequirement request, string store, long restore, long generation, string path) =>
        Hash(request.ApplicationId, ((byte)request.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture), store,
            restore.ToString(System.Globalization.CultureInfo.InvariantCulture), generation.ToString(System.Globalization.CultureInfo.InvariantCulture), path,
            Convert.ToHexString(request.ProtectedScopeSeekChecksum.AsSpan()), Scope(request.Scope), Subject(request.Parent));

    internal static ImmutableArray<byte> ItemChecksum(StudioEvidenceItem value) => Hash(
        ((byte)value.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture), Convert.ToHexString(value.OrderingTuple.AsSpan()),
        value.ObservedAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture), ((byte)value.SemanticKind).ToString(System.Globalization.CultureInfo.InvariantCulture),
        value is StudioRecordMutationEvidenceItem mutation ? mutation.CollectionId : null,
        value is StudioRecordMutationEvidenceItem mutation2 ? mutation2.RecordId.Value : null,
        value is StudioRecordMutationEvidenceItem mutation3 ? mutation3.Revision?.Value : null,
        value is StudioRecordMutationEvidenceItem mutation4 ? mutation4.EvidenceId : null,
        value is StudioRecordMutationEvidenceItem mutation5 ? mutation5.ReceiptIdentity : null,
        KindSpecific(value));

    private static string KindSpecific(StudioEvidenceItem value) => value switch
    {
        StudioReceiptEvidenceItem x => string.Join('|', x.ReceiptIdentity, x.ReceiptKind, x.Disposition, string.Join(',', x.AffectedResourceIdentities)),
        StudioActivationOccurrenceEvidenceItem x => string.Join('|', x.ScheduleId, x.OccurrenceId, x.ActivationId, x.Disposition),
        StudioActivationAttemptEvidenceItem x => string.Join('|', x.ActivationId, x.AttemptNumber, x.EventSequence, x.State),
        StudioActivationEffectEvidenceItem x => string.Join('|', x.ActivationId, x.AttemptNumber, x.EffectId, x.EventSequence, x.Outcome),
        StudioSearchRebuildEvidenceItem x => string.Join('|', x.IndexId, x.RebuildGeneration, x.PhaseSequence, x.Phase, x.ProbeOutcome),
        StudioLifecycleEvidenceItem x => string.Join('|', x.ContractId, Convert.ToHexString(x.ProtectedScopeOrder.AsSpan()), x.Epoch, x.Incarnation, x.Sequence, x.State),
        StudioRetirementEvidenceItem x => string.Join('|', x.ContractId, Convert.ToHexString(x.ProtectedScopeOrder.AsSpan()), x.Epoch, x.Incarnation, x.PublicationSequence, x.Disposition),
        StudioSchemaEvidenceItem x => string.Join('|', x.StoreId, x.SchemaGeneration, x.HistorySequence, Convert.ToHexString(x.AuthorityChecksum.AsSpan()), x.State),
        StudioBackupRestoreEvidenceItem x => string.Join('|', x.StoreId, x.OperationIdentity, x.RestoreEpoch, Convert.ToHexString(x.ArtifactAuthorityChecksum.AsSpan()), x.State),
        StudioMaintenanceEvidenceItem x => string.Join('|', x.StoreId, x.MaintenanceKind, x.Generation, x.PageSequence, x.State),
        StudioQuarantineEvidenceItem x => string.Join('|', x.StoreId, x.SubsystemId, x.QuarantineIdentity, x.State),
        StudioHealthTransitionEvidenceItem x => string.Join('|', x.ContributorId, x.ObservationPosition, x.ContributorGeneration, x.State),
        _ => "",
    };

    internal static ImmutableArray<byte> PageChecksum(IEnumerable<StudioEvidenceItem> items, long generation,
        StudioEvidenceBoundary? next, IEnumerable<StudioEvidenceReadInterval> intervals, StudioEvidenceProviderAccounting accounting) => Hash(
            generation.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Join(',', items.Select(static item => Convert.ToHexString(item.EvidenceChecksum.AsSpan()))),
            next is null ? "" : Convert.ToHexString(next.CanonicalTuple.AsSpan()),
            string.Join(',', intervals.Select(static interval => Convert.ToHexString(interval.Checksum.AsSpan()))), accounting.RowsRead.ToString(System.Globalization.CultureInfo.InvariantCulture),
            accounting.Intervals.ToString(System.Globalization.CultureInfo.InvariantCulture), accounting.EvidenceBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            accounting.TransientBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));

    internal static string Subject(StudioEvidenceSubject subject) => subject switch
    {
        StudioCollectionEvidenceSubject value => $"collection:{value.CollectionId}:{Convert.ToHexString(value.InstalledCollectionChecksum.AsSpan())}",
        StudioRecordEvidenceSubject value => $"record:{value.CollectionId}:{value.RecordId.Value}:{Convert.ToHexString(value.InstalledCollectionChecksum.AsSpan())}",
        _ => throw new InvalidOperationException("The evidence subject is invalid."),
    };
    internal static string Scope(BaseOwnedSubjectScopeEvidence scope) => $"{(byte)scope.Kind}:{scope.Value ?? ""}";

    internal static string CollectionIndexKey(BaseOwnedSubjectScopeEvidence scope, string collectionId) => Convert.ToHexString(Hash("collection-index", Scope(scope), collectionId).AsSpan());
    internal static string RecordIndexKey(BaseOwnedSubjectScopeEvidence scope, string collectionId, RecordId recordId) =>
        Convert.ToHexString(Hash("record-index", Scope(scope), collectionId, recordId.Value).AsSpan());

    internal static long Measure(StudioEvidenceItem item) => checked(item.OrderingTuple.Length + item.EvidenceChecksum.Length +
        1 + (item is StudioRecordMutationEvidenceItem mutation
            ? Encoding.UTF8.GetByteCount(mutation.CollectionId) + Encoding.UTF8.GetByteCount(mutation.RecordId.Value) +
              Encoding.UTF8.GetByteCount(mutation.Revision?.Value ?? "") + Encoding.UTF8.GetByteCount(mutation.EvidenceId) : 0) + 32);

    internal static StudioEvidenceRequirement Freeze(StudioEvidenceRequirement value) => value with
    {
        ApplicationId = new string(value.ApplicationId.AsSpan()), ProtectedScopeSeekChecksum = [.. value.ProtectedScopeSeekChecksum],
        Scope = value.Scope with { Value = value.Scope.Value is null ? null : new string(value.Scope.Value.AsSpan()) },
        Parent = value.Parent switch
        {
            StudioCollectionEvidenceSubject collection => collection with { CollectionId = new string(collection.CollectionId.AsSpan()), InstalledCollectionChecksum = [.. collection.InstalledCollectionChecksum] },
            StudioRecordEvidenceSubject record => record with { CollectionId = new string(record.CollectionId.AsSpan()), InstalledCollectionChecksum = [.. record.InstalledCollectionChecksum], RecordId = RecordId.Create(new string(record.RecordId.Value.AsSpan())) },
            _ => throw new InvalidOperationException("The evidence subject is invalid."),
        },
        Limits = value.Limits with { },
    };

    private static void Add(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value); Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length); hash.AppendData(length); hash.AppendData(bytes);
    }
}
