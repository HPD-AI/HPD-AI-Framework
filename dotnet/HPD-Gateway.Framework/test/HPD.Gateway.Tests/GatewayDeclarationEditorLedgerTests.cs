using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using HPD.Gateway.ControlPlane;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayDeclarationEditorLedgerTests
{
    [Fact]
    public void ExportIsCanonicalBoundedAndNonCircular()
    {
        GatewayDeclarationEditorLedgerEnvelope envelope = Envelope(Record("canonicalizationVersion"));

        GatewayDeclarationEditorLedgerExportDocument first =
            GatewayDeclarationEditorLedgerExporter.Export(envelope);
        GatewayDeclarationEditorLedgerExportDocument second =
            GatewayDeclarationEditorLedgerExporter.Export(envelope);

        first.Utf8.Should().Equal(second.Utf8);
        first.Value.ExportVersion.Should().Be(1);
        first.Value.HashAlgorithm.Should().Be("sha-256");
        first.Value.EnvelopeSha256.Should().MatchRegex("^[0-9a-f]{64}$");

        JsonObject outer = JsonNode.Parse(first.Utf8.AsSpan())!.AsObject();
        outer.Select(static property => property.Key).Should().Equal(
            "envelope", "envelopeSha256", "exportVersion", "hashAlgorithm");
        byte[] canonicalEnvelope = GatewayCanonicalJson.Serialize(outer["envelope"]!);
        first.Value.EnvelopeSha256.Should().Be(HashEnvelope(canonicalEnvelope));
    }

    [Fact]
    public void ExportRejectsUnsortedDuplicateAndUnknownValues()
    {
        GatewayEditorFieldRecord first = Record("routes");
        GatewayEditorFieldRecord second = Record("canonicalizationVersion");

        FluentActions.Invoking(() => GatewayDeclarationEditorLedgerExporter.Export(
            Envelope(first, second))).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => GatewayDeclarationEditorLedgerExporter.Export(
            Envelope(first, first))).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => GatewayDeclarationEditorLedgerExporter.Export(
            Envelope(first with { Family = (GatewayEditorDeclarationFamily)255 })))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ExportRejectsMalformedPathCapabilityAndDisposition()
    {
        GatewayEditorFieldRecord baseline = Record("routes");

        FluentActions.Invoking(() => GatewayDeclarationEditorLedgerExporter.Export(Envelope(
            baseline with { Target = baseline.Target with { OccurrencePath = default } })))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => GatewayDeclarationEditorLedgerExporter.Export(Envelope(
            baseline with
            {
                Capability = new(GatewayEditorCapabilityKind.ResilienceProfile, ["/profileName"]),
            }))).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => GatewayDeclarationEditorLedgerExporter.Export(Envelope(
            baseline with
            {
                Disposition = GatewayEditorFieldDisposition.StructuralOnly,
                StructuralReason = GatewayEditorStructuralReason.None,
            }))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RealGatewaySchemaProjectsEveryOccurrenceDeterministically()
    {
        JsonObject snapshot = JsonNode.Parse(File.ReadAllBytes(FindGeneratorFixture()))!.AsObject();
        JsonObject openApi = snapshot["openApi"]!.AsObject();

        GatewayDeclarationEditorLedgerExportDocument first =
            GatewayDeclarationEditorLedgerExporter.Export(openApi);
        GatewayDeclarationEditorLedgerExportDocument second =
            GatewayDeclarationEditorLedgerExporter.Export(ReverseObjects(openApi).AsObject());

        first.Utf8.Should().Equal(second.Utf8);
        first.Value.Envelope.Records.Should().HaveCount(420);
        first.Value.Envelope.Records.Select(static record => record.Target.OccurrencePath)
            .Distinct(OccurrencePathComparer.Instance).Should().HaveSameCount(first.Value.Envelope.Records);
        first.Value.Envelope.Records.Where(static record =>
            record.Disposition == GatewayEditorFieldDisposition.Editable)
            .Select(static record => record.Family).Distinct()
            .Should().Contain(Enum.GetValues<GatewayEditorDeclarationFamily>().Where(static family =>
                family != GatewayEditorDeclarationFamily.None));
        first.Value.Envelope.Records.Select(static record => record.Capability.Kind).Distinct()
            .Should().Contain(Enum.GetValues<GatewayEditorCapabilityKind>());

        string[] expectedConstraints = GatewayAdminClientSchemaConstraintLedger.V1
            .Where(static item => GatewayAdminSchemaReferenceIds.Create(item.SchemaType)!
                .StartsWith("HPD_Gateway_Abstractions_", StringComparison.Ordinal))
            .Select(static item => "#/components/schemas/" +
                GatewayAdminSchemaReferenceIds.Create(item.SchemaType) + "/properties/" + item.PropertyName + ":" +
                item.AppliesTo.ToString().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] claimedConstraints = first.Value.Envelope.Records
            .SelectMany(static record => record.Target.ConstraintTargets)
            .Select(static target => target.SchemaRef + target.PropertyPointer + ":" +
                target.AppliesTo.ToString().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        claimedConstraints.Should().Equal(expectedConstraints);
        first.Value.Envelope.Records.Count(static record =>
            record.Inheritance == GatewayEditorInheritanceKind.RootInheritedAndRouteReplaced)
            .Should().Be(8);

        JsonObject drifted = openApi.DeepClone().AsObject();
        drifted["components"]!["schemas"]!["HPD_Gateway_Abstractions_GatewayConfiguration"]!
            ["properties"]!.AsObject()["invented"] = new JsonObject { ["type"] = "string" };
        FluentActions.Invoking(() => GatewayDeclarationEditorLedgerExporter.Export(drifted))
            .Should().Throw<InvalidOperationException>();
    }

    private static GatewayDeclarationEditorLedgerEnvelope Envelope(params GatewayEditorFieldRecord[] records) =>
        new(1, GatewayDeclarationEditorLedgerExporter.DeclarationSchemaRef, [.. records]);

    private static GatewayEditorFieldRecord Record(string property) => new(
        new(
            [new(GatewayEditorOccurrenceStepKind.Property, property, null)],
            GatewayDeclarationEditorLedgerExporter.DeclarationSchemaRef,
            "/properties/" + property,
            []),
        GatewayEditorFieldDisposition.Editable,
        GatewayEditorCompositionScope.Document,
        GatewayEditorOmittedValueKind.Absent,
        null,
        GatewayEditorInheritanceKind.None,
        [],
        GatewayEditorDeclarationFamily.Routing,
        new(GatewayEditorCapabilityKind.None, []),
        GatewayEditorPresentationGroup.Document,
        "gateway.editor.document." + property.ToLowerInvariant(),
        GatewayEditorQuickRouteStep.None,
        GatewayEditorStructuralReason.None);

    private static string HashEnvelope(byte[] bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd.gateway.editor-ledger.v1\0"u8);
        Span<byte> length = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string FindGeneratorFixture()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "typescript",
                "hpd-gateway-client-generator", "fixtures", "gateway-client-snapshot.json");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Gateway generator fixture was not found.");
    }

    private static JsonNode ReverseObjects(JsonNode node) => node switch
    {
        JsonObject value => new JsonObject(value.Reverse().Select(static property =>
            KeyValuePair.Create(property.Key, property.Value is null ? null : ReverseObjects(property.Value)))),
        JsonArray value => new JsonArray(value.Select(static item =>
            item is null ? null : ReverseObjects(item)).ToArray()),
        _ => node.DeepClone(),
    };

    private sealed class OccurrencePathComparer : IEqualityComparer<ImmutableArray<GatewayEditorOccurrenceStep>>
    {
        internal static OccurrencePathComparer Instance { get; } = new();

        public bool Equals(ImmutableArray<GatewayEditorOccurrenceStep> x,
            ImmutableArray<GatewayEditorOccurrenceStep> y) => x.SequenceEqual(y);

        public int GetHashCode(ImmutableArray<GatewayEditorOccurrenceStep> value)
        {
            var hash = new HashCode();
            foreach (GatewayEditorOccurrenceStep step in value) hash.Add(step);
            return hash.ToHashCode();
        }
    }
}
