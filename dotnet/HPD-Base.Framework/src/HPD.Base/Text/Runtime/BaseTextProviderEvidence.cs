using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Creates and verifies the canonical provider evidence shared by Runtime and provider assemblies.</summary>
public static class BaseTextProviderEvidence
{
    /// <summary>Creates the exact lowering receipt for one session-owned preparation.</summary>
    public static BaseTextLoweringReceipt CreateLoweringReceipt(BaseTextProviderDescriptor provider, BaseTextAuthoritySnapshot snapshot, BaseTextIndexDefinition index, ImmutableArray<byte> queryDigest, ImmutableArray<byte> constraintDigest, ImmutableArray<BaseTextFieldInfluenceConstraint> influences, ImmutableArray<BaseTextOrder> order, BaseTextExecutionLimits limits)
    {
        ImmutableArray<byte> influenceDigest = Digest("base.text.influences.v1", stream => { Sequence(stream, influences.Length); foreach (BaseTextFieldInfluenceConstraint value in influences) { String(stream, value.StableFieldId); Bytes(stream, value.ConstraintDigest); } });
        ImmutableArray<byte> statement = Digest("base.text.statementShape.v1", stream => { String(stream, provider.Id); U64(stream, checked((ulong)provider.Version)); Bytes(stream, queryDigest); Bytes(stream, constraintDigest); });
        return new BaseTextLoweringReceipt
        {
            ProviderId = provider.Id, ProviderVersion = provider.Version, ProviderClass = provider.ProviderClass,
            AuthoritySnapshotDigest = SnapshotDigest(snapshot), IndexChecksum = Copy(index.DefinitionChecksum), QueryDigest = Copy(queryDigest), ConstraintDigest = Copy(constraintDigest), InfluenceConstraintsDigest = influenceDigest,
            StatementShapeDigest = statement, OrderingDigest = Digest("base.text.ordering.v1", stream => { Sequence(stream, order.Length); foreach (BaseTextOrder value in order) { String(stream, value.StableFieldId); stream.WriteByte((byte)value.Direction); stream.WriteByte((byte)value.NullOrder); } }), LimitsDigest = LimitsDigest(limits), CertificationReceiptDigest = DigestBytes("base.text.certificationReceipt.v1", provider.CertificationReceipt),
        };
    }

    /// <summary>Computes the canonical digest of one lowering receipt.</summary>
    public static ImmutableArray<byte> LoweringReceiptDigest(BaseTextLoweringReceipt value) => Digest("base.text.loweringReceipt.v1", stream =>
    { String(stream, value.ProviderId); U64(stream, checked((ulong)value.ProviderVersion)); U64(stream, (ulong)value.ProviderClass); Bytes(stream, value.AuthoritySnapshotDigest); Bytes(stream, value.IndexChecksum); Bytes(stream, value.QueryDigest); Bytes(stream, value.ConstraintDigest); Bytes(stream, value.InfluenceConstraintsDigest); Bytes(stream, value.StatementShapeDigest); Bytes(stream, value.OrderingDigest); Bytes(stream, value.LimitsDigest); Bytes(stream, value.CertificationReceiptDigest); });

    /// <summary>Creates canonical completeness evidence over one returned page.</summary>
    public static BaseTextCompletenessEvidence CreateCompleteness(BaseTextProviderDescriptor provider, BaseTextAuthoritySnapshot snapshot, BaseTextLoweringReceipt lowering, ImmutableArray<BaseTextCandidate> candidates, int takePlusOne, ImmutableArray<byte>? afterBoundary)
    {
        ImmutableArray<byte>? first = candidates.Length == 0 ? null : Copy(candidates[0].CanonicalOrderingBoundary); ImmutableArray<byte>? last = candidates.Length == 0 ? null : Copy(candidates[^1].CanonicalOrderingBoundary); ImmutableArray<byte> loweringDigest = LoweringReceiptDigest(lowering);
        ImmutableArray<byte> execution = Digest("base.text.providerExecution.v1", stream => { Bytes(stream, loweringDigest); U64(stream, checked((ulong)takePlusOne)); OptionalBytes(stream, afterBoundary); U64(stream, checked((ulong)candidates.Length)); Bool(stream, candidates.Length == takePlusOne); OptionalBytes(stream, first); OptionalBytes(stream, last); I64(stream, snapshot.SearchVisibleThrough.Value); });
        return new() { ProviderClass = provider.ProviderClass, LoweringReceiptDigest = loweringDigest, CertificationReceiptDigest = DigestBytes("base.text.certificationReceipt.v1", provider.CertificationReceipt), RequestedTakePlusOne = takePlusOne, RequestedAfterBoundary = afterBoundary is null ? null : Copy(afterBoundary.Value), ReturnedCandidateCount = candidates.Length, HasMore = candidates.Length == takePlusOne, FirstBoundary = first, LastBoundary = last, VisibleThrough = snapshot.SearchVisibleThrough, ProviderExecutionDigest = execution };
    }

    /// <summary>Verifies that completeness evidence is the canonical evidence for the supplied page.</summary>
    public static bool CompletenessEquals(BaseTextCompletenessEvidence actual, BaseTextCompletenessEvidence expected) => actual.ProviderClass == expected.ProviderClass && actual.RequestedTakePlusOne == expected.RequestedTakePlusOne && actual.ReturnedCandidateCount == expected.ReturnedCandidateCount && actual.HasMore == expected.HasMore && actual.VisibleThrough == expected.VisibleThrough && Equal(actual.LoweringReceiptDigest, expected.LoweringReceiptDigest) && Equal(actual.CertificationReceiptDigest, expected.CertificationReceiptDigest) && OptionalEqual(actual.RequestedAfterBoundary, expected.RequestedAfterBoundary) && OptionalEqual(actual.FirstBoundary, expected.FirstBoundary) && OptionalEqual(actual.LastBoundary, expected.LastBoundary) && Equal(actual.ProviderExecutionDigest, expected.ProviderExecutionDigest);

    /// <summary>Verifies every canonical member of a lowering receipt.</summary>
    public static bool LoweringEquals(BaseTextLoweringReceipt actual, BaseTextLoweringReceipt expected) =>
        actual.ProviderId == expected.ProviderId && actual.ProviderVersion == expected.ProviderVersion && actual.ProviderClass == expected.ProviderClass
        && Equal(actual.AuthoritySnapshotDigest, expected.AuthoritySnapshotDigest) && Equal(actual.IndexChecksum, expected.IndexChecksum)
        && Equal(actual.QueryDigest, expected.QueryDigest) && Equal(actual.ConstraintDigest, expected.ConstraintDigest)
        && Equal(actual.InfluenceConstraintsDigest, expected.InfluenceConstraintsDigest) && Equal(actual.StatementShapeDigest, expected.StatementShapeDigest)
        && Equal(actual.OrderingDigest, expected.OrderingDigest) && Equal(actual.LimitsDigest, expected.LimitsDigest)
        && Equal(actual.CertificationReceiptDigest, expected.CertificationReceiptDigest);

    /// <summary>Counts the exact statement parameters represented by the closed query and candidate constraint.</summary>
    public static long StatementParameterCount(BaseTextQuery query, BaseTextCandidateConstraint constraint) => checked(QueryParameters(query) + ConstraintParameters(constraint));

    private static long QueryParameters(BaseTextQuery value) => value switch
    {
        BaseTextQuery.Term => 1, BaseTextQuery.Prefix => 1, BaseTextQuery.Phrase phrase => phrase.Terms.Length,
        BaseTextQuery.Field field => QueryParameters(field.Child), BaseTextQuery.Not not => QueryParameters(not.Child),
        BaseTextQuery.And and => and.Children.Sum(QueryParameters), BaseTextQuery.Or or => or.Children.Sum(QueryParameters),
        _ => throw new InvalidOperationException("Unknown text query node."),
    };
    private static long ConstraintParameters(BaseTextCandidateConstraint value) => value switch
    {
        BaseTextCandidateConstraint.True or BaseTextCandidateConstraint.False or BaseTextCandidateConstraint.IsMissing or BaseTextCandidateConstraint.IsNull => 0,
        BaseTextCandidateConstraint.Equal => 1, BaseTextCandidateConstraint.In inside => inside.Values.Length,
        BaseTextCandidateConstraint.And and => and.Children.Sum(ConstraintParameters), BaseTextCandidateConstraint.Or or => or.Children.Sum(ConstraintParameters),
        _ => throw new InvalidOperationException("Unknown text constraint node."),
    };

    private static ImmutableArray<byte> SnapshotDigest(BaseTextAuthoritySnapshot value) => Digest("base.text.authoritySnapshot.v1", stream => { String(stream, value.StoreIdentityDigest); I64(stream, value.RestoreEpoch); I64(stream, value.SchemaGeneration); String(stream, value.CollectionId); I64(stream, value.PurgeGeneration); String(stream, value.TextIndexId); U64(stream, checked((ulong)value.TextIndexVersion)); I64(stream, value.TextIndexGeneration); I64(stream, value.AuthoritativeHead.Value); I64(stream, value.AppliedThrough.Value); I64(stream, value.SearchVisibleThrough.Value); Bytes(stream, value.AnalyzerReceipt); Bytes(stream, value.ScoringReceipt); });
    private static ImmutableArray<byte> LimitsDigest(BaseTextExecutionLimits value) => Digest("base.text.limits.v1", stream => { long[] values = [value.MaximumQueryNodes,value.MaximumQueryDepth,value.MaximumPhraseTerms,value.MaximumQueryBytes,value.MaximumFilterNodes,value.MaximumFilterDepth,value.MaximumFilterLiterals,value.MaximumInValues,value.MaximumPrefixExpansions,value.MaximumPrefixExpansionBytes,value.MaximumSecondaryOrderFields,value.MaximumOrderingBytes,value.MaximumCandidates,value.MaximumScoreProofBytes,value.MaximumTokensPerField,value.MaximumNormalizedBytesPerField,value.MaximumNormalizedBytesPerRecord,value.MaximumResults,value.MaximumResultBytes,value.MaximumCursorBytes,value.MaximumStatementParameters,value.MaximumTransientBytes,value.QueryTimeout.Ticks,value.ConsistencyWaitTimeout.Ticks]; foreach (long item in values) I64(stream,item); });
    private static ImmutableArray<byte> DigestBytes(string purpose, ImmutableArray<byte> value) => Digest(purpose, stream => Bytes(stream, value));
    private static ImmutableArray<byte> Digest(string purpose, Action<Stream> write) { using var stream = new MemoryStream(); stream.Write(Encoding.ASCII.GetBytes(purpose)); stream.WriteByte(0); write(stream); return ImmutableArray.Create(SHA256.HashData(stream.ToArray())); }
    private static ImmutableArray<byte> Copy(ImmutableArray<byte> value) => ImmutableArray.Create(value.ToArray());
    private static bool Equal(ImmutableArray<byte> left, ImmutableArray<byte> right) => left.AsSpan().SequenceEqual(right.AsSpan());
    private static bool OptionalEqual(ImmutableArray<byte>? left, ImmutableArray<byte>? right) => left is null && right is null || left is { } a && right is { } b && Equal(a,b);
    private static void String(Stream stream, string value) => Bytes(stream, ImmutableArray.Create(Encoding.UTF8.GetBytes(value)));
    private static void Bytes(Stream stream, ImmutableArray<byte> value) { Sequence(stream, value.Length); stream.Write(value.AsSpan()); }
    private static void OptionalBytes(Stream stream, ImmutableArray<byte>? value) { stream.WriteByte(value is null ? (byte)0 : (byte)1); if (value is { } bytes) Bytes(stream, bytes); }
    private static void Sequence(Stream stream, int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value)); stream.Write(bytes); }
    private static void U64(Stream stream, ulong value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, value); stream.Write(bytes); }
    private static void I64(Stream stream, long value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); stream.Write(bytes); }
    private static void Bool(Stream stream, bool value) => stream.WriteByte(value ? (byte)1 : (byte)0);
}
