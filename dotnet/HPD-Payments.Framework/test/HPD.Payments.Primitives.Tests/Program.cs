using System.Reflection;
using HPD.Payments.Primitives.Classification;
using HPD.Payments.Primitives.Compatibility;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Manifests;
using HPD.Payments.Primitives.Results;
using HPD.Payments.Primitives.Time;

var tests = new (string Name, Action Body)[]
{
    ("default values are invalid", Defaults),
    ("identity equality hash canonical round trip and scope collisions", IdentityRoundTrip),
    ("bounds and malformed canonical bytes reject", Bounds),
    ("owned memory is defensive", OwnedLifetime),
    ("generations revisions cuts and named times enforce invariants", RevisionTimeCut),
    ("digests profiles compatibility and manifests preserve meaning", DigestCompatibilityManifest),
    ("unknown variants preserve bytes and classification", UnknownPreservation),
    ("typed results distinguish uncertainty", Results),
    ("public shapes have no borrowed or universal abstractions", PublicShapeAudit),
};

foreach (var test in tests) { test.Body(); Console.WriteLine($"PASS {test.Name}"); }
Console.WriteLine($"Executed {tests.Length} primitive proof groups.");

static void Defaults()
{
    False(default(ScopeId).IsValid); False(default(SemanticId).IsValid); False(default(OwnerGeneration).IsValid);
    False(default(Revision).IsValid); False(default(ContractVersion).IsValid); False(default(NamedTime).IsValid);
    False(default(ClassificationMark).IsValid); False(default(ReaderRange).IsValid);
    Throws<InvalidOperationException>(() => default(SemanticId).GetCanonicalBytes());
}

static void IdentityRoundTrip()
{
    var a = SemanticId.Create(ScopeId.Create("tenant-a", "live", "scoped-identity"), "checkout", "session", "abc");
    var same = SemanticId.Create(ScopeId.Create("tenant-a", "live", "scoped-identity"), "checkout", "session", "abc");
    var tenant = SemanticId.Create(ScopeId.Create("tenant-b", "live", "scoped-identity"), "checkout", "session", "abc");
    var environment = SemanticId.Create(ScopeId.Create("tenant-a", "test", "scoped-identity"), "checkout", "session", "abc");
    True(a == same); Equal(a.GetHashCode(), same.GetHashCode()); False(a == tenant); False(a == environment);
    True(SemanticId.TryParseCanonical(a.GetCanonicalBytes(), out var parsed)); Equal(a, parsed);
    var external = SemanticId.Create(a.Scope, "provider-operation", "effect", "e1", "stripe", "acct-1");
    True(SemanticId.TryParseCanonical(external.GetCanonicalBytes(), out var externalParsed)); Equal(external, externalParsed);
}

static void Bounds()
{
    False(ScopeId.TryCreate("Tenant", "live", "owner", out _));
    False(ScopeId.TryCreate(new string('a', 129), "live", "owner", out _));
    False(SemanticId.TryParseCanonical(new byte[] { 0, 9, 1 }, out _));
    False(SemanticId.TryParseCanonical(new byte[] { 0, 1, 0xff }, out _));
    Throws<ArgumentException>(() => Consume(new OwnedClassifiedBytes(new byte[2], ClassificationMark.Create(DataClassification.Restricted, RetentionKind.Durable), 1)));
}

static void OwnedLifetime()
{
    var source = new byte[] { 1, 2, 3 };
    var owned = new OwnedClassifiedBytes(source, ClassificationMark.Create(DataClassification.Confidential, RetentionKind.Durable));
    source[0] = 9; Equal((byte)1, owned.CopyBytes()[0]);
    var copy = owned.CopyBytes(); copy[1] = 9; Equal((byte)2, owned.CopyBytes()[1]);
}

static void RevisionTimeCut()
{
    var generation = OwnerGeneration.Create(1); True(generation.TryNext(out var next)); Equal(2UL, next.Value);
    False(OwnerGeneration.Create(ulong.MaxValue).TryNext(out _));
    var revision = Revision.Create("policy", 7); Equal(7UL, revision.Value);
    var record = NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch);
    Throws<ArgumentException>(() => NamedTime.Create(TimeKind.Record, DateTimeOffset.Now));
    var scope = ScopeId.Create("tenant-a", "live", "owner");
    var id = SemanticId.Create(scope, "facts", "fact", "f1");
    var input = new[] { new OwnerCut(scope, id, generation) };
    var cut = new HistoricalCut(HistoricalFrameKind.AsKnownAt, record, input, ContractVersion.Create(1, 0));
    input[0] = default; True(cut.OwnerCuts[0].IsValid);
    Throws<ArgumentException>(() => Consume(new HistoricalCut(HistoricalFrameKind.AsKnownAt, record, new[] { new OwnerCut(ScopeId.Create("tenant-b", "live", "owner"), id, generation) }, ContractVersion.Create(1, 0))));
}

static void DigestCompatibilityManifest()
{
    var profile = new CanonicalDigestProfileId("checkout-session", ContractVersion.Create(1, 0), "semantic-v1", "none", "utf8-time-v1", "ordered", "sha256-keyless");
    var d1 = CanonicalDigest.Sha256(profile, "meaning"u8); var d2 = CanonicalDigest.Sha256(profile, "meaning"u8);
    True(d1.Equals(d2)); Equal(d1.GetHashCode(), d2.GetHashCode());
    var range = new ReaderRange("checkout-session", ContractVersion.Create(1, 0), ContractVersion.Create(1, 2));
    Equal(CompatibilityKind.Compatible, range.Classify("checkout-session", ContractVersion.Create(1, 1)));
    Equal(CompatibilityKind.Unsupported, range.Classify("other", ContractVersion.Create(1, 1)));
    Equal(CompatibilityKind.Indeterminate, default(ReaderRange).Classify("other", ContractVersion.Create(1, 1)));
    var id = SemanticId.Create(ScopeId.Create("tenant-a", "live", "scoped-identity"), "manifests", "co-binding", "m1");
    var source = new[] { new CanonicalBinding(d1, d2, NamedTime.Create(TimeKind.Verify, DateTimeOffset.UnixEpoch)) };
    var manifest = new CanonicalBindingManifest(id, ContractVersion.Create(1, 0), source);
    source[0] = null!; True(manifest.Bindings[0].IsValid);
}

static void UnknownPreservation()
{
    var bytes = new byte[] { 4, 5, 6 };
    var unknown = new UnknownVariant("future-kind", ContractVersion.Create(9, 0), bytes, ClassificationMark.Create(DataClassification.Restricted, RetentionKind.Durable));
    bytes[0] = 0; Equal((byte)4, unknown.Payload.CopyBytes()[0]);
    var equal = new UnknownVariant("future-kind", ContractVersion.Create(9, 0), new byte[] { 4, 5, 6 }, ClassificationMark.Create(DataClassification.Restricted, RetentionKind.Durable));
    True(unknown.Equals(equal));
}

static void Results()
{
    Equal("ok", PrimitiveResults.Success("ok").Match(x => x, (_, _, _) => "bad"));
    foreach (var kind in new[] { ResultKind.Unknown, ResultKind.Indeterminate, ResultKind.Residual, ResultKind.Unverifiable, ResultKind.Conflict, ResultKind.Unsupported })
        Equal(kind, PrimitiveResults.NonSuccess<string>(kind, "bounded-code").Kind);
    Throws<ArgumentException>(() => PrimitiveResults.NonSuccess<string>(ResultKind.Success, "bad"));
}

static void PublicShapeAudit()
{
    var assembly = typeof(ScopeId).Assembly;
    var forbiddenNames = new[] { "Entity", "Event", "Status", "Money", "Hold", "Application", "Reversal", "Repository", "Envelope", "Timestamp", "Current" };
    foreach (var type in assembly.GetExportedTypes()) False(forbiddenNames.Contains(type.Name, StringComparer.Ordinal));
    foreach (var member in assembly.GetExportedTypes().SelectMany(static t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)))
    {
        var type = member switch { PropertyInfo p => p.PropertyType, FieldInfo f => f.FieldType, MethodInfo m => m.ReturnType, _ => null };
        if (type is not null) False(type.IsByRefLike || type == typeof(Memory<byte>) || type == typeof(ReadOnlyMemory<byte>));
    }
}

static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
static void False(bool value) => True(!value);
static void Equal<T>(T expected, T actual) where T : notnull { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
static void Consume(object value) => GC.KeepAlive(value);
