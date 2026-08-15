using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HPD.Agent.SourceGenerator.SourceGeneration;

/// <summary>Generates internal graph-executable catalog evidence from exact application declarations.</summary>
[Generator]
public sealed class GraphExecutableFactoryCatalogSourceGenerator : IIncrementalGenerator
{
    private const string AttributeName = "HPD.Agent.Audio.Graph.HpdGraphExecutableFactoryAttribute";
    private static readonly byte[] FactoryDomain = Encoding.ASCII.GetBytes("hpd-s2-graph-executable-factory-v1\0");
    private static readonly byte[] CatalogDomain = Encoding.ASCII.GetBytes("hpd-s2-graph-executable-factory-catalog-v1\0");
    private static readonly DiagnosticDescriptor Invalid = new("HPDA006", "Invalid graph executable factory catalog",
        "Graph executable factory catalog is invalid: {0}", "HPD.Audio", DiagnosticSeverity.Error, true);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classes = context.SyntaxProvider.ForAttributeWithMetadataName(AttributeName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (value, _) => ReadClass(value))
            .Collect();
        context.RegisterSourceOutput(classes.Combine(context.CompilationProvider),
            static (production, pair) => Emit(production, pair.Left, pair.Right));
    }

    private static Candidate ReadClass(GeneratorAttributeSyntaxContext context)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        if (context.Attributes.Length != 1)
            return Candidate.Error(type.Locations.FirstOrDefault(), $"{type.ToDisplayString()} must have exactly one class declaration");
        var attribute = context.Attributes[0];
        if (attribute.ConstructorArguments.Length != 2 || attribute.ConstructorArguments[0].Value is not string key ||
            !TryUInt(attribute.ConstructorArguments[1], out var revision))
            return Candidate.Error(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                $"{type.ToDisplayString()} must use only the class (nodeKey, implementationRevision) form");
        return Candidate.Value(new Entry(type, key, revision, type.Locations.FirstOrDefault()));
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<Candidate> candidates, Compilation compilation)
    {
        foreach (var candidate in candidates)
            if (candidate.Message is not null)
                context.ReportDiagnostic(Diagnostic.Create(Invalid, candidate.Location, candidate.Message));
        if (candidates.Any(static candidate => candidate.Message is not null)) return;

        var localAssembly = compilation.Assembly.GetAttributes()
            .Where(static attribute => attribute.AttributeClass?.ToDisplayString() == AttributeName).ToArray();
        if (localAssembly.Length != 0)
        {
            foreach (var attribute in localAssembly)
                context.ReportDiagnostic(Diagnostic.Create(Invalid,
                    attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                    "manually authored local assembly contributions are forbidden"));
            return;
        }

        var entries = candidates.Select(static candidate => candidate.Entry!).ToList();
        foreach (var entry in entries)
            context.AddSource(Safe(entry.Type.ToDisplayString()) + ".GraphExecutableFactoryContribution.g.cs",
                Contribution(entry));

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != AttributeName) continue;
            if (attribute.ConstructorArguments.Length != 3 ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol type ||
                attribute.ConstructorArguments[1].Value is not string key ||
                !TryUInt(attribute.ConstructorArguments[2], out var revision) ||
                !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, assembly))
            {
                context.ReportDiagnostic(Diagnostic.Create(Invalid, null,
                    $"assembly {assembly.Name} has a malformed or non-owning graph executable contribution"));
                return;
            }
            entries.Add(new(type, key, revision, null));
        }

        var error = ValidateAndBind(entries);
        if (error is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Invalid, error.Value.Location, error.Value.Message));
            return;
        }
        if (entries.Count != 0)
            context.AddSource("GraphExecutableFactoryCatalogEvidenceV1.g.cs", Evidence(entries));
    }

    private static (Location? Location, string Message)? ValidateAndBind(List<Entry> entries)
    {
        if (entries.Count > 64) return (null, "more than 64 declarations");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!ConcreteClosedAccessible(entry.Type))
                return (entry.Location, $"{entry.Type.ToDisplayString()} must be concrete, non-static, closed, non-file-local, and assembly-accessible");
            if (!ValidNodeKey(entry.NodeKey)) return (entry.Location, $"invalid node key '{entry.NodeKey}'");
            if (entry.Revision == 0) return (entry.Location, $"node '{entry.NodeKey}' has revision zero");
            entry.ImplementationIdentity = Identity(entry.Type, entry.Revision);
            if (!entry.ImplementationIdentity.IsNormalized(NormalizationForm.FormC) ||
                Encoding.UTF8.GetByteCount(entry.ImplementationIdentity) > 512)
                return (entry.Location, $"node '{entry.NodeKey}' has an invalid NFC implementation identity");
            entry.FactoryIdentity = FactoryId(entry.NodeKey, entry.ImplementationIdentity, entry.Revision);
            if (entry.FactoryIdentity.All(static value => value == 0))
                return (entry.Location, $"node '{entry.NodeKey}' derives the invalid zero factory identity");
            if (!keys.Add(entry.NodeKey)) return (entry.Location, $"duplicate node key '{entry.NodeKey}'");
            if (!ids.Add(Hex(entry.FactoryIdentity))) return (entry.Location, $"duplicate factory identity for '{entry.NodeKey}'");
        }
        entries.Sort(Compare);
        return null;
    }

    private static bool ConcreteClosedAccessible(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic || type.IsUnboundGenericType || type.IsFileLocal)
            return false;
        for (var current = type; current is not null; current = current.ContainingType)
            if (current.Arity != 0 || current.DeclaredAccessibility is not Accessibility.Public and not Accessibility.Internal)
                return false;
        return true;
    }

    private static string Contribution(Entry entry) => $$"""
        // <auto-generated/>
        #nullable enable
        [assembly: global::HPD.Agent.Audio.Graph.HpdGraphExecutableFactoryAttribute(typeof({{entry.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}), {{Literal(entry.NodeKey)}}, {{entry.Revision.ToString(CultureInfo.InvariantCulture)}}U)]
        """;

    private static string Evidence(IReadOnlyList<Entry> entries)
    {
        var fingerprint = CatalogFingerprint(entries);
        var builder = new StringBuilder("// <auto-generated/>\n#nullable enable\nnamespace HPD.Agent.Audio.Graph.Generated;\n\n");
        builder.AppendLine("internal static class GraphExecutableFactoryCatalogEvidenceV1");
        builder.AppendLine("{");
        builder.Append("    internal const string CatalogFingerprintHex = \"").Append(Hex(fingerprint)).AppendLine("\";");
        builder.AppendLine("    internal static readonly string[] OrderedDeclarations =");
        builder.AppendLine("    [");
        foreach (var entry in entries)
            builder.Append("        ").Append(Literal(entry.NodeKey + "\0" + entry.ImplementationIdentity + "\0" +
                entry.Revision.ToString(CultureInfo.InvariantCulture))).AppendLine(",");
        builder.AppendLine("    ];");
        builder.AppendLine("    internal static readonly string[] FactoryIdentityHex =");
        builder.AppendLine("    [");
        foreach (var entry in entries) builder.Append("        \"").Append(Hex(entry.FactoryIdentity)).AppendLine("\",");
        builder.AppendLine("    ];");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static byte[] FactoryId(string nodeKey, string identity, uint revision)
    {
        var key = Encoding.UTF8.GetBytes(nodeKey); var implementation = Encoding.UTF8.GetBytes(identity);
        using var stream = new MemoryStream(); stream.Write(FactoryDomain, 0, FactoryDomain.Length);
        WriteU16(stream, checked((ushort)key.Length)); stream.Write(key, 0, key.Length);
        WriteU16(stream, checked((ushort)implementation.Length)); stream.Write(implementation, 0, implementation.Length);
        WriteU32(stream, revision); var digest = Sha(stream.ToArray()); return digest.Take(16).ToArray();
    }

    private static byte[] CatalogFingerprint(IReadOnlyList<Entry> entries)
    {
        using var cbor = new MemoryStream(); Array(cbor, 2); Unsigned(cbor, 1); Array(cbor, (ulong)entries.Count);
        foreach (var entry in entries)
        {
            Array(cbor, 4); Text(cbor, entry.NodeKey); Bytes(cbor, entry.FactoryIdentity);
            Text(cbor, entry.ImplementationIdentity); Unsigned(cbor, entry.Revision);
        }
        var payload = cbor.ToArray(); var preimage = new byte[CatalogDomain.Length + payload.Length];
        Buffer.BlockCopy(CatalogDomain, 0, preimage, 0, CatalogDomain.Length);
        Buffer.BlockCopy(payload, 0, preimage, CatalogDomain.Length, payload.Length); return Sha(preimage);
    }

    private static int Compare(Entry left, Entry right)
    {
        var compared = CompareBytes(Encoding.UTF8.GetBytes(left.NodeKey), Encoding.UTF8.GetBytes(right.NodeKey));
        if (compared != 0) return compared;
        compared = CompareBytes(left.FactoryIdentity, right.FactoryIdentity); if (compared != 0) return compared;
        compared = CompareBytes(Encoding.UTF8.GetBytes(left.ImplementationIdentity), Encoding.UTF8.GetBytes(right.ImplementationIdentity));
        return compared != 0 ? compared : left.Revision.CompareTo(right.Revision);
    }

    private static int CompareBytes(byte[] left, byte[] right)
    { for (var i = 0; i < Math.Min(left.Length, right.Length); i++) { var c = left[i].CompareTo(right[i]); if (c != 0) return c; } return left.Length.CompareTo(right.Length); }
    private static bool ValidNodeKey(string value) => value.Length is > 0 and <= 64 && value.All(static character => character is >= (char)0x21 and <= (char)0x7e);
    private static string Identity(INamedTypeSymbol type, uint revision) => type.ContainingAssembly.Name + ":" +
        type.ToDisplayString(new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces)) + "@" +
        revision.ToString(CultureInfo.InvariantCulture);
    private static bool TryUInt(TypedConstant value, out uint result)
    { if (value.Value is uint typed) { result = typed; return true; } result = 0; return false; }
    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);
    private static string Safe(string value)
    { uint hash = 2166136261; foreach (var character in value) { hash ^= character; hash *= 16777619; } return new string(value.Where(char.IsLetterOrDigit).ToArray()) + "." + hash.ToString("x8", CultureInfo.InvariantCulture); }
    private static byte[] Sha(byte[] value) { using var sha = SHA256.Create(); return sha.ComputeHash(value); }
    private static string Hex(byte[] value) => string.Concat(value.Select(static item => item.ToString("x2", CultureInfo.InvariantCulture)));
    private static void WriteU16(Stream stream, ushort value) { stream.WriteByte((byte)(value >> 8)); stream.WriteByte((byte)value); }
    private static void WriteU32(Stream stream, uint value) { stream.WriteByte((byte)(value >> 24)); stream.WriteByte((byte)(value >> 16)); stream.WriteByte((byte)(value >> 8)); stream.WriteByte((byte)value); }
    private static void Array(Stream stream, ulong count) => Major(stream, 4, count);
    private static void Text(Stream stream, string value) { var bytes = Encoding.UTF8.GetBytes(value); Major(stream, 3, (ulong)bytes.Length); stream.Write(bytes, 0, bytes.Length); }
    private static void Bytes(Stream stream, byte[] value) { Major(stream, 2, (ulong)value.Length); stream.Write(value, 0, value.Length); }
    private static void Unsigned(Stream stream, ulong value) => Major(stream, 0, value);
    private static void Major(Stream stream, byte major, ulong value)
    {
        if (value < 24) { stream.WriteByte((byte)(major << 5 | (byte)value)); return; }
        if (value <= byte.MaxValue) { stream.WriteByte((byte)(major << 5 | 24)); stream.WriteByte((byte)value); return; }
        if (value <= ushort.MaxValue) { stream.WriteByte((byte)(major << 5 | 25)); WriteU16(stream, (ushort)value); return; }
        stream.WriteByte((byte)(major << 5 | 26)); WriteU32(stream, checked((uint)value));
    }

    private sealed class Entry
    {
        internal Entry(INamedTypeSymbol type, string nodeKey, uint revision, Location? location)
        { Type = type; NodeKey = nodeKey; Revision = revision; Location = location; }
        internal INamedTypeSymbol Type { get; }
        internal string NodeKey { get; }
        internal uint Revision { get; }
        internal Location? Location { get; }
        internal string ImplementationIdentity { get; set; } = string.Empty;
        internal byte[] FactoryIdentity { get; set; } = System.Array.Empty<byte>();
    }

    private sealed class Candidate
    {
        private Candidate(Entry? entry, Location? location, string? message) { Entry = entry; Location = location; Message = message; }
        internal Entry? Entry { get; }
        internal Location? Location { get; }
        internal string? Message { get; }
        internal static Candidate Value(Entry entry) => new(entry, null, null);
        internal static Candidate Error(Location? location, string message) => new(null, location, message);
    }
}
