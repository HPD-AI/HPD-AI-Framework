using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace HPD.Gateway.Admin;

internal static class GatewayDeclarationEditorLedgerExporter
{
    internal const string DeclarationSchemaRef =
        "#/components/schemas/HPD_Gateway_Abstractions_GatewayConfiguration";
    private const int MaximumRecords = 4_096;
    private const int MaximumOccurrenceSteps = 64;
    private const int MaximumSchemaReferenceBytes = 512;
    private const int MaximumPointerBytes = 1_024;
    private const int MaximumHelpCodeBytes = 128;
    private const int MaximumConstraintTargets = 3;
    private const int MaximumOmittedValueBytes = 16 * 1024;

    internal static GatewayDeclarationEditorLedgerExportDocument Export(JsonObject openApi)
    {
        ArgumentNullException.ThrowIfNull(openApi);
        JsonObject securitySchemes = openApi["components"]?["securitySchemes"]?.AsObject() ??
            throw new InvalidOperationException("Gateway editor OpenAPI security catalog is missing.");
        if (securitySchemes.Count != 1)
            throw new InvalidOperationException("Gateway editor OpenAPI security catalog is invalid.");
        GatewayClientOpenApiJsonValidator.Validate(openApi, securitySchemes.First().Key);
        return Export(GatewayDeclarationEditorLedgerProjector.Project(openApi));
    }

    internal static GatewayDeclarationEditorLedgerExportDocument Export(
        GatewayDeclarationEditorLedgerEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        Validate(envelope);
        JsonObject envelopeNode = ProjectEnvelope(envelope);
        byte[] envelopeUtf8 = GatewayCanonicalJson.Serialize(envelopeNode);
        string digest = Convert.ToHexStringLower(Hash(envelopeUtf8));
        var value = new GatewayDeclarationEditorLedgerExportV1(1, "sha-256", digest, envelope);
        var exportNode = new JsonObject
        {
            ["envelope"] = envelopeNode,
            ["envelopeSha256"] = digest,
            ["exportVersion"] = 1,
            ["hashAlgorithm"] = "sha-256",
        };
        return new(value, GatewayCanonicalJson.Serialize(exportNode).ToImmutableArray());
    }

    private static void Validate(GatewayDeclarationEditorLedgerEnvelope value)
    {
        if (value.SchemaVersion != 1)
            throw new InvalidOperationException("Editor ledger schemaVersion must be 1.");
        if (!string.Equals(value.DeclarationSchemaRef, DeclarationSchemaRef, StringComparison.Ordinal))
            throw new InvalidOperationException("Editor ledger declarationSchemaRef is unsupported.");
        if (value.Records.IsDefault || value.Records.Length > MaximumRecords)
            throw new InvalidOperationException("Editor ledger records are malformed or over bound.");

        ImmutableArray<GatewayEditorOccurrenceStep> previous = default;
        foreach (GatewayEditorFieldRecord record in value.Records)
        {
            if (record is null)
                throw new InvalidOperationException("Editor ledger contains a null record.");
            ValidateRecord(record);
            if (!previous.IsDefault && ComparePath(previous, record.Target.OccurrencePath) >= 0)
                throw new InvalidOperationException("Editor ledger records are not strictly ordered and unique.");
            previous = record.Target.OccurrencePath;
        }
    }

    private static void ValidateRecord(GatewayEditorFieldRecord record)
    {
        if (record.Target is null || record.Capability is null)
            throw new InvalidOperationException("Editor ledger record contains a null object.");
        ValidateEnum(record.Disposition);
        ValidateEnum(record.CompositionScope);
        ValidateEnum(record.OmittedValueKind);
        ValidateEnum(record.Inheritance);
        ValidateEnum(record.Family);
        ValidateEnum(record.PresentationGroup);
        ValidateEnum(record.QuickRouteStep);
        ValidateEnum(record.StructuralReason);
        ValidatePath(record.Target.OccurrencePath, false);
        ValidateAscii(record.Target.ComponentSchemaRef, MaximumSchemaReferenceBytes, "component schema ref");
        ValidateAscii(record.Target.ComponentSchemaPointer, MaximumPointerBytes, "component schema pointer");
        if (record.Target.ConstraintTargets.IsDefault ||
            record.Target.ConstraintTargets.Length > MaximumConstraintTargets)
            throw new InvalidOperationException("Editor constraint targets are malformed or over bound.");
        GatewayEditorConstraintTarget? previous = null;
        foreach (GatewayEditorConstraintTarget target in record.Target.ConstraintTargets)
        {
            if (target is null)
                throw new InvalidOperationException("Editor constraint target is null.");
            ValidateEnum(target.AppliesTo);
            ValidateAscii(target.SchemaRef, MaximumSchemaReferenceBytes, "constraint schema ref");
            ValidateAscii(target.PropertyPointer, MaximumPointerBytes, "constraint property pointer");
            if (previous is not null && CompareConstraint(previous, target) >= 0)
                throw new InvalidOperationException("Editor constraint targets are not strictly ordered and unique.");
            previous = target;
        }
        ValidatePath(record.InheritanceSourceOccurrencePath, true);
        ValidateAscii(record.HelpCode, MaximumHelpCodeBytes, "help code");
        if (!record.HelpCode.StartsWith("gateway.editor.", StringComparison.Ordinal) ||
            record.HelpCode.Any(static c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-')))
            throw new InvalidOperationException("Editor help code is invalid.");
        if (record.OmittedValueKind == GatewayEditorOmittedValueKind.Absent && record.OmittedValueJson is not null)
            throw new InvalidOperationException("Absent omitted value must not carry JSON.");
        if (record.OmittedValueKind == GatewayEditorOmittedValueKind.CanonicalJson)
            ValidateUtf8(record.OmittedValueJson, MaximumOmittedValueBytes, "omitted value JSON");
        if (record.Disposition == GatewayEditorFieldDisposition.Editable &&
            record.StructuralReason != GatewayEditorStructuralReason.None)
            throw new InvalidOperationException("Editable field cannot have a structural reason.");
        if (record.Disposition == GatewayEditorFieldDisposition.StructuralOnly &&
            (record.StructuralReason == GatewayEditorStructuralReason.None ||
             record.QuickRouteStep != GatewayEditorQuickRouteStep.None ||
             record.Capability.Kind != GatewayEditorCapabilityKind.None))
            throw new InvalidOperationException("Structural field invariants are violated.");
        ValidateCapability(record.Capability);
    }

    private static void ValidateCapability(GatewayEditorCapabilitySelector value)
    {
        ValidateEnum(value.Kind);
        if (value.RelativeValuePointers.IsDefault || value.RelativeValuePointers.Length > 2)
            throw new InvalidOperationException("Capability pointers are malformed or over bound.");
        foreach (string pointer in value.RelativeValuePointers)
            ValidateAscii(pointer, MaximumPointerBytes, "capability pointer");
        int expected = value.Kind switch
        {
            GatewayEditorCapabilityKind.None or GatewayEditorCapabilityKind.InstalledFamily or
                GatewayEditorCapabilityKind.InspectionSpill => 0,
            GatewayEditorCapabilityKind.ResilienceProfile => 2,
            _ => 1,
        };
        if (value.RelativeValuePointers.Length != expected)
            throw new InvalidOperationException("Capability pointer count is invalid.");
    }

    private static void ValidatePath(ImmutableArray<GatewayEditorOccurrenceStep> path, bool allowEmpty)
    {
        if (path.IsDefault || path.Length > MaximumOccurrenceSteps || (!allowEmpty && path.IsEmpty))
            throw new InvalidOperationException("Editor occurrence path is malformed or over bound.");
        foreach (GatewayEditorOccurrenceStep step in path)
        {
            if (step is null)
                throw new InvalidOperationException("Editor occurrence path contains null.");
            ValidateEnum(step.Kind);
            switch (step.Kind)
            {
                case GatewayEditorOccurrenceStepKind.Property:
                case GatewayEditorOccurrenceStepKind.Reference:
                    ValidateAscii(step.Value, step.Kind == GatewayEditorOccurrenceStepKind.Reference
                        ? MaximumSchemaReferenceBytes : MaximumPointerBytes, "occurrence value");
                    if (step.SecondaryValue is not null)
                        throw new InvalidOperationException("Occurrence secondary value is invalid.");
                    break;
                case GatewayEditorOccurrenceStepKind.Items:
                    if (step.Value is not null || step.SecondaryValue is not null)
                        throw new InvalidOperationException("Items occurrence is invalid.");
                    break;
                case GatewayEditorOccurrenceStepKind.UnionBranch:
                    ValidateAscii(step.Value, MaximumPointerBytes, "union discriminator");
                    ValidateAscii(step.SecondaryValue, MaximumPointerBytes, "union value");
                    break;
                default:
                    throw new InvalidOperationException("Occurrence kind is invalid.");
            }
        }
    }

    private static void ValidateAscii(string? value, int maximumBytes, string name)
    {
        ValidateUtf8(value, maximumBytes, name);
        if (value!.Any(static c => c is < '!' or > '~'))
            throw new InvalidOperationException($"Editor {name} must be visible ASCII.");
    }

    private static void ValidateUtf8(string? value, int maximumBytes, string name)
    {
        if (string.IsNullOrEmpty(value) || !value.IsNormalized(NormalizationForm.FormC) ||
            Encoding.UTF8.GetByteCount(value) > maximumBytes)
            throw new InvalidOperationException($"Editor {name} is malformed or over bound.");
        foreach (Rune rune in value.EnumerateRunes())
            if (Rune.GetUnicodeCategory(rune) is System.Globalization.UnicodeCategory.Control)
                throw new InvalidOperationException($"Editor {name} contains a control.");
    }

    private static int ComparePath(ImmutableArray<GatewayEditorOccurrenceStep> left,
        ImmutableArray<GatewayEditorOccurrenceStep> right)
    {
        for (int index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            int result = left[index].Kind.CompareTo(right[index].Kind);
            if (result == 0) result = string.CompareOrdinal(left[index].Value, right[index].Value);
            if (result == 0) result = string.CompareOrdinal(left[index].SecondaryValue, right[index].SecondaryValue);
            if (result != 0) return result;
        }
        return left.Length.CompareTo(right.Length);
    }

    private static int CompareConstraint(GatewayEditorConstraintTarget left, GatewayEditorConstraintTarget right)
    {
        int result = string.CompareOrdinal(left.SchemaRef, right.SchemaRef);
        if (result == 0) result = string.CompareOrdinal(left.PropertyPointer, right.PropertyPointer);
        return result == 0 ? left.AppliesTo.CompareTo(right.AppliesTo) : result;
    }

    private static void ValidateEnum<T>(T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new InvalidOperationException("Editor ledger contains an unknown enum value.");
    }

    private static byte[] Hash(ReadOnlySpan<byte> value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd.gateway.editor-ledger.v1\0"u8);
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
        return hash.GetHashAndReset();
    }

    private static JsonObject ProjectEnvelope(GatewayDeclarationEditorLedgerEnvelope value) => new()
    {
        ["declarationSchemaRef"] = value.DeclarationSchemaRef,
        ["records"] = new JsonArray(value.Records.Select(ProjectRecord).ToArray<JsonNode?>()),
        ["schemaVersion"] = (int)value.SchemaVersion,
    };

    private static JsonObject ProjectRecord(GatewayEditorFieldRecord value) => new()
    {
        ["capability"] = new JsonObject
        {
            ["kind"] = Token(value.Capability.Kind),
            ["relativeValuePointers"] = new JsonArray(value.Capability.RelativeValuePointers
                .Select(static pointer => (JsonNode?)JsonValue.Create(pointer)).ToArray()),
        },
        ["compositionScope"] = Token(value.CompositionScope),
        ["disposition"] = Token(value.Disposition),
        ["family"] = Token(value.Family),
        ["helpCode"] = value.HelpCode,
        ["inheritance"] = Token(value.Inheritance),
        ["inheritanceSourceOccurrencePath"] = ProjectPath(value.InheritanceSourceOccurrencePath),
        ["omittedValueJson"] = value.OmittedValueJson,
        ["omittedValueKind"] = Token(value.OmittedValueKind),
        ["presentationGroup"] = Token(value.PresentationGroup),
        ["quickRouteStep"] = Token(value.QuickRouteStep),
        ["structuralReason"] = Token(value.StructuralReason),
        ["target"] = ProjectTarget(value.Target),
    };

    private static JsonObject ProjectTarget(GatewayEditorFieldTarget value) => new()
    {
        ["componentSchemaPointer"] = value.ComponentSchemaPointer,
        ["componentSchemaRef"] = value.ComponentSchemaRef,
        ["constraintTargets"] = new JsonArray(value.ConstraintTargets.Select(static target =>
            (JsonNode)new JsonObject
            {
                ["appliesTo"] = Token(target.AppliesTo),
                ["propertyPointer"] = target.PropertyPointer,
                ["schemaRef"] = target.SchemaRef,
            }).ToArray()),
        ["occurrencePath"] = ProjectPath(value.OccurrencePath),
    };

    private static JsonArray ProjectPath(ImmutableArray<GatewayEditorOccurrenceStep> value) =>
        new(value.Select(static step => (JsonNode)new JsonObject
        {
            ["kind"] = Token(step.Kind),
            ["secondaryValue"] = step.SecondaryValue,
            ["value"] = step.Value,
        }).ToArray());

    private static string Token<T>(T value) where T : struct, Enum => value switch
    {
        GatewayEditorFieldDisposition.Editable => "editable",
        GatewayEditorFieldDisposition.StructuralOnly => "structural-only",
        GatewayEditorCompositionScope.Document => "document",
        GatewayEditorCompositionScope.RootDefaults => "root-defaults",
        GatewayEditorCompositionScope.Route => "route",
        GatewayEditorCompositionScope.RouteMatch => "route-match",
        GatewayEditorCompositionScope.Upstream => "upstream",
        GatewayEditorCompositionScope.EndpointSource => "endpoint-source",
        GatewayEditorCompositionScope.Destination => "destination",
        GatewayEditorCompositionScope.Definition => "definition",
        GatewayEditorCompositionScope.Metadata => "metadata",
        GatewayEditorCompositionScope.Transform => "transform",
        GatewayEditorOmittedValueKind.Absent => "absent",
        GatewayEditorOmittedValueKind.CanonicalJson => "canonical-json",
        GatewayEditorInheritanceKind.None => "none",
        GatewayEditorInheritanceKind.RootInheritedAndRouteReplaced => "root-inherited-and-route-replaced",
        GatewayEditorDeclarationFamily family => Kebab(family.ToString()),
        GatewayEditorCapabilityKind capability => Kebab(capability.ToString()),
        GatewayEditorPresentationGroup group => Kebab(group.ToString()),
        GatewayEditorQuickRouteStep step => Kebab(step.ToString()),
        GatewayEditorStructuralReason reason => Kebab(reason.ToString()),
        GatewayEditorConstraintAppliesTo appliesTo => Kebab(appliesTo.ToString()),
        GatewayEditorOccurrenceStepKind kind => Kebab(kind.ToString()),
        _ => throw new InvalidOperationException("Editor enum value is unsupported."),
    };

    private static string Kebab(string value) => string.Concat(value.Select((character, index) =>
        char.IsUpper(character) && index > 0
            ? "-" + char.ToLowerInvariant(character)
            : char.ToLowerInvariant(character).ToString()));
}
