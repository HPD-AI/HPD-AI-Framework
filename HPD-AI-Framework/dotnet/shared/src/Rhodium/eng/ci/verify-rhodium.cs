#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const string RhodiumRoot = "HPD-AI-Framework/dotnet/shared/src/Rhodium";
const string LogPath = "rhodium-verify.log";
const int VectorSmokeVariantCount = 10_000;
const int VectorSmokeBarCount = 100;
const int ReplayCertificationScenarioCount = 8;
const int TargetHardwareLogicalProcessorCount = 64;
const int VectorSmokeMaxElapsedSeconds = 300;

if (args.Contains("--help", StringComparer.OrdinalIgnoreCase)
    || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
{
    PrintUsage();
    return 0;
}

if (args.Contains("--list-gates", StringComparer.OrdinalIgnoreCase))
{
    PrintGates();
    return 0;
}

try
{
    ValidateArguments(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

var skipVectorSmoke = args.Contains("--skip-vector-smoke", StringComparer.OrdinalIgnoreCase);
var skipReplayCertification = args.Contains("--skip-replay-certification", StringComparer.OrdinalIgnoreCase);
var skipTests = args.Contains("--skip-tests", StringComparer.OrdinalIgnoreCase);
var requireReleaseEvidence = args.Contains("--require-release-evidence", StringComparer.OrdinalIgnoreCase);
var requireExternalParity = args.Contains("--require-external-parity", StringComparer.OrdinalIgnoreCase);
var externalParityManifestPath = GetOptionValue(args, "--external-parity-manifest");
if (requireExternalParity && string.IsNullOrWhiteSpace(externalParityManifestPath))
{
    Console.Error.WriteLine("--require-external-parity requires --external-parity-manifest <PATH>.");
    return 2;
}

if (skipTests && skipVectorSmoke && skipReplayCertification && string.IsNullOrWhiteSpace(externalParityManifestPath))
{
    Console.Error.WriteLine("Verifier would skip tests, vector smoke, replay certification smoke, and external parity validation. At least one gate must run.");
    return 2;
}

var keepReports = args.Contains("--keep-reports", StringComparer.OrdinalIgnoreCase);
var keepLog = args.Contains("--keep-log", StringComparer.OrdinalIgnoreCase);
var requireCleanGit = args.Contains("--require-clean-git", StringComparer.OrdinalIgnoreCase);
var requireTargetHardware = args.Contains("--require-target-hardware", StringComparer.OrdinalIgnoreCase);
var reportDirectoryOption = GetOptionValue(args, "--report-dir");
if (requireReleaseEvidence
    && (!keepReports
        || !requireCleanGit
        || !requireTargetHardware
        || !requireExternalParity
        || string.IsNullOrWhiteSpace(reportDirectoryOption)
        || skipTests
        || skipVectorSmoke
        || skipReplayCertification))
{
    Console.Error.WriteLine("--require-release-evidence requires --keep-reports, --require-clean-git, --require-target-hardware, --require-external-parity, --report-dir, and all local gates enabled.");
    return 2;
}

if (requireTargetHardware && skipVectorSmoke)
{
    Console.Error.WriteLine("--require-target-hardware requires the vector smoke gate. Remove --skip-vector-smoke.");
    return 2;
}

var reportDirectory = reportDirectoryOption ?? $"{RhodiumRoot}/benchmarks";
if (requireReleaseEvidence && !IsPathWithinDirectory(externalParityManifestPath!, reportDirectory))
{
    Console.Error.WriteLine("--require-release-evidence requires --external-parity-manifest to be inside --report-dir.");
    return 2;
}

var vectorSmokeReportPath = Path.Combine(reportDirectory, "vector-smoke-report.json");
var replayCertificationReportPath = Path.Combine(reportDirectory, "replay-certification-smoke.json");
var certificationManifestPath = Path.Combine(reportDirectory, "rhodium-certification-manifest.json");
var certificationRunId = Guid.NewGuid().ToString("N");
var log = new StringBuilder();
var result = 1;
var externalParityPreflightValidated = false;
string[] testProjects =
[
    $"{RhodiumRoot}/test/Rhodium.Primitives.Tests/Rhodium.Primitives.Tests.csproj",
    $"{RhodiumRoot}/test/Rhodium.Events.Tests/Rhodium.Events.Tests.csproj",
    $"{RhodiumRoot}/test/Rhodium.Tensor.Tests/Rhodium.Tensor.Tests.csproj",
    $"{RhodiumRoot}/test/Rhodium.HFT.Tests/Rhodium.HFT.Tests.csproj",
    $"{RhodiumRoot}/test/Rhodium.Kernel.Tests/Rhodium.Kernel.Tests.csproj",
    $"{RhodiumRoot}/test/Rhodium.Control.Tests/Rhodium.Control.Tests.csproj",
    $"{RhodiumRoot}/test/Rhodium.Platform.Tests/Rhodium.Platform.Tests.csproj",
    $"{RhodiumRoot}/test/Rhodium.SourceGenerators.Tests/Rhodium.SourceGenerators.Tests.csproj",
    $"{RhodiumRoot}/test/Rhodium.Analytics.Tests/Rhodium.Analytics.Tests.csproj",
    $"{RhodiumRoot}/test/Rhodium.Simulation.Tests/Rhodium.Simulation.Tests.csproj",
    $"{RhodiumRoot}/test/Rhodium.Connectivity.Tests/Rhodium.Connectivity.Tests.csproj",
    $"{RhodiumRoot}/test/Rhodium.Risk.Tests/Rhodium.Risk.Tests.csproj",
    $"{RhodiumRoot}/test/Rhodium.Quant.Tests/Rhodium.Quant.Tests.csproj",
    $"{RhodiumRoot}/test/Rhodium.Options.Tests/Rhodium.Options.Tests.csproj",
    $"{RhodiumRoot}/test/Rhodium.Indicators.Tests/Rhodium.Indicators.Tests.csproj",
    $"{RhodiumRoot}/test/Rhodium.Data.Tests/Rhodium.Data.Tests.csproj"
];

try
{
    if (requireReleaseEvidence)
    {
        Console.WriteLine("### EXTERNAL PARITY PREFLIGHT");
        ValidateExternalParityManifest(externalParityManifestPath!);
        externalParityPreflightValidated = true;
    }

    if (!skipTests)
    {
        foreach (var project in testProjects)
        {
            Console.WriteLine($"### TEST {project}");
            var exitCode = await DotnetAsync(
                [
                    "test",
                    project,
                    "--nologo",
                    "-m:1",
                    "/nodeReuse:false",
                    "/clp:ErrorsOnly",
                    "--logger",
                    "console;verbosity=minimal"
                ],
                log);

            if (exitCode != 0)
            {
                result = exitCode;
                return result;
            }
        }
    }
    else
    {
        Console.WriteLine("### TESTS SKIPPED");
    }

    if (!skipVectorSmoke)
    {
        Console.WriteLine("### VECTOR SMOKE");
        var exitCode = await DotnetAsync(
            [
                "run",
                "--framework",
                "net10.0",
                "--project",
                $"{RhodiumRoot}/benchmarks/Rhodium.Benchmarks/Rhodium.Benchmarks.csproj",
                "--",
                "--vector-smoke",
                "--vector-smoke-report",
                vectorSmokeReportPath,
                "--certification-run-id",
                certificationRunId
            ],
            log);

        if (exitCode != 0)
        {
            result = exitCode;
            return result;
        }
    }

    if (!skipReplayCertification)
    {
        Console.WriteLine("### REPLAY CERTIFICATION SMOKE");
        var exitCode = await DotnetAsync(
            [
                "run",
                "--framework",
                "net10.0",
                "--project",
                $"{RhodiumRoot}/benchmarks/Rhodium.Benchmarks/Rhodium.Benchmarks.csproj",
                "--",
                "--replay-certification-smoke",
                "--replay-certification-report",
                replayCertificationReportPath,
                "--certification-run-id",
                certificationRunId
            ],
            log);

        if (exitCode != 0)
        {
            result = exitCode;
            return result;
        }
    }

    Console.WriteLine("### REPORT CONTRACTS");
    if (!skipVectorSmoke)
        ValidateVectorSmokeReport(vectorSmokeReportPath, requireCleanGit, requireTargetHardware, certificationRunId);
    if (!skipReplayCertification)
        ValidateReplayCertificationReport(replayCertificationReportPath, requireCleanGit, certificationRunId);
    if (!string.IsNullOrWhiteSpace(externalParityManifestPath) && !externalParityPreflightValidated)
    {
        Console.WriteLine("### EXTERNAL PARITY");
        ValidateExternalParityManifest(externalParityManifestPath);
    }

    Console.WriteLine("### CERTIFICATION MANIFEST");
    WriteCertificationManifest(
        certificationManifestPath,
        certificationRunId,
        testsRun: !skipTests,
        vectorSmokeRun: !skipVectorSmoke,
        replayCertificationRun: !skipReplayCertification,
        reportContractsValidated: true,
        requireCleanGit,
        requireTargetHardware,
        requireReleaseEvidence,
        requireExternalParity,
        testProjectCount: testProjects.Length,
        vectorSmokeVariantCount: VectorSmokeVariantCount,
        vectorSmokeBarCount: VectorSmokeBarCount,
        replayCertificationScenarioCount: ReplayCertificationScenarioCount,
        vectorSmokeReportPath: skipVectorSmoke ? null : vectorSmokeReportPath,
        replayCertificationReportPath: skipReplayCertification ? null : replayCertificationReportPath,
        externalParityManifestPath,
        verifierArguments: args);
    ValidateCertificationManifest(
        certificationManifestPath,
        certificationRunId,
        testsRun: !skipTests,
        vectorSmokeRun: !skipVectorSmoke,
        replayCertificationRun: !skipReplayCertification,
        reportContractsValidated: true,
        requireCleanGit,
        requireTargetHardware,
        requireReleaseEvidence,
        requireExternalParity,
        testProjectCount: testProjects.Length,
        vectorSmokeVariantCount: VectorSmokeVariantCount,
        vectorSmokeBarCount: VectorSmokeBarCount,
        replayCertificationScenarioCount: ReplayCertificationScenarioCount,
        externalParityManifestPath,
        verifierArguments: args);

    Console.WriteLine("### CLEAN BIN OBJ");
    CleanBuildArtifacts(RhodiumRoot);
    if (!keepReports)
        CleanGeneratedReports([vectorSmokeReportPath, replayCertificationReportPath, certificationManifestPath]);

    Console.WriteLine("Rhodium verification passed.");
    result = 0;
    return result;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    result = 1;
    return result;
}
finally
{
    if (result == 0 && !keepLog)
    {
        if (File.Exists(LogPath))
            File.Delete(LogPath);
    }
    else
    {
        await File.WriteAllTextAsync(LogPath, log.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

static async Task<int> DotnetAsync(IReadOnlyList<string> arguments, StringBuilder log)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

    foreach (var argument in arguments)
        startInfo.ArgumentList.Add(argument);

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start dotnet process.");

    var standardOutput = process.StandardOutput.ReadToEndAsync();
    var standardError = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    var combined = (await standardOutput) + await standardError;
    log.AppendLine("$ dotnet " + string.Join(' ', arguments.Select(QuoteIfNeeded)));
    log.AppendLine(combined);

    foreach (var line in ReadLines(combined))
    {
        if (ShouldSuppress(line))
            continue;

        Console.WriteLine(line);
    }

    return process.ExitCode;
}

static bool ShouldSuppress(string line)
{
    if (string.IsNullOrWhiteSpace(line))
        return true;

    return line.Contains("warning", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Determining projects to restore", StringComparison.Ordinal)
        || line.Contains("All projects are up-to-date", StringComparison.Ordinal)
        || line.Contains("CSSM_ModuleLoad", StringComparison.Ordinal)
        || line.Contains("Restored ", StringComparison.Ordinal)
        || line.Contains(" -> ", StringComparison.Ordinal);
}

static IEnumerable<string> ReadLines(string text)
{
    using var reader = new StringReader(text);
    while (reader.ReadLine() is { } line)
        yield return line;
}

static string QuoteIfNeeded(string value)
    => value.Any(char.IsWhiteSpace) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;

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

static void CleanBuildArtifacts(string root)
{
    if (!Directory.Exists(root))
        return;

    foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                 .Where(static directory =>
                 {
                     var name = Path.GetFileName(directory);
                     return name is "bin" or "obj";
                 })
                 .OrderByDescending(static directory => directory.Length))
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void CleanGeneratedReports(IEnumerable<string> paths)
{
    foreach (var path in paths)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}

static void WriteCertificationManifest(
    string path,
    string certificationRunId,
    bool testsRun,
    bool vectorSmokeRun,
    bool replayCertificationRun,
    bool reportContractsValidated,
    bool requireCleanGit,
    bool requireTargetHardware,
    bool requireReleaseEvidence,
    bool requireExternalParity,
    int testProjectCount,
    int vectorSmokeVariantCount,
    int vectorSmokeBarCount,
    int replayCertificationScenarioCount,
    string? vectorSmokeReportPath,
    string? replayCertificationReportPath,
    string? externalParityManifestPath,
    IReadOnlyList<string> verifierArguments)
{
    var directory = Path.GetDirectoryName(Path.GetFullPath(path));
    if (!string.IsNullOrEmpty(directory))
        Directory.CreateDirectory(directory);

    var manifest = new CertificationManifest(
        ReportVersion: 1,
        CertificationRunId: certificationRunId,
        GeneratedAtUtc: DateTimeOffset.UtcNow,
        TestsRun: testsRun,
        VectorSmokeRun: vectorSmokeRun,
        ReplayCertificationRun: replayCertificationRun,
        ReportContractsValidated: reportContractsValidated,
        RequireCleanGit: requireCleanGit,
        RequireTargetHardware: requireTargetHardware,
        RequireReleaseEvidence: requireReleaseEvidence,
        RequireExternalParity: requireExternalParity,
        TestProjectCount: testProjectCount,
        VectorSmokeVariantCount: vectorSmokeVariantCount,
        VectorSmokeBarCount: vectorSmokeBarCount,
        ReplayCertificationScenarioCount: replayCertificationScenarioCount,
        VectorSmokeReportPath: vectorSmokeReportPath,
        VectorSmokeReportSha256: string.IsNullOrWhiteSpace(vectorSmokeReportPath)
            ? null
            : ComputeSha256Hex(vectorSmokeReportPath),
        ReplayCertificationReportPath: replayCertificationReportPath,
        ReplayCertificationReportSha256: string.IsNullOrWhiteSpace(replayCertificationReportPath)
            ? null
            : ComputeSha256Hex(replayCertificationReportPath),
        ExternalParityManifestPath: externalParityManifestPath,
        ExternalParityManifestSha256: string.IsNullOrWhiteSpace(externalParityManifestPath)
            ? null
            : ComputeSha256Hex(externalParityManifestPath),
        VerifierArguments: verifierArguments.ToArray());

    File.WriteAllText(
        path,
        JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
}

static void ValidateCertificationManifest(
    string path,
    string certificationRunId,
    bool testsRun,
    bool vectorSmokeRun,
    bool replayCertificationRun,
    bool reportContractsValidated,
    bool requireCleanGit,
    bool requireTargetHardware,
    bool requireReleaseEvidence,
    bool requireExternalParity,
    int testProjectCount,
    int vectorSmokeVariantCount,
    int vectorSmokeBarCount,
    int replayCertificationScenarioCount,
    string? externalParityManifestPath,
    IReadOnlyList<string> verifierArguments)
{
    using var document = OpenReport(path);
    var root = document.RootElement;
    Require(root.GetProperty("ReportVersion").GetInt32() == 1, "Certification manifest version must be 1.");
    Require(root.GetProperty("CertificationRunId").GetString() == certificationRunId, "Certification manifest run id mismatch.");
    Require(!string.IsNullOrWhiteSpace(root.GetProperty("GeneratedAtUtc").GetString()), "Certification manifest missing GeneratedAtUtc.");
    Require(root.GetProperty("TestsRun").GetBoolean() == testsRun, "Certification manifest TestsRun mismatch.");
    Require(root.GetProperty("VectorSmokeRun").GetBoolean() == vectorSmokeRun, "Certification manifest VectorSmokeRun mismatch.");
    Require(root.GetProperty("ReplayCertificationRun").GetBoolean() == replayCertificationRun, "Certification manifest ReplayCertificationRun mismatch.");
    Require(root.GetProperty("ReportContractsValidated").GetBoolean() == reportContractsValidated, "Certification manifest ReportContractsValidated mismatch.");
    Require(root.GetProperty("RequireCleanGit").GetBoolean() == requireCleanGit, "Certification manifest RequireCleanGit mismatch.");
    Require(root.GetProperty("RequireTargetHardware").GetBoolean() == requireTargetHardware, "Certification manifest RequireTargetHardware mismatch.");
    Require(root.GetProperty("RequireReleaseEvidence").GetBoolean() == requireReleaseEvidence, "Certification manifest RequireReleaseEvidence mismatch.");
    Require(root.GetProperty("RequireExternalParity").GetBoolean() == requireExternalParity, "Certification manifest RequireExternalParity mismatch.");
    Require(root.GetProperty("TestProjectCount").GetInt32() == testProjectCount, "Certification manifest TestProjectCount mismatch.");
    Require(root.GetProperty("VectorSmokeVariantCount").GetInt32() == vectorSmokeVariantCount, "Certification manifest VectorSmokeVariantCount mismatch.");
    Require(root.GetProperty("VectorSmokeBarCount").GetInt32() == vectorSmokeBarCount, "Certification manifest VectorSmokeBarCount mismatch.");
    Require(root.GetProperty("ReplayCertificationScenarioCount").GetInt32() == replayCertificationScenarioCount, "Certification manifest ReplayCertificationScenarioCount mismatch.");
    ValidateVerifierArguments(root, verifierArguments);

    ValidateManifestReportPath(root, "VectorSmokeReportPath", vectorSmokeRun);
    ValidateManifestOptionalSha256(root, "VectorSmokeReportSha256", vectorSmokeRun ? root.GetProperty("VectorSmokeReportPath").GetString() : null);
    ValidateManifestReportPath(root, "ReplayCertificationReportPath", replayCertificationRun);
    ValidateManifestOptionalSha256(root, "ReplayCertificationReportSha256", replayCertificationRun ? root.GetProperty("ReplayCertificationReportPath").GetString() : null);
    ValidateManifestOptionalPath(root, "ExternalParityManifestPath", externalParityManifestPath);
    ValidateManifestOptionalSha256(root, "ExternalParityManifestSha256", externalParityManifestPath);
}

static void ValidateVerifierArguments(JsonElement root, IReadOnlyList<string> expectedArguments)
{
    var arguments = root.GetProperty("VerifierArguments");
    Require(arguments.ValueKind == JsonValueKind.Array, "Certification manifest VerifierArguments must be an array.");
    Require(arguments.GetArrayLength() == expectedArguments.Count, "Certification manifest VerifierArguments length mismatch.");

    var index = 0;
    foreach (var argument in arguments.EnumerateArray())
    {
        Require(argument.GetString() == expectedArguments[index], $"Certification manifest VerifierArguments mismatch at index {index}.");
        index++;
    }
}

static void ValidateManifestReportPath(JsonElement root, string propertyName, bool reportShouldExist)
{
    var property = root.GetProperty(propertyName);
    if (!reportShouldExist)
    {
        Require(property.ValueKind == JsonValueKind.Null, $"Certification manifest {propertyName} must be null when the gate is skipped.");
        return;
    }

    var path = property.GetString();
    Require(!string.IsNullOrWhiteSpace(path), $"Certification manifest missing {propertyName}.");
    Require(File.Exists(path), $"Certification manifest {propertyName} points to a missing report: {path}");
}

static void ValidateManifestOptionalPath(JsonElement root, string propertyName, string? expectedPath)
{
    var property = root.GetProperty(propertyName);
    if (string.IsNullOrWhiteSpace(expectedPath))
    {
        Require(property.ValueKind == JsonValueKind.Null, $"Certification manifest {propertyName} must be null when no path is supplied.");
        return;
    }

    Require(property.GetString() == expectedPath, $"Certification manifest {propertyName} mismatch.");
    Require(File.Exists(expectedPath), $"Certification manifest {propertyName} points to a missing file: {expectedPath}");
}

static void ValidateManifestOptionalSha256(JsonElement root, string propertyName, string? expectedPath)
{
    var property = root.GetProperty(propertyName);
    if (string.IsNullOrWhiteSpace(expectedPath))
    {
        Require(property.ValueKind == JsonValueKind.Null, $"Certification manifest {propertyName} must be null when the source artifact is not supplied.");
        return;
    }

    var expectedSha256 = property.GetString();
    Require(IsSha256Hex(expectedSha256), $"Certification manifest {propertyName} must be a 64-character SHA-256 hex digest.");
    var actualSha256 = ComputeSha256Hex(expectedPath);
    Require(
        string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase),
        $"Certification manifest {propertyName} does not match the external parity manifest.");
}

static void ValidateExternalParityManifest(string path)
{
    using var document = OpenReport(path);
    var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
    var root = document.RootElement;
    ValidateAllowedProperties(
        root,
        "External parity manifest",
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
    Require(root.GetProperty("ReportVersion").GetInt32() == 1, "External parity manifest version must be 1.");
    Require(root.GetProperty("Passed").GetBoolean(), "External parity manifest must be passed.");
    Require(!string.IsNullOrWhiteSpace(root.GetProperty("Provider").GetString()), "External parity manifest missing Provider.");
    Require(!string.IsNullOrWhiteSpace(root.GetProperty("DatasetId").GetString()), "External parity manifest missing DatasetId.");
    ValidateExternalParityGitCommit(root.GetProperty("GitCommit").GetString());
    ValidateExternalParityStringArray(root, "AcceptedLimitations");
    ValidateExternalParityStringArray(root, "UnsupportedFeatures");
    ValidateExternalParityMismatches(root);

    var fixtures = root.GetProperty("Fixtures");
    Require(fixtures.ValueKind == JsonValueKind.Array, "External parity manifest Fixtures must be an array.");
    Require(fixtures.GetArrayLength() > 0, "External parity manifest must contain at least one fixture.");
    var fixtureKinds = new HashSet<string>(StringComparer.Ordinal);
    foreach (var fixture in fixtures.EnumerateArray())
    {
        ValidateAllowedProperties(
            fixture,
            "External parity fixture",
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
                "InputArtifactSha256",
                "OutputArtifactPath",
                "OutputArtifactSha256",
                "ComparisonReportPath",
                "ComparisonReportSha256"
            ]);
        var name = fixture.GetProperty("Name").GetString();
        Require(!string.IsNullOrWhiteSpace(name), "External parity fixture missing Name.");
        var fixtureKind = fixture.GetProperty("FixtureKind").GetString();
        Require(IsExternalParityFixtureKind(fixtureKind), $"External parity fixture has an invalid FixtureKind: {name}");
        fixtureKinds.Add(fixtureKind!);
        Require(!string.IsNullOrWhiteSpace(fixture.GetProperty("Provider").GetString()), $"External parity fixture missing Provider: {name}");
        Require(!string.IsNullOrWhiteSpace(fixture.GetProperty("DatasetId").GetString()), $"External parity fixture missing DatasetId: {name}");
        Require(!string.IsNullOrWhiteSpace(fixture.GetProperty("ExpectedResultSource").GetString()), $"External parity fixture missing ExpectedResultSource: {name}");
        Require(!string.IsNullOrWhiteSpace(fixture.GetProperty("CoveredDateRange").GetString()), $"External parity fixture missing CoveredDateRange: {name}");
        Require(!string.IsNullOrWhiteSpace(fixture.GetProperty("InstrumentSet").GetString()), $"External parity fixture missing InstrumentSet: {name}");
        Require(!string.IsNullOrWhiteSpace(fixture.GetProperty("AccountType").GetString()), $"External parity fixture missing AccountType: {name}");
        Require(fixture.GetProperty("Passed").GetBoolean(), $"External parity fixture failed: {name}");

        ValidateExternalParityArtifact(fixture, "InputArtifactPath", "InputArtifactSha256", name, manifestDirectory);
        ValidateExternalParityArtifact(fixture, "OutputArtifactPath", "OutputArtifactSha256", name, manifestDirectory);
        ValidateExternalParityArtifact(fixture, "ComparisonReportPath", "ComparisonReportSha256", name, manifestDirectory);
    }

    foreach (var requiredKind in RequiredExternalParityFixtureKinds())
        Require(fixtureKinds.Contains(requiredKind), $"External parity manifest missing required FixtureKind: {requiredKind}");
}

static string[] RequiredExternalParityFixtureKinds()
    => [
        "TradingCalendar",
        "AccountStatement",
        "MarginLiquidationFinancing",
        "MarketReplayExecution",
        "VenueOrderPolicy",
        "CrossVenueRouting"
    ];

static bool IsExternalParityFixtureKind(string? fixtureKind)
    => fixtureKind is "TradingCalendar"
        or "AccountStatement"
        or "MarginLiquidationFinancing"
        or "MarketReplayExecution"
        or "VenueOrderPolicy"
        or "CrossVenueRouting";

static void ValidateExternalParityGitCommit(string? manifestGitCommit)
{
    Require(!string.IsNullOrWhiteSpace(manifestGitCommit), "External parity manifest missing GitCommit.");
    Require(manifestGitCommit.Length >= 12, "External parity manifest GitCommit must be at least 12 characters.");

    var currentGitCommit = Git("rev-parse", "HEAD");
    Require(!string.IsNullOrWhiteSpace(currentGitCommit), "Could not determine current git commit for external parity validation.");
    Require(
        string.Equals(currentGitCommit, manifestGitCommit, StringComparison.OrdinalIgnoreCase)
            || currentGitCommit.StartsWith(manifestGitCommit, StringComparison.OrdinalIgnoreCase),
        "External parity manifest GitCommit does not match the current checkout.");
}

static void ValidateExternalParityStringArray(JsonElement root, string propertyName)
{
    var values = root.GetProperty(propertyName);
    Require(values.ValueKind == JsonValueKind.Array, $"External parity manifest {propertyName} must be an array.");
    foreach (var value in values.EnumerateArray())
        Require(!string.IsNullOrWhiteSpace(value.GetString()), $"External parity manifest {propertyName} contains an empty item.");
}

static void ValidateExternalParityMismatches(JsonElement root)
{
    var mismatches = root.GetProperty("Mismatches");
    Require(mismatches.ValueKind == JsonValueKind.Array, "External parity manifest Mismatches must be an array.");
    foreach (var mismatch in mismatches.EnumerateArray())
    {
        ValidateAllowedProperties(
            mismatch,
            "External parity mismatch",
            [
                "Name",
                "Description",
                "Classification"
            ]);
        var name = mismatch.GetProperty("Name").GetString();
        Require(!string.IsNullOrWhiteSpace(name), "External parity mismatch missing Name.");
        Require(!string.IsNullOrWhiteSpace(mismatch.GetProperty("Description").GetString()), $"External parity mismatch missing Description: {name}");

        var classification = mismatch.GetProperty("Classification").GetString();
        Require(
            classification is "RhodiumBug" or "ProviderDataAmbiguity" or "PolicyDifference" or "UnsupportedFeature",
            $"External parity mismatch has an invalid Classification: {name}");
        Require(classification is not "RhodiumBug", $"External parity manifest is passed but contains a RhodiumBug mismatch: {name}");
    }
}

static void ValidateAllowedProperties(JsonElement element, string elementName, string[] allowedProperties)
{
    foreach (var property in element.EnumerateObject())
        Require(Array.IndexOf(allowedProperties, property.Name) >= 0, $"{elementName} contains an unknown property: {property.Name}");
}

static void ValidateExternalParityArtifact(
    JsonElement fixture,
    string pathPropertyName,
    string hashPropertyName,
    string? fixtureName,
    string manifestDirectory)
{
    Require(fixture.TryGetProperty(pathPropertyName, out var pathProperty), $"External parity fixture missing {pathPropertyName}: {fixtureName}");
    Require(pathProperty.ValueKind == JsonValueKind.String, $"External parity fixture {pathPropertyName} must be a string: {fixtureName}");

    var artifactPath = pathProperty.GetString();
    Require(!string.IsNullOrWhiteSpace(artifactPath), $"External parity fixture has an empty {pathPropertyName}: {fixtureName}");

    var resolvedPath = Path.IsPathRooted(artifactPath)
        ? artifactPath
        : Path.GetFullPath(Path.Combine(manifestDirectory, artifactPath));
    Require(
        IsPathWithinDirectory(resolvedPath, manifestDirectory),
        $"External parity fixture {pathPropertyName} must stay inside the manifest directory: {artifactPath}");
    Require(File.Exists(resolvedPath), $"External parity fixture {pathPropertyName} points to a missing file: {artifactPath}");

    Require(fixture.TryGetProperty(hashPropertyName, out var hashProperty), $"External parity fixture missing {hashPropertyName}: {fixtureName}");
    Require(hashProperty.ValueKind == JsonValueKind.String, $"External parity fixture {hashPropertyName} must be a string: {fixtureName}");
    var expectedHash = hashProperty.GetString();
    Require(IsSha256Hex(expectedHash), $"External parity fixture {hashPropertyName} must be a 64-character SHA-256 hex digest: {fixtureName}");

    var actualHash = ComputeSha256Hex(resolvedPath);
    Require(
        string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase),
        $"External parity fixture {hashPropertyName} does not match {pathPropertyName}: {fixtureName}");
}

static bool IsSha256Hex(string? value)
    => value is { Length: 64 } && value.All(static c => char.IsAsciiHexDigit(c));

static string ComputeSha256Hex(string path)
{
    using var stream = File.OpenRead(path);
    var hash = SHA256.HashData(stream);
    return Convert.ToHexString(hash).ToLowerInvariant();
}

static void ValidateVectorSmokeReport(string path, bool requireCleanGit, bool requireTargetHardware, string certificationRunId)
{
    using var document = OpenReport(path);
    var root = document.RootElement;
    Require(root.GetProperty("ReportVersion").GetInt32() == 1, "Vector smoke report version must be 1.");
    Require(root.GetProperty("GateName").GetString() == "vector-smoke", "Vector smoke report gate name mismatch.");
    Require(root.GetProperty("CertificationRunId").GetString() == certificationRunId, "Vector smoke report run id mismatch.");
    Require(root.GetProperty("Passed").GetBoolean(), "Vector smoke report must be passed.");
    Require(root.GetProperty("VariantCount").GetInt32() == VectorSmokeVariantCount, "Vector smoke report variant count mismatch.");
    Require(root.GetProperty("BarCount").GetInt32() == VectorSmokeBarCount, "Vector smoke report bar count mismatch.");
    var maxElapsed = ReadTimeSpan(root, "MaxElapsed", "Vector smoke report");
    var elapsed = ReadTimeSpan(root, "Elapsed", "Vector smoke report");
    Require(maxElapsed == TimeSpan.FromSeconds(VectorSmokeMaxElapsedSeconds), "Vector smoke report max elapsed mismatch.");
    Require(elapsed <= maxElapsed, $"Vector smoke report exceeded max elapsed: {elapsed} > {maxElapsed}.");
    if (requireTargetHardware)
    {
        var logicalProcessorCount = root.GetProperty("LogicalProcessorCount").GetInt32();
        Require(
            logicalProcessorCount >= TargetHardwareLogicalProcessorCount,
            $"Vector smoke report was produced on {logicalProcessorCount} logical processors; target hardware certification requires at least {TargetHardwareLogicalProcessorCount}.");
    }

    ValidateEnvironment(root.GetProperty("Environment"), "Vector smoke report", requireCleanGit);
}

static TimeSpan ReadTimeSpan(JsonElement root, string propertyName, string reportName)
{
    var value = root.GetProperty(propertyName).GetString();
    Require(
        TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var timeSpan),
        $"{reportName} {propertyName} must be a TimeSpan string.");
    return timeSpan;
}

static void ValidateReplayCertificationReport(string path, bool requireCleanGit, string certificationRunId)
{
    using var document = OpenReport(path);
    var root = document.RootElement;
    Require(root.GetProperty("ReportVersion").GetInt32() == 1, "Replay certification report version must be 1.");
    Require(root.GetProperty("GateName").GetString() == "replay-certification-smoke", "Replay certification report gate name mismatch.");
    Require(root.GetProperty("CertificationRunId").GetString() == certificationRunId, "Replay certification report run id mismatch.");
    Require(root.GetProperty("Passed").GetBoolean(), "Replay certification report must be passed.");
    ValidateEnvironment(root.GetProperty("Environment"), "Replay certification report", requireCleanGit);

    var scenarios = root.GetProperty("Scenarios");
    Require(scenarios.ValueKind == JsonValueKind.Array, "Replay certification scenarios must be an array.");
    Require(scenarios.GetArrayLength() == ReplayCertificationScenarioCount, "Replay certification scenario count mismatch.");
    var scenarioNames = new HashSet<string>(StringComparer.Ordinal);
    foreach (var scenario in scenarios.EnumerateArray())
    {
        var name = scenario.GetProperty("Name").GetString();
        Require(!string.IsNullOrWhiteSpace(name), "Replay certification scenario missing Name.");
        Require(scenarioNames.Add(name), $"Replay certification scenario was duplicated: {name}.");
        Require(scenario.GetProperty("Passed").GetBoolean(), $"Replay certification scenario failed: {name}.");
        Require(scenario.GetProperty("Evidence").EnumerateObject().Any(), $"Replay certification scenario has no evidence: {name}.");
    }

    foreach (var requiredName in RequiredReplayCertificationScenarioNames())
        Require(scenarioNames.Contains(requiredName), $"Replay certification report missing scenario: {requiredName}.");
}

static string[] RequiredReplayCertificationScenarioNames()
    => [
        "Bundled calendar dataset",
        "Internal cash transfer",
        "Reduce-to-maintenance liquidation",
        "Corporate actions",
        "Financing charges",
        "Cross-venue diagnostics",
        "Cross-venue sweep routing",
        "Provider policy feeds"
    ];

static JsonDocument OpenReport(string path)
{
    Require(File.Exists(path), $"Expected report file does not exist: {path}");
    return JsonDocument.Parse(File.ReadAllText(path));
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

static void ValidateEnvironment(JsonElement environment, string reportName, bool requireCleanGit)
{
    Require(!string.IsNullOrWhiteSpace(environment.GetProperty("GeneratedAtUtc").GetString()), $"{reportName} missing GeneratedAtUtc.");
    Require(!string.IsNullOrWhiteSpace(environment.GetProperty("MachineName").GetString()), $"{reportName} missing MachineName.");
    Require(!string.IsNullOrWhiteSpace(environment.GetProperty("OSDescription").GetString()), $"{reportName} missing OSDescription.");
    Require(!string.IsNullOrWhiteSpace(environment.GetProperty("ProcessArchitecture").GetString()), $"{reportName} missing ProcessArchitecture.");
    Require(!string.IsNullOrWhiteSpace(environment.GetProperty("FrameworkDescription").GetString()), $"{reportName} missing FrameworkDescription.");
    Require(!string.IsNullOrWhiteSpace(environment.GetProperty("RuntimeVersion").GetString()), $"{reportName} missing RuntimeVersion.");
    Require(environment.GetProperty("LogicalProcessorCount").GetInt32() > 0, $"{reportName} LogicalProcessorCount must be positive.");
    Require(environment.TryGetProperty("GitBranch", out _), $"{reportName} missing GitBranch.");
    Require(environment.TryGetProperty("GitCommit", out _), $"{reportName} missing GitCommit.");
    Require(environment.TryGetProperty("GitTrackedChanges", out var gitTrackedChanges), $"{reportName} missing GitTrackedChanges.");
    if (requireCleanGit)
        Require(gitTrackedChanges.ValueKind == JsonValueKind.False, $"{reportName} was produced with tracked git changes.");
}

static string? Git(params string[] arguments)
{
    try
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

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output.Trim() : null;
    }
    catch
    {
        return null;
    }
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void ValidateArguments(IReadOnlyList<string> values)
{
    for (var i = 0; i < values.Count; i++)
    {
        var value = values[i];
        if (!value.StartsWith("-", StringComparison.Ordinal))
            continue;

        if (IsKnownFlag(value))
            continue;

        if (string.Equals(value, "--report-dir", StringComparison.OrdinalIgnoreCase))
        {
            if (i == values.Count - 1 || values[i + 1].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException("--report-dir requires a path value.");

            i++;
            continue;
        }

        if (value.StartsWith("--report-dir=", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Length == "--report-dir=".Length)
                throw new ArgumentException("--report-dir requires a non-empty path value.");

            continue;
        }

        if (string.Equals(value, "--external-parity-manifest", StringComparison.OrdinalIgnoreCase))
        {
            if (i == values.Count - 1 || values[i + 1].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException("--external-parity-manifest requires a path value.");

            i++;
            continue;
        }

        if (value.StartsWith("--external-parity-manifest=", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Length == "--external-parity-manifest=".Length)
                throw new ArgumentException("--external-parity-manifest requires a non-empty path value.");

            continue;
        }

        throw new ArgumentException($"Unknown verifier option: {value}. Run with -- --help for usage.");
    }
}

static bool IsKnownFlag(string value)
    => string.Equals(value, "--skip-tests", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "--skip-vector-smoke", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "--skip-replay-certification", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "--keep-reports", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "--keep-log", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "--require-clean-git", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "--require-target-hardware", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "--require-release-evidence", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "--require-external-parity", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "--list-gates", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);

static void PrintUsage()
{
    Console.WriteLine(
        """
        Usage:
          dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/verify-rhodium.cs [options]

        Options:
          --skip-tests                  Skip the Rhodium test project matrix.
          --skip-vector-smoke           Skip the vector smoke gate and vector report contract.
          --skip-replay-certification   Skip the replay certification smoke gate and replay report contract.
          --keep-reports                Keep generated smoke JSON reports after validation.
          --keep-log                    Keep rhodium-verify.log even when verification passes.
          --require-clean-git           Fail report validation if tracked files were modified.
          --require-target-hardware     Require vector smoke to run on at least 64 logical processors.
          --require-release-evidence    Require retained reports, clean git, target hardware, external parity, explicit report dir, and all local gates.
          --require-external-parity     Require a passing external parity manifest with all fixture kinds and artifact hashes.
          --external-parity-manifest <PATH>
                                        Validate an external broker or venue parity manifest.
          --external-parity-manifest=<PATH>
                                        Equivalent external parity manifest form for CI argument lists.
          --report-dir <PATH>           Write smoke JSON reports to PATH instead of Rhodium/benchmarks.
          --report-dir=<PATH>           Equivalent report directory form for CI argument lists.
          --list-gates                  List verifier gates and replay certification scenarios.
          --help, -h                    Show this help text.

        Default behavior:
          Runs all Rhodium tests, vector smoke, replay certification smoke, report contract validation,
          then removes Rhodium bin/obj directories and generated smoke reports.

        External parity manifests:
          Must include TradingCalendar, AccountStatement, MarginLiquidationFinancing,
          MarketReplayExecution, VenueOrderPolicy, and CrossVenueRouting fixtures.
          Each fixture must point to input/output/comparison artifacts inside the manifest
          directory and include matching SHA-256 hashes. Passed manifests cannot contain
          RhodiumBug mismatches.
        """);
}

static void PrintGates()
{
    Console.WriteLine(
        """
        Rhodium verifier gates:
          tests                         All Rhodium test projects listed in verify-rhodium.cs.
          vector-smoke                  10,000 variants x 100 bars with report contract validation.
          replay-certification-smoke    Replay/accounting/multi-venue certification scenarios with evidence.
          external-parity               Optional broker/venue parity manifest validation with fixture, artifact, hash, and mismatch checks.

        Evidence modes:
          require-clean-git             Report validation fails if tracked files were modified.
          require-target-hardware       Vector smoke must run on at least 64 logical processors.
          require-release-evidence      Retained reports, clean git, target hardware, external parity, explicit report dir, and all local gates are required.

        External parity required FixtureKind values:
          - TradingCalendar
          - AccountStatement
          - MarginLiquidationFinancing
          - MarketReplayExecution
          - VenueOrderPolicy
          - CrossVenueRouting

        External parity artifact rules:
          - InputArtifactPath, OutputArtifactPath, and ComparisonReportPath are required.
          - Artifact paths must resolve inside the manifest directory.
          - InputArtifactSha256, OutputArtifactSha256, and ComparisonReportSha256 must match file bytes.
          - Passed manifests may not contain RhodiumBug mismatches.

        Replay certification scenarios:
          - Bundled calendar dataset
          - Internal cash transfer
          - Reduce-to-maintenance liquidation
          - Corporate actions
          - Financing charges
          - Cross-venue diagnostics
          - Cross-venue sweep routing
          - Provider policy feeds
        """);
}

internal sealed record CertificationManifest(
    int ReportVersion,
    string CertificationRunId,
    DateTimeOffset GeneratedAtUtc,
    bool TestsRun,
    bool VectorSmokeRun,
    bool ReplayCertificationRun,
    bool ReportContractsValidated,
    bool RequireCleanGit,
    bool RequireTargetHardware,
    bool RequireReleaseEvidence,
    bool RequireExternalParity,
    int TestProjectCount,
    int VectorSmokeVariantCount,
    int VectorSmokeBarCount,
    int ReplayCertificationScenarioCount,
    string? VectorSmokeReportPath,
    string? VectorSmokeReportSha256,
    string? ReplayCertificationReportPath,
    string? ReplayCertificationReportSha256,
    string? ExternalParityManifestPath,
    string? ExternalParityManifestSha256,
    string[] VerifierArguments);
