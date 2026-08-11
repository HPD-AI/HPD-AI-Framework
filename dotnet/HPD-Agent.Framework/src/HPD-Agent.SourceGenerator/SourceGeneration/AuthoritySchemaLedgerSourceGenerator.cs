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
    private const string ExpectedCanonicalSha256 = "4f3167ba83daa0ca9d8911691bafd7fcb4a97c8c478b700026634609d1c089f7";
    private static readonly DiagnosticDescriptor InvalidLedger = new(
        "HPDA002", "Invalid authority schema ledger", "Authority schema ledger is invalid: {0}",
        "HPD.Authority", DiagnosticSeverity.Error, true);

    private static readonly (string Name, int Count)[] ExpectedSections =
    [
        ("IdFamilies", 46), ("IdFamilyCborUsages", 81), ("Axes", 11), ("Dimensions", 14),
        ("LinearizationPoints", 39), ("WireTypes", 27), ("Schemas", 99), ("SchemaFields", 397),
        ("AxisValueBindings", 11), ("CapacitySubjectBindings", 11), ("UnionDiscriminators", 9),
        ("JsonProjectionContexts", 99), ("CborCodecHashInventory", 99),
        ("AuthorityPayloadDiscriminators", 33), ("GenerationTransitionSchemas", 11),
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
        if (schemas.Count != 99 || sections["SchemaFields"].Any(row => !schemas.Contains(row.Split('|')[0])) ||
            sections["CborCodecHashInventory"].Any(row => !schemas.Contains(row.Split('|')[0])))
        {
            Fail(context, "schema fields or codecs do not join the 99-schema registry");
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
        context.AddSource("AuthoritySchemaLedgerV1.g.cs", source.ToString());
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static void Fail(SourceProductionContext context, string reason) =>
        context.ReportDiagnostic(Diagnostic.Create(InvalidLedger, Location.None, reason));
}
