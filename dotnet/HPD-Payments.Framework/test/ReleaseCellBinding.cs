using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class ReleaseCellBinding
{
    private const string CanonicalDigest = "sha256:3c623b0dfbf040f34e30dfdc15a20629ada211e5b9493495fbde5300b2326ca9";
    private const string ClaimDigest = "sha256:e2ba5f55f9fe10b0ef13f422d15640057d1c4eda0610d1a7ef67a212fe1b1a05";

    internal static bool ValidateAndExecute(string invokedCommand)
    {
        var suppliedRoute = Environment.GetEnvironmentVariable("HPD_PAYMENTS_RELEASE_ROUTE");
        var suppliedSeed = Environment.GetEnvironmentVariable("HPD_PAYMENTS_RELEASE_SEED");
        var suppliedCell = Environment.GetEnvironmentVariable("HPD_PAYMENTS_RELEASE_CELL");
        var suppliedSource = Environment.GetEnvironmentVariable("HPD_PAYMENTS_RELEASE_SOURCE");
        var suppliedCommand = Environment.GetEnvironmentVariable("HPD_PAYMENTS_RELEASE_COMMAND");
        if (suppliedRoute is null && suppliedSeed is null && suppliedCell is null && suppliedSource is null && suppliedCommand is null)
            return true;
        try
        {
            if (suppliedRoute is null || suppliedSeed is null || suppliedCell is null || suppliedSource is null ||
                suppliedCommand is null || suppliedCommand != invokedCommand)
                throw new InvalidDataException("Incomplete or wrong release-oracle binding.");

            var root = Directory.GetCurrentDirectory();
            using var canonical = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "eng/registry/canonical-capabilities.json")));
            using var claims = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "eng/registry/claim-matrix.json")));
            if (canonical.RootElement.GetProperty("contentDigest").GetString() != CanonicalDigest ||
                claims.RootElement.GetProperty("contentDigest").GetString() != ClaimDigest ||
                claims.RootElement.GetProperty("canonicalRegistryDigest").GetString() != CanonicalDigest)
                throw new InvalidDataException("Untrusted registry digest.");

            var routes = canonical.RootElement.GetProperty("capabilities").EnumerateArray()
                .Where(x => x.GetProperty("id").GetString() == suppliedRoute).ToArray();
            var matchingClaims = claims.RootElement.GetProperty("claims").EnumerateArray()
                .Where(x => x.GetProperty("canonicalId").GetString() == suppliedRoute).ToArray();
            if (routes.Length != 1 || matchingClaims.Length != 1)
                throw new InvalidDataException("Unknown or duplicate canonical route.");
            var route = routes[0];
            var claim = matchingClaims[0];
            var prefix = Required(route, "prefix");
            var expectedCommand = RouteCommand(prefix);
            if (expectedCommand != invokedCommand) throw new InvalidDataException("Route does not belong to invoked oracle.");

            var actualSource = CaptureSource(root);
            if (suppliedSource != actualSource) throw new InvalidDataException("Stale source binding.");
            var expectedSeed = Hex(Join(actualSource, suppliedRoute, expectedCommand));
            if (suppliedSeed != expectedSeed) throw new InvalidDataException("Forged route seed.");
            var expectedCell = "sha256:" + Hex(Cell(route, suppliedRoute, expectedCommand));
            if (suppliedCell != expectedCell) throw new InvalidDataException("Forged proof cell.");

            var inventory = ExecuteRouteAssertions(route, claim, suppliedSeed, expectedCommand);
            Console.WriteLine($"PASS release-cell route={suppliedRoute} seed={suppliedSeed} cell={suppliedCell} assertions={inventory.Digest} total={inventory.Total} passed={inventory.Total}");
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or CryptographicException)
        {
            Console.Error.WriteLine($"Invalid exact release-cell binding: {error.Message}");
            return false;
        }
    }

    private static (string Digest, int Total) ExecuteRouteAssertions(JsonElement route, JsonElement claim, string seed, string command)
    {
        var routeId = Required(route, "id");
        var prefix = Required(route, "prefix");
        var assertions = new List<string>
        {
            $"route:{routeId}", $"prefix:{prefix}", $"command:{command}",
            $"claim:{Required(claim, "cellId")}", $"family:{Required(route, "candidateContractFamily")}",
            $"oracle:{Required(route, "executableOracle")}", $"security:{Required(route, "securityClass")}",
            $"policy:{Required(route, "externalPolicyCertificationBoundary")}",
        };
        if (Required(claim, "cellId") != "BASELINE-" + routeId || !routeId.StartsWith(prefix + '-', StringComparison.Ordinal))
            throw new InvalidDataException("Route-specific claim identity failed.");
        foreach (var name in new[] { "hazards", "ownershipCells", "extensionCells", "workflows", "authorityOwners" })
        {
            var values = route.GetProperty(name).EnumerateArray().Select(static x => x.ToString()).ToArray();
            assertions.Add($"{name}:{string.Join(',', values)}");
        }

        var seedBytes = Convert.FromHexString(seed);
        var routeBytes = Encoding.UTF8.GetBytes(route.GetRawText());
        for (var index = 0; index < 16; index++)
        {
            var position = (seedBytes[index] * 257 + seedBytes[index + 16]) % routeBytes.Length;
            var mutated = routeBytes.ToArray();
            mutated[position] ^= (byte)(1 + seedBytes[index + 8] % 255);
            if (mutated.AsSpan().SequenceEqual(routeBytes)) throw new InvalidDataException("Seeded route case was inert.");
            assertions.Add($"seeded-{index:00}:{position}:{Hex(mutated)}");
        }
        return ("sha256:" + Hex(Join(assertions.ToArray())), assertions.Count);
    }

    private static string Cell(JsonElement route, string routeId, string command)
    {
        var prefix = Required(route, "prefix");
        var provider = prefix is "CHK" or "CONN" or "DISP" or "PAY" or "PAYOUT" or "REF" or "ROUT";
        var owners = route.GetProperty("authorityOwners").EnumerateArray().Select(static x => x.GetString()!).ToArray();
        var owner = owners.Length == 0 ? Required(route, "frozenOwnerOrSupportingConcept") : owners[0];
        var own = route.GetProperty("ownershipCells")[0].GetString()!;
        var extension = route.GetProperty("extensionCells")[0].GetString()!;
        return Join(routeId, owner, Required(route, "candidateContractFamily"), own, extension,
            "EmbeddedInMemory", "Static", "InMemory", provider ? "Simulator" : "NotApplicable",
            provider ? "local-simulator" : "NotApplicable", provider ? "deterministic" : "NotApplicable",
            provider ? "v1" : "NotApplicable", "managed-release", "portable-managed", "macOS", "arm64",
            "dotnet-sdk-10.0.301", "net10.0", "Roslyn", "NotApplicable", "false", command, routeId);
    }

    private static string CaptureSource(string root)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var included in new[] { "src", "test", "perf", "eng/registry", "eng/commands" })
            foreach (var path in Directory.EnumerateFiles(Path.Combine(root, included), "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                if (relative.Split('/').Any(static x => x is "bin" or "obj")) continue;
                paths.Add(path);
            }
        foreach (var included in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "HPD-Payments.slnx" })
            paths.Add(Path.Combine(root, included));
        var entries = paths.Select(path =>
        {
            var bytes = File.ReadAllBytes(path);
            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            return Join(relative, bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), Hex(bytes));
        }).ToArray();
        return "sha256:" + Hex(Join(entries));
    }

    private static string RouteCommand(string prefix) => prefix switch
    {
        "TEST" => "validate-proof",
        "CHK" or "CONN" or "DISP" or "PAY" or "PAYOUT" or "REF" or "ROUT" => "test-simulator-certification",
        "EVT" or "OBS" or "WORK" => "test-worker",
        _ => "test-runtime-baseline",
    };

    private static string Required(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? throw new InvalidDataException($"Missing route field {name}.");
    private static string Join(params string[] values) => string.Concat(values.Select(value => Encoding.UTF8.GetByteCount(value) + ":" + value));
    private static string Hex(string value) => Hex(Encoding.UTF8.GetBytes(value));
    private static string Hex(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
}
