// check-nuget-versions.cs — latest-stable NuGet audit for HPD-Agent.Framework/src
//
//   dotnet run --file eng/check-nuget-versions.cs -- [options]
//
// Options:
//   --path <dir>          Directory tree to scan for *.csproj (default: ../src next to this file)
//   --output <file>       Also write the report to <file>
//   --include-internal    Include HPD-* (first-party) packages
//   --fail-on-outdated    Exit 1 when any package is behind nuget.org
//   --concurrency <n>     Parallel nuget.org requests (default 8)
//   -h | --help           Show usage
//
// Behaviour:
//   * "latest stable" = highest nuget.org version without a SemVer pre-release tag.
//   * Versions that are MSBuild properties (e.g. $(HPDTuiVersion)) are reported as
//     "unresolved" instead of being silently dropped.
//   * Report -> stdout; progress -> stderr (keeps `dotnet run --file` build noise separable).

#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property LangVersion=latest
#:property PublishAot=false
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

try
{
    return await RunAsync(AuditOptions.Parse(args));
}
catch (Exception exception)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 1;
}

// ---------------------------------------------------------------------------
// Orchestration
// ---------------------------------------------------------------------------

async Task<int> RunAsync(AuditOptions options)
{
    var scanRoot = options.Path is { Length: > 0 } explicitPath
        ? Path.GetFullPath(explicitPath)
        : Path.GetFullPath(Path.Combine(ScriptDirectory(), "..", "src"));

    if (!Directory.Exists(scanRoot))
    {
        Console.Error.WriteLine($"scan path not found: {scanRoot}");
        return 1;
    }

    var references = ScanPackageReferences(scanRoot)
        .Where(reference => options.IncludeInternal
            || !reference.Id.StartsWith("HPD-", StringComparison.OrdinalIgnoreCase))
        .ToList();

    var unresolved = references.Where(reference => !LooksLikeVersion(reference.Version)).ToList();

    var groups = references
        .Where(reference => LooksLikeVersion(reference.Version))
        .GroupBy(reference => reference.Id, StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        .ToList();

    Console.Error.WriteLine($"scanning  : {scanRoot}");
    Console.Error.WriteLine($"packages  : {groups.Count} id(s) across {groups.Sum(g => g.Select(r => r.Project).Distinct().Count())} project reference(s)");
    if (unresolved.Count > 0)
    {
        Console.Error.WriteLine($"unresolved: {unresolved.Count} reference(s) with MSBuild-property versions");
    }

    Console.Error.WriteLine($"querying  : https://api.nuget.org (concurrency {options.Concurrency})");

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("HPD-Agent-nuget-audit/1.0");
    using var gate = new SemaphoreSlim(options.Concurrency);

    var rows = new List<Row>(groups.Count);
    var lockObject = new object();

    var lookups = groups.Select(async group =>
    {
        await gate.WaitAsync().ConfigureAwait(false);
        string? latest;
        try
        {
            latest = await GetLatestStableAsync(http, group.Key).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }

        var current = group
            .Select(reference => reference.Version)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(VersionOrder.Key, StringComparer.Ordinal)
            .ToList();

        var row = new Row(group.Key, current, latest, [.. group.Select(reference => (reference.Project, reference.Version))]);
        lock (lockObject)
        {
            rows.Add(row);
        }
    }).ToList();

    await Task.WhenAll(lookups).ConfigureAwait(false);

    rows.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Id, right.Id));

    var report = Render(rows, unresolved);
    Console.Write(report);

    if (options.Output is { Length: > 0 } outputPath)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(fullOutputPath, report).ConfigureAwait(false);
        Console.Error.WriteLine($"report    : {fullOutputPath}");
    }

    var stale = rows.Where(row => row.Status is RowStatus.Outdated or RowStatus.Mixed).ToList();
    var ahead = rows.Count(row => row.Status == RowStatus.Ahead);
    var unavailable = rows.Count(row => row.Status == RowStatus.Unavailable);

    Console.Error.WriteLine(
        $"summary   : {rows.Count - stale.Count - ahead - unavailable} up-to-date, {stale.Count} outdated, {ahead} ahead, {unavailable} unavailable");

    return options.FailOnOutdated && stale.Count > 0 ? 1 : 0;
}

// ---------------------------------------------------------------------------
// Rendering
// ---------------------------------------------------------------------------

string Render(List<Row> rows, List<PackageReferenceEntry> unresolved)
{
    var builder = new StringBuilder();
    builder.AppendLine("Package".PadRight(58) + "Current".PadRight(26) + "Latest stable".PadRight(26) + "Status");
    builder.AppendLine(new string('-', 126));

    foreach (var row in rows)
    {
        builder.AppendLine(
            row.Id.PadRight(58)
            + string.Join(", ", row.Current).PadRight(26)
            + (row.Latest ?? "-").PadRight(26)
            + Describe(row.Status));
    }

    var stale = rows.Where(row => row.Status is RowStatus.Outdated or RowStatus.Mixed).ToList();
    if (stale.Count > 0)
    {
        builder.AppendLine();
        builder.AppendLine("Behind nuget.org:");
        foreach (var row in stale)
        {
            builder.AppendLine($"  {row.Id}  ({string.Join(", ", row.Current)} -> {row.Latest})");
            foreach (var (project, version) in row.References)
            {
                builder.AppendLine($"      {version,-16} {project}");
            }
        }
    }

    var unavailable = rows.Where(row => row.Status == RowStatus.Unavailable).ToList();
    if (unavailable.Count > 0)
    {
        builder.AppendLine();
        builder.AppendLine("Not found on nuget.org (private/renamed feed?):");
        foreach (var row in unavailable)
        {
            builder.AppendLine($"  {row.Id}  ({string.Join(", ", row.Current)})");
        }
    }

    if (unresolved.Count > 0)
    {
        builder.AppendLine();
        builder.AppendLine("Unresolved (MSBuild-property version):");
        foreach (var reference in unresolved.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"  {reference.Id,-48} {reference.Version,-24} {reference.Project}");
        }
    }

    return builder.ToString();
}

static string Describe(RowStatus status) => status switch
{
    RowStatus.UpToDate => "up-to-date",
    RowStatus.Outdated => "OUTDATED",
    RowStatus.Mixed => "MIXED VERSIONS",
    RowStatus.Ahead => "ahead of nuget.org",
    _ => "unavailable",
};

// ---------------------------------------------------------------------------
// nuget.org flat-container lookup
// ---------------------------------------------------------------------------

static async Task<string?> GetLatestStableAsync(HttpClient http, string id)
{
    var url = FormattableString.Invariant($"https://api.nuget.org/v3-flatcontainer/{id.ToLowerInvariant()}/index.json");

    using var response = await http.GetAsync(url).ConfigureAwait(false);
    if (!response.IsSuccessStatusCode)
    {
        return null;
    }

    await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
    using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

    if (!document.RootElement.TryGetProperty("versions", out var versions))
    {
        return null;
    }

    string? best = null;
    var bestKey = string.Empty;

    foreach (var element in versions.EnumerateArray())
    {
        var candidate = element.GetString();
        if (candidate is null || candidate.Contains('-', StringComparison.Ordinal))
        {
            continue; // pre-release
        }

        var key = VersionOrder.Key(candidate);
        if (best is null || string.CompareOrdinal(key, bestKey) > 0)
        {
            best = candidate;
            bestKey = key;
        }
    }

    return best;
}

// ---------------------------------------------------------------------------
// csproj scanning
// ---------------------------------------------------------------------------

static IEnumerable<PackageReferenceEntry> ScanPackageReferences(string root)
{
    var tagPattern = new Regex(@"<PackageReference\b[^>]*>", RegexOptions.Compiled);
    var attributePattern = new Regex("(?<name>[A-Za-z_][A-Za-z0-9_-]*)\\s*=\\s*\"(?<value>[^\"]*)\"", RegexOptions.Compiled);

    var projects = Directory
        .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
        .Where(path => !IsBuildOutput(path))
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    foreach (var project in projects)
    {
        var text = File.ReadAllText(project);
        var relative = Path.GetRelativePath(root, project);

        foreach (Match tag in tagPattern.Matches(text))
        {
            string? id = null;
            string? version = null;

            foreach (Match attribute in attributePattern.Matches(tag.Value))
            {
                var name = attribute.Groups["name"].Value;
                if (name.Equals("Include", StringComparison.OrdinalIgnoreCase))
                {
                    id = attribute.Groups["value"].Value;
                }
                else if (name.Equals("Version", StringComparison.OrdinalIgnoreCase))
                {
                    version = attribute.Groups["value"].Value;
                }
            }

            if (id is null)
            {
                continue;
            }

            yield return new PackageReferenceEntry(relative, id, version ?? string.Empty);
        }
    }
}

static bool IsBuildOutput(string path)
{
    var separator = Path.DirectorySeparatorChar;
    return path.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase);
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static bool LooksLikeVersion(string version)
{
    if (version.Length == 0)
    {
        return false;
    }

    var first = version[0];
    return char.IsAsciiDigit(first);
}

static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(Path.GetFullPath(path))!;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

sealed record PackageReferenceEntry(string Project, string Id, string Version);

// Zero-padded comparable key so "10.0.10" sorts above "10.0.9".
static class VersionOrder
{
    internal static string Key(string version)
    {
        var builder = new StringBuilder(28);
        var parts = version.Split('.');

        for (var index = 0; index < 4; index++)
        {
            var raw = index < parts.Length ? parts[index] : "0";
            var digits = new string([.. raw.TakeWhile(char.IsAsciiDigit)]);
            var value = int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            builder.Append(value.ToString("D6", CultureInfo.InvariantCulture)).Append('.');
        }

        return builder.ToString();
    }
}

sealed record Row(string Id, IReadOnlyList<string> Current, string? Latest, IReadOnlyList<(string Project, string Version)> References)
{
    internal RowStatus Status { get; } = Compute(Current, Latest);

    static RowStatus Compute(IReadOnlyList<string> current, string? latest)
    {
        if (latest is null)
        {
            return RowStatus.Unavailable;
        }

        var newestCurrent = current[^1];
        var newestKey = VersionOrder.Key(newestCurrent);
        var latestKey = VersionOrder.Key(latest);

        var comparison = string.CompareOrdinal(newestKey, latestKey);
        if (comparison < 0)
        {
            return RowStatus.Outdated;
        }

        if (comparison > 0)
        {
            return RowStatus.Ahead;
        }

        return current.Count > 1 ? RowStatus.Mixed : RowStatus.UpToDate;
    }
}

enum RowStatus
{
    UpToDate,
    Outdated,
    Mixed,
    Ahead,
    Unavailable,
}

sealed record AuditOptions(string? Path, string? Output, bool IncludeInternal, bool FailOnOutdated, int Concurrency)
{
    internal static AuditOptions Parse(string[] args)
    {
        string? path = null;
        string? output = null;
        var includeInternal = false;
        var failOnOutdated = false;
        var concurrency = 8;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--path":
                    path = NextValue(args, ref index, "--path");
                    break;
                case "--output":
                    output = NextValue(args, ref index, "--output");
                    break;
                case "--include-internal":
                    includeInternal = true;
                    break;
                case "--fail-on-outdated":
                    failOnOutdated = true;
                    break;
                case "--concurrency":
                    concurrency = int.Parse(NextValue(args, ref index, "--concurrency"), CultureInfo.InvariantCulture);
                    break;
                case "-h" or "--help":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"unknown argument '{args[index]}' (use --help)");
            }
        }

        return new AuditOptions(path, output, includeInternal, failOnOutdated, concurrency);
    }

    static string NextValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"'{name}' requires a value.");
        }

        return args[++index];
    }

    internal static void PrintUsage()
    {
        Console.WriteLine("usage: dotnet run --file eng/check-nuget-versions.cs -- [options]");
        Console.WriteLine();
        Console.WriteLine("  --path <dir>          Directory tree to scan for *.csproj (default: ../src)");
        Console.WriteLine("  --output <file>       Also write the report to <file>");
        Console.WriteLine("  --include-internal    Include HPD-* (first-party) packages");
        Console.WriteLine("  --fail-on-outdated    Exit 1 when any package is behind nuget.org");
        Console.WriteLine("  --concurrency <n>     Parallel nuget.org requests (default 8)");
        Console.WriteLine("  -h | --help           Show usage");
    }
}
