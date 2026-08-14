using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace HPD.Agent.SourceGenerator.SourceGeneration;

/// <summary>Generates the closed authority schema, tag, discriminator, and codec inventories.</summary>
[Generator]
public sealed class AuthoritySchemaLedgerSourceGenerator : IIncrementalGenerator
{
    private const string ExpectedCanonicalSha256 = "9ef191500c2beb85c7d9b3ee41df9cc6bf76fdf5d3572d2e2122e06d96057010";
    private static readonly DiagnosticDescriptor InvalidLedger = new(
        "HPDA002", "Invalid authority schema ledger", "Authority schema ledger is invalid: {0}",
        "HPD.Authority", DiagnosticSeverity.Error, true);

    private static readonly (string Name, int Count)[] ExpectedSections =
    [
        ("IdFamilies", 48), ("IdFamilyCborUsages", 135), ("Axes", 11), ("Dimensions", 14),
        ("LinearizationPoints", 39), ("WireTypes", 34), ("WireTypeMembers", 134),
        ("Schemas", 162), ("SchemaFields", 727),
        ("AxisValueBindings", 11), ("CapacitySubjectBindings", 11), ("UnionDiscriminators", 9),
        ("JsonProjectionContexts", 162), ("CborCodecHashInventory", 162),
        ("AuthorityPayloadDiscriminators", 48), ("GenerationTransitionSchemas", 11),
        ("GenerationInitializationSchemas", 10),
        ("NativeSchemaInventory", 0),
    ];

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var ledgers = context.AdditionalTextsProvider
            .Where(static file => string.Equals(Path.GetFileName(file.Path), "authority-schema-ledger-v1.txt", StringComparison.Ordinal))
            .Select(static (file, cancellationToken) => file.GetText(cancellationToken)?.ToString() ?? string.Empty)
            .Collect();
        context.RegisterSourceOutput(ledgers, static (productionContext, texts) => Emit(productionContext, texts));
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<string> texts)
    {
        if (texts.Length == 0)
            return;
        if (texts.Length != 1)
        {
            Fail(context, $"expected one ledger, found {texts.Length}");
            return;
        }

        var canonicalText = texts[0].Replace("\r\n", "\n").Replace('\r', '\n');
        using (var sha = SHA256.Create())
        {
            var actual = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(canonicalText)).Select(static value => value.ToString("x2")));
            if (!string.Equals(actual, ExpectedCanonicalSha256, StringComparison.Ordinal))
            {
                Fail(context, $"canonical ledger hash mismatch: {actual}");
                return;
            }
        }

        var sections = ExpectedSections.ToDictionary(item => item.Name, _ => new List<string>(), StringComparer.Ordinal);
        string? current = null;
        var provenance = 0;
        foreach (var raw in canonicalText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.StartsWith("# source-", StringComparison.Ordinal))
            {
                var separator = raw.IndexOf('=');
                if (separator < 0 || raw.Length - separator - 1 != 64 || raw.Skip(separator + 1).Any(static c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
                {
                    Fail(context, "invalid source provenance hash");
                    return;
                }
                provenance++;
                continue;
            }
            if (raw.StartsWith("#", StringComparison.Ordinal))
                continue;
            if (raw.StartsWith("@", StringComparison.Ordinal))
            {
                current = raw.Substring(1);
                if (!sections.ContainsKey(current))
                {
                    Fail(context, $"unknown section {current}");
                    return;
                }
                continue;
            }
            if (current is null || raw.Length == 0 || raw.Any(static c => char.IsControl(c)))
            {
                Fail(context, "row outside a section or containing control characters");
                return;
            }
            sections[current].Add(raw);
        }

        if (provenance != 2)
        {
            Fail(context, $"expected two provenance hashes, found {provenance}");
            return;
        }
        foreach (var expected in ExpectedSections)
        {
            var rows = sections[expected.Name];
            var mustBeUnique = !string.Equals(expected.Name, "JsonProjectionContexts", StringComparison.Ordinal);
            if (rows.Count != expected.Count || mustBeUnique && rows.Count != rows.Distinct(StringComparer.Ordinal).Count())
            {
                Fail(context, $"section {expected.Name} expected {expected.Count} unique rows, found {rows.Count}");
                return;
            }
        }

        var schemas = new HashSet<string>(sections["Schemas"].Select(static row => row.Split('|')[0]), StringComparer.Ordinal);
        if (schemas.Count != 162 || sections["SchemaFields"].Any(row => !schemas.Contains(row.Split('|')[0])) ||
            sections["CborCodecHashInventory"].Any(row => !schemas.Contains(row.Split('|')[0])))
        {
            Fail(context, "schema fields or codecs do not join the 162-schema registry");
            return;
        }

        var source = new StringBuilder("// <auto-generated/>\n#nullable enable\nnamespace HPD.Agent.Authority;\n\ninternal static class AuthoritySchemaLedgerV1\n{\n");
        foreach (var expected in ExpectedSections)
        {
            source.Append("    internal static readonly string[] ").Append(expected.Name).AppendLine(" =\n    [");
            foreach (var row in sections[expected.Name])
                source.Append("        \"").Append(Escape(row)).AppendLine("\",");
            source.AppendLine("    ];");
        }
        source.AppendLine("}");
        var axes = sections["Axes"].Select(static row => row.Split('|')).ToArray();
        source.AppendLine("\n/// <summary>Identifies one registered authority-generation axis.</summary>");
        source.AppendLine("public enum AuthorityAxisId : ushort\n{");
        foreach (var axis in axes)
        {
            var member = AxisMember(axis[1]);
            source.Append("    /// <summary>Identifies the ").Append(member).Append(" axis owned by ").Append(axis[2]).AppendLine(".</summary>");
            source.Append("    ").Append(member).Append(" = ").Append(axis[0]).AppendLine(",");
        }
        source.AppendLine("}");
        source.AppendLine("\n/// <summary>Provides the closed typed values permitted in a sparse expected-authority vector.</summary>");
        source.AppendLine("public abstract record AuthorityAxisValueV1\n{");
        source.AppendLine("    private AuthorityAxisValueV1() { }");
        source.AppendLine("    /// <summary>Gets the registered axis represented by this value.</summary>");
        source.AppendLine("    public abstract AuthorityAxisId AxisId { get; }");
        source.AppendLine("    internal abstract bool TryWriteBytes(global::System.Span<byte> destination);");
        foreach (var axis in axes.Where(static axis => string.Equals(axis[3], "SparseAxisEntry", StringComparison.Ordinal)))
        {
            var member = AxisMember(axis[1]);
            source.Append("    /// <summary>Contains a validated ").Append(member).Append(" generation owned by ").Append(axis[2]).AppendLine(".</summary>");
            source.Append("    public sealed record ").Append(member).AppendLine(" : AuthorityAxisValueV1\n    {");
            source.Append("        /// <summary>Initializes the typed ").Append(member).AppendLine(" axis value.</summary>");
            source.AppendLine("        /// <param name=\"value\">The non-default semantic generation identifier.</param>");
            source.AppendLine("        /// <exception cref=\"global::System.ArgumentException\">The identifier is the invalid default value.</exception>");
            source.Append("        public ").Append(member).Append('(').Append(axis[1]).AppendLine(" value)\n        {");
            source.AppendLine("            if (!value.IsValid) throw new global::System.ArgumentException(\"A generation identifier is required.\", nameof(value));");
            source.AppendLine("            Value = value;\n        }");
            source.Append("        /// <summary>Gets the typed ").Append(member).AppendLine(" generation identifier.</summary>");
            source.Append("        public ").Append(axis[1]).AppendLine(" Value { get; }");
            source.Append("        /// <inheritdoc />\n        public override AuthorityAxisId AxisId => AuthorityAxisId.").Append(member).AppendLine(";");
            source.AppendLine("        internal override bool TryWriteBytes(global::System.Span<byte> destination) => Value.TryWriteBytes(destination);");
            source.AppendLine("    }");
        }
        source.AppendLine("}");
        context.AddSource("AuthoritySchemaLedgerV1.g.cs", source.ToString());
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string AxisMember(string wrapper) => wrapper.EndsWith("GenerationId", StringComparison.Ordinal)
        ? wrapper.Substring(0, wrapper.Length - "GenerationId".Length)
        : throw new InvalidOperationException($"Axis wrapper {wrapper} does not end in GenerationId.");
    private static void Fail(SourceProductionContext context, string reason) =>
        context.ReportDiagnostic(Diagnostic.Create(InvalidLedger, Location.None, reason));
}
