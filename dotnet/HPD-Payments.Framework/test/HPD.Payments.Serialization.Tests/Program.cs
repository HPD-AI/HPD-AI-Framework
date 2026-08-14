using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using HPD.Payments.Serialization.Wire;

var tests = new (string Name, Action Run)[]
{
    ("closed-17-family-registry", ClosedRegistry),
    ("golden-all-families", GoldenFamilies),
    ("old-new-reader-writer-matrix", VersionMatrix),
    ("unknown-nested-preservation", UnknownPreservation),
    ("canonical-semantic-digest", CanonicalDigest),
    ("bounded-malformed-deep-oversize", BoundedInputs),
    ("fuzz-deterministic-malformed", Fuzz),
};
foreach (var test in tests) { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
Console.WriteLine($"PASS exact-once serialization suites={tests.Length}");
return 0;

static void ClosedRegistry()
{
    Assert(AuthorityWireRegistry.All.Length == 17, "family count");
    var names = new HashSet<string>(StringComparer.Ordinal);
    foreach (var entry in AuthorityWireRegistry.All)
    {
        Assert(names.Add(entry.Discriminator), "duplicate discriminator");
        Assert(AuthorityWireRegistry.TryResolve(entry.Discriminator, out var resolved) && resolved == entry.Family, "reverse map");
        Assert(AuthorityWireRegistry.GetDiscriminator(entry.Family) == entry.Discriminator, "forward map");
    }
    Assert(!AuthorityWireRegistry.TryResolve(typeof(AuthorityFamily).FullName!, out _), "CLR name dispatch");
}

static void GoldenFamilies()
{
    var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-v1.json");
    var fixtureBytes = File.ReadAllBytes(fixturePath);
    var fixtureNames = JsonSerializer.Deserialize<string[]>(fixtureBytes)!;
    Assert(fixtureNames.Length == 17, "fixture family count");
    Console.WriteLine($"FIXTURE golden-v1 sha256={Convert.ToHexStringLower(SHA256.HashData(fixtureBytes))}");
    foreach (var entry in AuthorityWireRegistry.All)
    {
        Assert(fixtureNames.Contains(entry.Discriminator, StringComparer.Ordinal), "fixture mapping");
        var bytes = Encoding.UTF8.GetBytes($"{{\"kind\":\"{entry.Discriminator}\",\"semanticVersion\":1,\"representationVersion\":1,\"semanticFields\":{{\"id\":\"golden\"}}}}");
        var read = AuthorityWireCodec.Read(bytes, 1, 1, 1);
        Assert(read.Disposition == CompatibilityDisposition.Supported && read.Family == entry.Family, entry.Discriminator);
        Assert(AuthorityWireCodec.Read(AuthorityWireCodec.Write(read.Document!), 1, 1, 1).Disposition == CompatibilityDisposition.Supported, "roundtrip");
    }
}

static void VersionMatrix()
{
    static WireReadResult Read(int semantic, int representation, int min, int max, int repr) => AuthorityWireCodec.Read(Encoding.UTF8.GetBytes($"{{\"kind\":\"agreement\",\"semanticVersion\":{semantic},\"representationVersion\":{representation},\"semanticFields\":{{}}}}"), min, max, repr);
    Assert(Read(1, 1, 1, 2, 2).Disposition == CompatibilityDisposition.Supported, "old writer/new reader");
    Assert(Read(2, 2, 1, 1, 1).Disposition == CompatibilityDisposition.Quarantined, "new writer/old reader");
    Assert(Read(1, 1, 2, 2, 2).Disposition == CompatibilityDisposition.Unsupported, "retired semantics");
    Assert(Read(1, 1, 0, 0, 0).Disposition == CompatibilityDisposition.Indeterminate, "invalid reader facts");
}

static void UnknownPreservation()
{
    var json = "{\"kind\":\"future-authority\",\"semanticVersion\":9,\"representationVersion\":3,\"semanticFields\":{\"nested\":{\"future\":[1,{\"x\":true}]}},\"futureTop\":{\"opaque\":7}}";
    var result = AuthorityWireCodec.Read(Encoding.UTF8.GetBytes(json), 1, 1, 1);
    Assert(result.Disposition == CompatibilityDisposition.Quarantined && result.Family is null, "unknown quarantine");
    Assert(Encoding.UTF8.GetString(result.OwnedUtf8.Span) == json, "complete owned preservation");
    Assert(result.Document!.UnknownProperties!["futureTop"].GetProperty("opaque").GetInt32() == 7, "top unknown");
    Assert(result.Document.SemanticFields["nested"].GetProperty("future")[1].GetProperty("x").GetBoolean(), "nested unknown");
}

static void CanonicalDigest()
{
    var a = Parse("{\"kind\":\"agreement\",\"semanticVersion\":1,\"representationVersion\":1,\"semanticFields\":{\"b\":2,\"a\":{\"y\":2,\"x\":1}}}");
    var b = Parse("{\"representationVersion\":2,\"semanticFields\":{\"a\":{\"x\":1,\"y\":2},\"b\":2},\"semanticVersion\":1,\"kind\":\"agreement\",\"unknown\":true}");
    Assert(AuthorityWireCodec.ComputeSemanticDigest(a) == AuthorityWireCodec.ComputeSemanticDigest(b), "representation independence");
    static AuthorityWireDocument Parse(string json) => AuthorityWireCodec.Read(Encoding.UTF8.GetBytes(json), 1, 1, 2).Document!;
}

static void BoundedInputs()
{
    Assert(AuthorityWireCodec.Read("{"u8, 1, 1, 1).Reason == "malformed-json", "malformed");
    Assert(AuthorityWireCodec.Read(new byte[33], 1, 1, 1, new(32, 4, 4, 4)).Reason == "document-size", "oversize");
    var deep = Encoding.UTF8.GetBytes("{\"kind\":\"agreement\",\"semanticVersion\":1,\"representationVersion\":1,\"semanticFields\":{\"x\":[[[[[0]]]]]}}");
    Assert(AuthorityWireCodec.Read(deep, 1, 1, 1, new(1024, 4, 4, 4)).Reason == "malformed-json", "deep");
}

static void Fuzz()
{
    uint state = 0x5EEDu;
    for (var i = 0; i < 256; i++)
    {
        var bytes = new byte[(Next() % 511) + 1];
        for (var j = 0; j < bytes.Length; j++) bytes[j] = (byte)Next();
        var result = AuthorityWireCodec.Read(bytes, 1, 2, 2, new(512, 8, 16, 8));
        Assert(result.OwnedUtf8.Length == bytes.Length, "owned fuzz input");
        Assert(result.Disposition is CompatibilityDisposition.Unsupported or CompatibilityDisposition.Quarantined, "fuzz fail closed");
    }
    int Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (int)(state & 0x7FFF_FFFF); }
}

static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
