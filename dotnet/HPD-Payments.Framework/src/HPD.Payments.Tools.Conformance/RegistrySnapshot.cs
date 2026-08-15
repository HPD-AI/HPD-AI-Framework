using System.Buffers;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace HPD.Payments.Tools.Conformance;

/// <summary>Loads the frozen registries on a bounded cold path without dynamic type activation.</summary>
internal sealed class RegistrySnapshot
{
    private readonly RegistryRoute[] _routes;
    private readonly BaselineClaim[] _claims;
    /// <summary>Gets the canonical registry's declared content digest.</summary>
    internal string CanonicalDigest { get; }
    /// <summary>Gets the claim matrix's declared content digest.</summary>
    internal string ClaimMatrixDigest { get; }
    /// <summary>Gets owned canonical routes.</summary>
    internal IReadOnlyList<RegistryRoute> Routes => Array.AsReadOnly(_routes);
    /// <summary>Gets owned baseline claims.</summary>
    internal IReadOnlyList<BaselineClaim> Claims => Array.AsReadOnly(_claims);

    private RegistrySnapshot(string canonicalDigest, string claimMatrixDigest,
        RegistryRoute[] routes, BaselineClaim[] claims) =>
        (CanonicalDigest, ClaimMatrixDigest, _routes, _claims) = (canonicalDigest, claimMatrixDigest, routes, claims);

    /// <summary>Loads and cross-validates bounded canonical and claim registry bytes.</summary>
    internal static RegistrySnapshot Load(ReadOnlyMemory<byte> canonicalBytes, ReadOnlyMemory<byte> claimBytes)
    {
        if (canonicalBytes.Length is < 2 or > 32_000_000 || claimBytes.Length is < 2 or > 32_000_000)
            throw new ArgumentOutOfRangeException(nameof(canonicalBytes));
        using var canonical = JsonDocument.Parse(canonicalBytes, Options);
        using var matrix = JsonDocument.Parse(claimBytes, Options);
        var canonicalRoot = canonical.RootElement;
        var matrixRoot = matrix.RootElement;
        var canonicalDigest = RequiredString(canonicalRoot, "contentDigest");
        var matrixDigest = RequiredString(matrixRoot, "contentDigest");
        if (!StringComparer.Ordinal.Equals(canonicalDigest, CanonicalContentDigest(canonicalRoot)) ||
            !StringComparer.Ordinal.Equals(matrixDigest, CanonicalContentDigest(matrixRoot)))
            throw new InvalidDataException("Registry content digest does not match canonical JSON bytes.");
        if (!StringComparer.Ordinal.Equals(RequiredString(matrixRoot, "canonicalRegistryDigest"), canonicalDigest))
            throw new InvalidDataException("Claim matrix binds a different canonical registry digest.");

        var routes = canonicalRoot.GetProperty("capabilities").EnumerateArray().Select(static item =>
            new RegistryRoute(RequiredString(item, "id"), RequiredString(item, "prefix"),
                RequiredString(item, "frozenOwnerOrSupportingConcept"), RequiredString(item, "candidateContractFamily"),
                RequiredString(item, "proofState"),
                item.GetProperty("res009Affected").GetBoolean(), RequiredStrings(item, "authorityOwners"),
                RequiredIntegers(item, "workflows"), RequiredStrings(item, "hazards"),
                RequiredStrings(item, "ownershipCells"), RequiredStrings(item, "extensionCells"))).ToArray();
        var claims = matrixRoot.GetProperty("claims").EnumerateArray().Select(static item =>
            new BaselineClaim(RequiredString(item, "cellId"), RequiredString(item, "canonicalId"),
                RequiredString(item, "applicability"), RequiredString(item, "expectedProofState"),
                RequiredString(item, "res009Status"))).ToArray();
        Validate(routes, claims);
        return new(canonicalDigest, matrixDigest, routes, claims);
    }

    private static void Validate(RegistryRoute[] routes, BaselineClaim[] claims)
    {
        if (routes.Length != 179 || claims.Length != 179) throw new InvalidDataException("Frozen registries must contain exactly 179 rows each.");
        if (routes.Select(static x => x.Id).Distinct(StringComparer.Ordinal).Count() != routes.Length ||
            claims.Select(static x => x.CellId).Distinct(StringComparer.Ordinal).Count() != claims.Length ||
            claims.Select(static x => x.CanonicalId).Distinct(StringComparer.Ordinal).Count() != claims.Length)
            throw new InvalidDataException("Frozen registries contain duplicate identities.");
        foreach (var route in routes)
        {
            if (!route.Id.StartsWith(route.Prefix + '-', StringComparison.Ordinal) || route.ProofState != "Untested")
                throw new InvalidDataException("Canonical route prefix or proof state is invalid.");
        }
        var routeIds = routes.Select(static x => x.Id).ToHashSet(StringComparer.Ordinal);
        if (!routeIds.SetEquals(claims.Select(static x => x.CanonicalId)))
            throw new InvalidDataException("Claim routes do not exactly equal canonical routes.");
        foreach (var claim in claims)
        {
            if (claim.ExpectedProofState != "Untested" || claim.CellId != "BASELINE-" + claim.CanonicalId)
                throw new InvalidDataException("Baseline claim identity or proof state is invalid.");
            var blocked = claim.Applicability == "Blocked" && claim.Res009Status == "BlockedPendingExplicitAcceptance";
            var pending = claim.Applicability == "ApplicablePendingSelection" && claim.Res009Status == "NotAffected";
            if (!blocked && !pending) throw new InvalidDataException("Baseline applicability and RES-009 disposition conflict.");
        }
        if (claims.Count(static x => x.Applicability == "Blocked") != 28 || routes.Count(static x => x.Res009Affected) != 28)
            throw new InvalidDataException("RES-009 blocked counts changed.");
        if (routes.Select(static x => x.Prefix).Distinct(StringComparer.Ordinal).Count() != 33 ||
            !Enumerable.Range(1, 6).Select(static x => $"TEST-{x:000}").All(routeIds.Contains))
            throw new InvalidDataException("Prefix or TEST route inventory changed.");
        var owners = routes.SelectMany(static x => x.AuthorityOwners).ToHashSet(StringComparer.Ordinal);
        if (!owners.SetEquals(FrozenAuthorities) ||
            !routes.SelectMany(static x => x.Workflows).ToHashSet().SetEquals(Enumerable.Range(1, 20)))
            throw new InvalidDataException("Authority or workflow inventory changed.");
        var hazards = Enumerable.Range(0, 14).Select(static x => $"H{x}").ToArray();
        var own = Enumerable.Range(1, 12).Select(static x => $"OWN-{x:00}").ToArray();
        foreach (var route in routes)
            if (!route.Hazards.SequenceEqual(hazards) || !route.OwnershipCells.SequenceEqual(own) ||
                !route.ExtensionCells.SequenceEqual(FrozenExtensionCells))
                throw new InvalidDataException($"Route {route.Id} changed its H/OWN/EXT inventory.");
    }

    private static string RequiredString(JsonElement element, string property)
    {
        var value = element.GetProperty(property).GetString();
        if (string.IsNullOrEmpty(value) || value.Length > 16_384) throw new InvalidDataException($"Missing or over-bound registry property: {property}.");
        return value;
    }

    private static string[] RequiredStrings(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(static x => x.GetString() ?? throw new InvalidDataException("Null registry string.")).ToArray();

    private static int[] RequiredIntegers(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(static x => x.GetInt32()).ToArray();

    private static string CanonicalContentDigest(JsonElement root)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            WriteCanonical(writer, root, isRoot: true);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element, bool isRoot = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().Where(x => !isRoot || x.Name != "contentDigest")
                    .OrderBy(static x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item); writer.WriteEndArray(); break;
            case JsonValueKind.String: writer.WriteStringValue(element.GetString()); break;
            case JsonValueKind.Number: writer.WriteRawValue(element.GetRawText(), skipInputValidation: true); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new InvalidDataException("Unsupported registry JSON token.");
        }
    }

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 32, CommentHandling = JsonCommentHandling.Disallow };
    private static readonly string[] FrozenAuthorities =
    [
        "Scoped Identity", "Agreement", "Requested Transition", "Effective Commercial Fact", "Measured Fact",
        "Measurement Generation", "Valuation", "Obligation", "Issuance Fact", "Held Position", "Value Movement",
        "Entitlement Grant/Removal Fact", "Restriction Fact", "Capability Evidence", "External Effect",
        "Work Requirement", "Publication Obligation",
    ];
    private static readonly string[] FrozenExtensionCells =
    [
        "EXT-DET-01", "EXT-EFFECT-02", "EXT-WORK-03", "EXT-RESOURCE-04", "EXT-ROTATE-05",
        "EXT-UPGRADE-06", "EXT-LANE-07", "EXT-SEC-08", "EXT-SER-09",
    ];
}

/// <summary>Retains the exact cold-path fields needed to route a canonical claim.</summary>
internal sealed record RegistryRoute(string Id, string Prefix, string OwnerOrSupportingConcept, string CandidateContractFamily, string ProofState,
    bool Res009Affected, IReadOnlyList<string> AuthorityOwners, IReadOnlyList<int> Workflows,
    IReadOnlyList<string> Hazards, IReadOnlyList<string> OwnershipCells, IReadOnlyList<string> ExtensionCells);

/// <summary>Retains one exact baseline claim disposition.</summary>
internal sealed record BaselineClaim(string CellId, string CanonicalId, string Applicability,
    string ExpectedProofState, string Res009Status);
