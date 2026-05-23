#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Contains("--help", StringComparer.OrdinalIgnoreCase)
    || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
{
    PrintUsage();
    return 0;
}

try
{
    var specPath = GetOptionValue(args, "--spec")
        ?? throw new InvalidOperationException("--spec <PATH> is required.");
    var outputPath = GetOptionValue(args, "--out")
        ?? throw new InvalidOperationException("--out <PATH> is required.");

    ValidateArguments(args);
    BuildManifest(specPath, outputPath);
    Console.WriteLine($"External parity manifest written: {outputPath}");
    return 0;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (JsonException ex)
{
    Console.Error.WriteLine($"Invalid JSON: {ex.Message}");
    return 1;
}

static void BuildManifest(string specPath, string outputPath)
{
    var specFullPath = Path.GetFullPath(specPath);
    Require(File.Exists(specFullPath), $"Spec file does not exist: {specPath}");

    var outputFullPath = Path.GetFullPath(outputPath);
    var outputDirectory = Path.GetDirectoryName(outputFullPath)
        ?? throw new InvalidOperationException($"Could not determine output directory for: {outputPath}");
    Directory.CreateDirectory(outputDirectory);

    var root = JsonNode.Parse(File.ReadAllText(specFullPath))?.AsObject()
        ?? throw new InvalidOperationException("Spec root must be a JSON object.");

    ValidateAllowedProperties(
        root,
        "External parity manifest spec",
        [
            "$schema",
            "ReportVersion",
            "Passed",
            "Provider",
            "DatasetId",
            "GitCommit",
            "AcceptedLimitations",
            "UnsupportedFeatures",
            "Mismatches",
            "Fixtures"
        ]);

    root["$schema"] = "Rhodium.ExternalParityManifest.schema.json";
    root["ReportVersion"] = 1;
    root["Passed"] = true;
    if (string.IsNullOrWhiteSpace(root["GitCommit"]?.GetValue<string>()))
        root["GitCommit"] = Git("rev-parse", "HEAD");

    RequireString(root, "Provider");
    RequireString(root, "DatasetId");
    ValidateGitCommit(RequireString(root, "GitCommit"));
    RequireArray(root, "AcceptedLimitations");
    RequireArray(root, "UnsupportedFeatures");
    ValidateMismatches(root);

    var fixtures = RequireArray(root, "Fixtures");
    Require(fixtures.Count > 0, "Spec Fixtures must contain at least one fixture.");
    var fixtureKinds = new HashSet<string>(StringComparer.Ordinal);
    foreach (var item in fixtures)
    {
        var fixture = item?.AsObject()
            ?? throw new InvalidOperationException("Each fixture must be a JSON object.");
        fixtureKinds.Add(NormalizeFixture(fixture, outputDirectory));
    }

    foreach (var requiredKind in RequiredFixtureKinds())
        Require(fixtureKinds.Contains(requiredKind), $"Spec missing required FixtureKind: {requiredKind}");

    File.WriteAllText(
        outputFullPath,
        root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

static string NormalizeFixture(JsonObject fixture, string manifestDirectory)
{
    ValidateAllowedProperties(
        fixture,
        "External parity fixture spec",
        [
            "Name",
            "FixtureKind",
            "Provider",
            "DatasetId",
            "ExpectedResultSource",
            "CoveredDateRange",
            "InstrumentSet",
            "AccountType",
            "Passed",
            "InputArtifactPath",
            "OutputArtifactPath",
            "ComparisonReportPath"
        ]);

    fixture["Passed"] = true;
    var name = RequireString(fixture, "Name");
    var fixtureKind = RequireString(fixture, "FixtureKind");
    Require(
        fixtureKind is "TradingCalendar"
            or "AccountStatement"
            or "MarginLiquidationFinancing"
            or "MarketReplayExecution"
            or "VenueOrderPolicy"
            or "CrossVenueRouting",
        $"Fixture has an invalid FixtureKind: {name}");

    RequireString(fixture, "Provider");
    RequireString(fixture, "DatasetId");
    RequireString(fixture, "ExpectedResultSource");
    RequireString(fixture, "CoveredDateRange");
    RequireString(fixture, "InstrumentSet");
    RequireString(fixture, "AccountType");

    fixture["InputArtifactSha256"] = ComputeArtifactSha256(fixture, "InputArtifactPath", name, manifestDirectory);
    fixture["OutputArtifactSha256"] = ComputeArtifactSha256(fixture, "OutputArtifactPath", name, manifestDirectory);
    fixture["ComparisonReportSha256"] = ComputeArtifactSha256(fixture, "ComparisonReportPath", name, manifestDirectory);
    return fixtureKind;
}

static string[] RequiredFixtureKinds()
    => [
        "TradingCalendar",
        "AccountStatement",
        "MarginLiquidationFinancing",
        "MarketReplayExecution",
        "VenueOrderPolicy",
        "CrossVenueRouting"
    ];

static void ValidateGitCommit(string manifestGitCommit)
{
    Require(manifestGitCommit.Length >= 12, "GitCommit must be empty or at least 12 characters.");
    Require(manifestGitCommit.All(static c => char.IsAsciiHexDigit(c)), "GitCommit must be a hexadecimal commit hash.");

    var currentGitCommit = Git("rev-parse", "HEAD");
    Require(!string.IsNullOrWhiteSpace(currentGitCommit), "Could not determine current git commit.");
    Require(
        string.Equals(currentGitCommit, manifestGitCommit, StringComparison.OrdinalIgnoreCase)
            || currentGitCommit.StartsWith(manifestGitCommit, StringComparison.OrdinalIgnoreCase),
        "GitCommit does not match the current checkout.");
}

static string ComputeArtifactSha256(JsonObject fixture, string pathPropertyName, string fixtureName, string manifestDirectory)
{
    var artifactPath = RequireString(fixture, pathPropertyName);
    var resolvedPath = Path.IsPathRooted(artifactPath)
        ? Path.GetFullPath(artifactPath)
        : Path.GetFullPath(Path.Combine(manifestDirectory, artifactPath));
    Require(
        IsPathWithinDirectory(resolvedPath, manifestDirectory),
        $"Fixture {pathPropertyName} must stay inside the manifest directory: {artifactPath}");
    Require(File.Exists(resolvedPath), $"Fixture {pathPropertyName} points to a missing file: {artifactPath}");

    using var stream = File.OpenRead(resolvedPath);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static void ValidateMismatches(JsonObject root)
{
    var mismatches = RequireArray(root, "Mismatches");
    foreach (var item in mismatches)
    {
        var mismatch = item?.AsObject()
            ?? throw new InvalidOperationException("Each mismatch must be a JSON object.");
        ValidateAllowedProperties(mismatch, "External parity mismatch spec", ["Name", "Description", "Classification"]);
        var name = RequireString(mismatch, "Name");
        RequireString(mismatch, "Description");
        var classification = RequireString(mismatch, "Classification");
        Require(
            classification is "ProviderDataAmbiguity" or "PolicyDifference" or "UnsupportedFeature",
            $"Mismatch has an invalid or non-passing Classification: {name}");
    }
}

static string RequireString(JsonObject node, string propertyName)
{
    if (!node.TryGetPropertyValue(propertyName, out var value)
        || value is null
        || value.GetValueKind() != JsonValueKind.String)
    {
        throw new InvalidOperationException($"Missing string property: {propertyName}");
    }

    var text = value.GetValue<string>();
    Require(!string.IsNullOrWhiteSpace(text), $"Empty string property: {propertyName}");
    return text;
}

static JsonArray RequireArray(JsonObject node, string propertyName)
{
    if (!node.TryGetPropertyValue(propertyName, out var value)
        || value is null
        || value.GetValueKind() != JsonValueKind.Array)
    {
        throw new InvalidOperationException($"Missing array property: {propertyName}");
    }

    return value.AsArray();
}

static void ValidateAllowedProperties(JsonObject node, string elementName, string[] allowedProperties)
{
    foreach (var property in node)
        Require(Array.IndexOf(allowedProperties, property.Key) >= 0, $"{elementName} contains an unknown property: {property.Key}");
}

static bool IsPathWithinDirectory(string path, string directory)
{
    var fullPath = Path.GetFullPath(path);
    var fullDirectory = Path.GetFullPath(directory);
    var relativePath = Path.GetRelativePath(fullDirectory, fullPath);

    return relativePath.Length == 0
        || (!Path.IsPathRooted(relativePath)
            && !relativePath.Equals("..", StringComparison.Ordinal)
            && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
}

static string? GetOptionValue(IReadOnlyList<string> values, string name)
{
    var prefix = name + "=";
    for (var i = 0; i < values.Count - 1; i++)
    {
        if (values[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return values[i][prefix.Length..];

        if (string.Equals(values[i], name, StringComparison.OrdinalIgnoreCase))
            return values[i + 1];
    }

    if (values.Count > 0 && values[^1].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        return values[^1][prefix.Length..];

    return null;
}

static void ValidateArguments(IReadOnlyList<string> values)
{
    for (var i = 0; i < values.Count; i++)
    {
        var value = values[i];
        if (!value.StartsWith("-", StringComparison.Ordinal))
            continue;

        if (string.Equals(value, "--spec", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "--out", StringComparison.OrdinalIgnoreCase))
        {
            if (i == values.Count - 1 || values[i + 1].StartsWith("-", StringComparison.Ordinal))
                throw new InvalidOperationException($"{value} requires a path value.");
            i++;
            continue;
        }

        if (value.StartsWith("--spec=", StringComparison.OrdinalIgnoreCase))
        {
            Require(value.Length > "--spec=".Length, "--spec requires a non-empty path value.");
            continue;
        }

        if (value.StartsWith("--out=", StringComparison.OrdinalIgnoreCase))
        {
            Require(value.Length > "--out=".Length, "--out requires a non-empty path value.");
            continue;
        }

        if (string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        throw new InvalidOperationException($"Unknown manifest builder option: {value}. Run with -- --help for usage.");
    }
}

static string? Git(params string[] arguments)
{
    var startInfo = new ProcessStartInfo("git")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };

    foreach (var argument in arguments)
        startInfo.ArgumentList.Add(argument);

    using var process = Process.Start(startInfo);
    if (process is null)
        return null;

    var output = process.StandardOutput.ReadToEnd().Trim();
    process.WaitForExit();
    return process.ExitCode == 0 ? output : null;
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        Usage:
          dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/build-external-parity-manifest.cs -- --spec <PATH> --out <PATH>

        Options:
          --spec <PATH>     JSON manifest spec without computed SHA-256 fields.
          --spec=<PATH>     Equivalent spec path form for CI argument lists.
          --out <PATH>      Output external parity manifest path.
          --out=<PATH>      Equivalent output path form for CI argument lists.
          --help, -h        Show this help text.

        Artifact paths in the spec are resolved relative to the output manifest directory.
        The builder requires artifacts to stay inside that directory and writes SHA-256
        hashes for input, output, and comparison artifacts.
        """);
}
