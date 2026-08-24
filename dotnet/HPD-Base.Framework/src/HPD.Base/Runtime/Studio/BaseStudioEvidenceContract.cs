using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal static class BaseStudioEvidenceContract
{
    internal const string RecordMutationPath = "base.studio.evidence.record-mutation.v1";
    internal static BaseStudioEvidenceCapability RecordMutationCapability() => new()
    {
        SupportedKinds = [BaseStudioEvidenceKind.RecordMutation], MaximumItems = 1_024, MaximumRowsRead = 1_025,
        MaximumIntervals = 1, MaximumEvidenceBytes = 4_194_304, MaximumTransientBytes = 4_194_304,
        AcquisitionDeadline = TimeSpan.FromSeconds(5), SessionDeadline = TimeSpan.FromSeconds(15), PageDeadline = TimeSpan.FromSeconds(15),
        BackupIncludedKinds = [BaseStudioEvidenceKind.RecordMutation], RestoreValidatedKinds = [BaseStudioEvidenceKind.RecordMutation],
        CertificationChecksum = CapabilityChecksum([BaseStudioEvidenceKind.RecordMutation], 1_024, 1_025, 1, 4_194_304, 4_194_304,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15), [BaseStudioEvidenceKind.RecordMutation], [BaseStudioEvidenceKind.RecordMutation]),
    };

    internal static bool Valid(BaseStudioEvidenceCapability value)
    {
        return value.SupportedKinds.Length > 0 && value.SupportedKinds.Distinct().Count() == value.SupportedKinds.Length && value.SupportedKinds.All(Enum.IsDefined) &&
            value.MaximumItems > 0 && value.MaximumRowsRead > 0 && value.MaximumIntervals > 0 && value.MaximumEvidenceBytes > 0 && value.MaximumTransientBytes > 0 &&
            value.AcquisitionDeadline > TimeSpan.Zero && value.SessionDeadline > TimeSpan.Zero && value.PageDeadline > TimeSpan.Zero &&
            value.BackupIncludedKinds.All(value.SupportedKinds.Contains) && value.RestoreValidatedKinds.All(value.SupportedKinds.Contains) &&
            value.CertificationChecksum.Length == 32 && CryptographicOperations.FixedTimeEquals(value.CertificationChecksum.AsSpan(), CapabilityChecksum(
                value.SupportedKinds, value.MaximumItems, value.MaximumRowsRead, value.MaximumIntervals, value.MaximumEvidenceBytes, value.MaximumTransientBytes,
                value.AcquisitionDeadline, value.SessionDeadline, value.PageDeadline, value.BackupIncludedKinds, value.RestoreValidatedKinds).AsSpan());
    }
    private static ImmutableArray<byte> CapabilityChecksum(IEnumerable<BaseStudioEvidenceKind> kinds, int items, long rows, int intervals,
        long evidence, long transient, TimeSpan acquisition, TimeSpan session, TimeSpan page,
        IEnumerable<BaseStudioEvidenceKind> backup, IEnumerable<BaseStudioEvidenceKind> restore) => Hash("capability",
        string.Join(',', kinds.Select(static x => (byte)x)), items.ToString(), rows.ToString(), intervals.ToString(), evidence.ToString(), transient.ToString(),
        acquisition.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture), session.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        page.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Join(',', backup.Select(static x => (byte)x)), string.Join(',', restore.Select(static x => (byte)x)));

    internal static bool Valid(BaseStudioEvidenceRequirement value) =>
        !string.IsNullOrWhiteSpace(value.ApplicationId) && Enum.IsDefined(value.Kind) &&
        value.Parent is BaseStudioCollectionEvidenceSubject or BaseStudioRecordEvidenceSubject && ValidScope(value.Scope) &&
        value.ProtectedScopeSeekChecksum.Length == 32 && Valid(value.Limits) && Valid(value.Parent);

    internal static bool Valid(BaseStudioEvidenceLimits value) => value.MaximumItems > 0 && value.MaximumRowsRead > 0 &&
        value.MaximumIntervals > 0 && value.MaximumEvidenceBytes > 0 && value.MaximumTransientBytes > 0 &&
        value.AcquisitionDeadline > TimeSpan.Zero && value.SessionDeadline > TimeSpan.Zero && value.PageDeadline > TimeSpan.Zero;

    internal static bool Valid(BaseStudioEvidenceSubject value) => value switch
    {
        BaseStudioCollectionEvidenceSubject collection => !string.IsNullOrWhiteSpace(collection.CollectionId) && collection.InstalledCollectionChecksum.Length == 32,
        BaseStudioRecordEvidenceSubject record => !string.IsNullOrWhiteSpace(record.CollectionId) && record.InstalledCollectionChecksum.Length == 32 && !string.IsNullOrWhiteSpace(record.RecordId.Value),
        _ => false,
    };

    internal static bool ValidScope(BaseOwnedSubjectScopeEvidence scope) => scope.Kind switch
    { BaseSubjectScopeKind.Global => scope.Value is null, BaseSubjectScopeKind.Tenant or BaseSubjectScopeKind.Project => !string.IsNullOrWhiteSpace(scope.Value), _ => false };

    internal static ImmutableArray<byte> Tuple(long position)
    {
        byte[] bytes = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, position); return [.. bytes];
    }

    internal static bool Position(BaseStudioEvidenceBoundary? boundary, out long position)
    {
        if (boundary is null) { position = 0; return true; }
        if (boundary.CanonicalTuple.Length != 8 || boundary.Checksum.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(boundary.Checksum.AsSpan(), BoundaryChecksum(boundary.Kind, boundary.CanonicalTuple).AsSpan())) { position = 0; return false; }
        position = BinaryPrimitives.ReadInt64BigEndian(boundary.CanonicalTuple.AsSpan()); return position >= 0;
    }

    internal static BaseStudioEvidenceBoundary Boundary(BaseStudioEvidenceKind kind, ImmutableArray<byte> tuple) => new()
    { Kind = kind, CanonicalTuple = [.. tuple], Checksum = BoundaryChecksum(kind, tuple) };
    internal static ImmutableArray<byte> BoundaryChecksum(BaseStudioEvidenceKind kind, ImmutableArray<byte> tuple) =>
        Hash("boundary", ((byte)kind).ToString(System.Globalization.CultureInfo.InvariantCulture), Convert.ToHexString(tuple.AsSpan()));
    internal static ImmutableArray<byte> IntervalChecksum(string path, ImmutableArray<byte> scope, ImmutableArray<byte> lower, ImmutableArray<byte> upper) =>
        Hash("interval", path, Convert.ToHexString(scope.AsSpan()), Convert.ToHexString(lower.AsSpan()), Convert.ToHexString(upper.AsSpan()), "inclusive", "exclusive");

    internal static ImmutableArray<byte> Hash(params string?[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, "base-studio-evidence-binary-v1"); foreach (string? value in values) Add(hash, value ?? "");
        return [.. hash.GetHashAndReset()];
    }

    internal static ImmutableArray<byte> AuthorityChecksum(BaseStudioEvidenceRequirement request, string store, long restore, long generation, string path) =>
        Hash(request.ApplicationId, ((byte)request.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture), store,
            restore.ToString(System.Globalization.CultureInfo.InvariantCulture), generation.ToString(System.Globalization.CultureInfo.InvariantCulture), path,
            Convert.ToHexString(request.ProtectedScopeSeekChecksum.AsSpan()), Scope(request.Scope), Subject(request.Parent));

    internal static ImmutableArray<byte> ItemChecksum(BaseStudioEvidenceItem value) => Hash(
        ((byte)value.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture), Convert.ToHexString(value.OrderingTuple.AsSpan()),
        value.ObservedAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture), ((byte)value.SemanticKind).ToString(System.Globalization.CultureInfo.InvariantCulture),
        value is BaseStudioRecordMutationEvidenceItem mutation ? mutation.CollectionId : null,
        value is BaseStudioRecordMutationEvidenceItem mutation2 ? mutation2.RecordId.Value : null,
        value is BaseStudioRecordMutationEvidenceItem mutation3 ? mutation3.Revision?.Value : null,
        value is BaseStudioRecordMutationEvidenceItem mutation4 ? mutation4.EvidenceId : null,
        value is BaseStudioRecordMutationEvidenceItem mutation5 ? mutation5.ReceiptIdentity : null,
        KindSpecific(value));

    private static string KindSpecific(BaseStudioEvidenceItem value) => value switch
    {
        BaseStudioReceiptEvidenceItem x => string.Join('|', x.ReceiptIdentity, x.ReceiptKind, x.Disposition, string.Join(',', x.AffectedResourceIdentities)),
        BaseStudioActivationOccurrenceEvidenceItem x => string.Join('|', x.ScheduleId, x.OccurrenceId, x.ActivationId, x.Disposition),
        BaseStudioActivationAttemptEvidenceItem x => string.Join('|', x.ActivationId, x.AttemptNumber, x.EventSequence, x.State),
        BaseStudioActivationEffectEvidenceItem x => string.Join('|', x.ActivationId, x.AttemptNumber, x.EffectId, x.EventSequence, x.Outcome),
        BaseStudioSearchRebuildEvidenceItem x => string.Join('|', x.IndexId, x.RebuildGeneration, x.PhaseSequence, x.Phase, x.ProbeOutcome),
        BaseStudioLifecycleEvidenceItem x => string.Join('|', x.ContractId, Convert.ToHexString(x.ProtectedScopeOrder.AsSpan()), x.Epoch, x.Incarnation, x.Sequence, x.State),
        BaseStudioRetirementEvidenceItem x => string.Join('|', x.ContractId, Convert.ToHexString(x.ProtectedScopeOrder.AsSpan()), x.Epoch, x.Incarnation, x.PublicationSequence, x.Disposition),
        BaseStudioSchemaEvidenceItem x => string.Join('|', x.StoreId, x.SchemaGeneration, x.HistorySequence, Convert.ToHexString(x.AuthorityChecksum.AsSpan()), x.State),
        BaseStudioBackupRestoreEvidenceItem x => string.Join('|', x.StoreId, x.OperationIdentity, x.RestoreEpoch, Convert.ToHexString(x.ArtifactAuthorityChecksum.AsSpan()), x.State),
        BaseStudioMaintenanceEvidenceItem x => string.Join('|', x.StoreId, x.MaintenanceKind, x.Generation, x.PageSequence, x.State),
        BaseStudioQuarantineEvidenceItem x => string.Join('|', x.StoreId, x.SubsystemId, x.QuarantineIdentity, x.State),
        BaseStudioHealthTransitionEvidenceItem x => string.Join('|', x.ContributorId, x.ObservationPosition, x.ContributorGeneration, x.State),
        _ => "",
    };

    internal static ImmutableArray<byte> PageChecksum(IEnumerable<BaseStudioEvidenceItem> items, long generation,
        BaseStudioEvidenceBoundary? next, IEnumerable<BaseStudioEvidenceReadInterval> intervals, BaseStudioEvidenceProviderAccounting accounting) => Hash(
            generation.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Join(',', items.Select(static item => Convert.ToHexString(item.EvidenceChecksum.AsSpan()))),
            next is null ? "" : Convert.ToHexString(next.CanonicalTuple.AsSpan()),
            string.Join(',', intervals.Select(static interval => Convert.ToHexString(interval.Checksum.AsSpan()))), accounting.RowsRead.ToString(System.Globalization.CultureInfo.InvariantCulture),
            accounting.Intervals.ToString(System.Globalization.CultureInfo.InvariantCulture), accounting.EvidenceBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            accounting.TransientBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));

    internal static string Subject(BaseStudioEvidenceSubject subject) => subject switch
    {
        BaseStudioCollectionEvidenceSubject value => $"collection:{value.CollectionId}:{Convert.ToHexString(value.InstalledCollectionChecksum.AsSpan())}",
        BaseStudioRecordEvidenceSubject value => $"record:{value.CollectionId}:{value.RecordId.Value}:{Convert.ToHexString(value.InstalledCollectionChecksum.AsSpan())}",
        _ => throw new InvalidOperationException("The evidence subject is invalid."),
    };
    internal static string Scope(BaseOwnedSubjectScopeEvidence scope) => $"{(byte)scope.Kind}:{scope.Value ?? ""}";

    internal static string CollectionIndexKey(BaseOwnedSubjectScopeEvidence scope, string collectionId) => Convert.ToHexString(Hash("collection-index", Scope(scope), collectionId).AsSpan());
    internal static string RecordIndexKey(BaseOwnedSubjectScopeEvidence scope, string collectionId, RecordId recordId) =>
        Convert.ToHexString(Hash("record-index", Scope(scope), collectionId, recordId.Value).AsSpan());

    internal static long Measure(BaseStudioEvidenceItem item) => checked(item.OrderingTuple.Length + item.EvidenceChecksum.Length +
        1 + (item is BaseStudioRecordMutationEvidenceItem mutation
            ? Encoding.UTF8.GetByteCount(mutation.CollectionId) + Encoding.UTF8.GetByteCount(mutation.RecordId.Value) +
              Encoding.UTF8.GetByteCount(mutation.Revision?.Value ?? "") + Encoding.UTF8.GetByteCount(mutation.EvidenceId) : 0) + 32);

    internal static BaseStudioEvidenceRequirement Freeze(BaseStudioEvidenceRequirement value) => value with
    {
        ApplicationId = new string(value.ApplicationId.AsSpan()), ProtectedScopeSeekChecksum = [.. value.ProtectedScopeSeekChecksum],
        Scope = value.Scope with { Value = value.Scope.Value is null ? null : new string(value.Scope.Value.AsSpan()) },
        Parent = value.Parent switch
        {
            BaseStudioCollectionEvidenceSubject collection => collection with { CollectionId = new string(collection.CollectionId.AsSpan()), InstalledCollectionChecksum = [.. collection.InstalledCollectionChecksum] },
            BaseStudioRecordEvidenceSubject record => record with { CollectionId = new string(record.CollectionId.AsSpan()), InstalledCollectionChecksum = [.. record.InstalledCollectionChecksum], RecordId = new RecordId(new string(record.RecordId.Value.AsSpan())) },
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
