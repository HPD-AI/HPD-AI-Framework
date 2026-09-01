using System.Xml.Linq;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var expected = ArchitectureGraph.Expected;
var failures = new List<string>();
foreach (var (project, allowed) in expected)
{
    var file = Path.Combine(root, "src", $"HPD.Payments.{project}", $"HPD.Payments.{project}.csproj");
    if (!File.Exists(file)) { failures.Add($"missing project: {project}"); continue; }
    var actual = XDocument.Load(file).Descendants("ProjectReference")
        .Select(e => Path.GetFileNameWithoutExtension((string?)e.Attribute("Include"))?.Replace("HPD.Payments.", "", StringComparison.Ordinal) ?? "")
        .Order(StringComparer.Ordinal).ToArray();
    if (!actual.SequenceEqual(allowed.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        failures.Add($"{project}: expected [{string.Join(',', allowed)}], actual [{string.Join(',', actual)}]");
}
var unknown = Directory.GetFiles(Path.Combine(root,"src"), "*.csproj", SearchOption.AllDirectories)
    .Select(Path.GetFileNameWithoutExtension).Select(n => n!.Replace("HPD.Payments.", "", StringComparison.Ordinal))
    .Except(expected.Keys, StringComparer.Ordinal).ToArray();
if (unknown.Length != 0) failures.Add($"unregistered projects: {string.Join(',', unknown)}");
if (ArchitectureGraph.HasCycle(expected)) failures.Add("cycle detected");
if (failures.Count != 0) { Console.Error.WriteLine(string.Join(Environment.NewLine, failures)); return 1; }
Console.WriteLine($"architecture graph validated: {expected.Count} projects, {expected.Sum(x => x.Value.Length)} edges, no missing/extra/inverse/cyclic edges");
return 0;

static class ArchitectureGraph
{
    public static readonly IReadOnlyDictionary<string,string[]> Expected = new Dictionary<string,string[]>(StringComparer.Ordinal)
    {
        ["Primitives"] = [],
        ["Contracts"] = ["Primitives"],
        ["Supporting"] = ["Primitives", "Contracts"],
        ["Serialization"] = ["Primitives", "Contracts", "Supporting"],
        ["Persistence"] = ["Primitives", "Contracts", "Supporting"],
        ["Runtime"] = ["Primitives", "Contracts", "Supporting", "Persistence", "HPD.Base", "HPD.Base.Generators"],
        ["Generators"] = [],
        ["Analyzers"] = [],
        ["Adapters.InMemory"] = ["Primitives", "Contracts", "Supporting", "Persistence", "Runtime"],
        ["Adapters.Sqlite"] = ["Primitives", "Contracts", "Supporting", "Persistence", "Runtime"],
        ["Adapters.Postgres"] = ["Primitives", "Contracts", "Supporting", "Persistence"],
        ["Connectors.Simulator"] = ["Primitives", "Contracts", "Supporting", "Runtime"],
        ["Connectors.Stripe"] = ["Primitives", "Contracts", "Supporting", "Runtime"],
        ["Extensions.Dynamic"] = ["Primitives", "Contracts", "Supporting", "Runtime"],
        ["Extensions.OutOfProcess"] = ["Primitives", "Contracts", "Supporting", "Serialization"],
        ["Profiles.Embedded"] = ["Runtime", "Serialization", "Adapters.InMemory", "Adapters.Sqlite", "Connectors.Simulator"],
        ["Profiles.Distributed"] = ["Runtime", "Serialization", "Connectors.Simulator"],
        ["Tools.Conformance"] = ["Primitives", "Contracts", "Supporting", "Serialization", "Persistence", "Runtime", "Generators", "Analyzers", "Adapters.InMemory", "Adapters.Sqlite", "Adapters.Postgres", "Connectors.Simulator", "Connectors.Stripe", "Extensions.Dynamic", "Extensions.OutOfProcess", "Profiles.Embedded", "Profiles.Distributed"],
        ["Host.Api"] = ["Profiles.Embedded", "Serialization"],
        ["Worker"] = ["Profiles.Embedded", "Serialization"],
        ["Extensions.OutOfProcess.Host"] = ["Extensions.OutOfProcess", "Extensions.Dynamic", "Serialization", "Runtime"],
    };
    public static bool HasCycle(IReadOnlyDictionary<string,string[]> graph)
    {
        var active = new HashSet<string>(StringComparer.Ordinal); var done = new HashSet<string>(StringComparer.Ordinal);
        bool Visit(string n) { if (active.Contains(n)) return true; if (!done.Add(n)) return false; if (!graph.TryGetValue(n, out var dependencies)) return false; active.Add(n); foreach (var d in dependencies) if (Visit(d)) return true; active.Remove(n); return false; }
        return graph.Keys.Any(Visit);
    }
}
