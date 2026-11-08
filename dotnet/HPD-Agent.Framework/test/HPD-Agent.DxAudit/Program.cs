using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent;
using HPD.Agent.DxAudit;

var strict = args.Contains("--strict", StringComparer.OrdinalIgnoreCase);
var snapshotPath = ReadOption(args, "--api-snapshot");

using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var results = await AuditSuite.RunAsync(cancellation.Token);
var surface = ApiSurfaceSnapshot.Capture(typeof(Agent).Assembly);

Console.WriteLine("# HPD-Agent DX audit");
Console.WriteLine();
Console.WriteLine($"API surface: {surface.TypeCount} types, {surface.MethodCount} methods, " +
    $"{surface.PropertyCount} properties, SHA-256 {surface.Fingerprint}");
Console.WriteLine();

foreach (var result in results)
{
    Console.WriteLine($"{StatusLabel(result.Status),-7} {result.Id} [{result.Area}]");
    Console.WriteLine($"        {result.Summary}");
    if (!string.IsNullOrWhiteSpace(result.Detail))
        Console.WriteLine($"        {result.Detail}");
}

var failures = results.Count(result =>
    result.Status is AuditStatus.Failure or AuditStatus.Gap);

Console.WriteLine();
Console.WriteLine(
    $"Summary: {results.Count(result => result.Status == AuditStatus.Pass)} passed, " +
    $"{results.Count(result => result.Status == AuditStatus.Failure)} failed, " +
    $"{results.Count(result => result.Status == AuditStatus.Gap)} surface gaps, " +
    $"{results.Count(result => result.Status == AuditStatus.NotRun)} not run.");

if (snapshotPath is not null)
{
    var fullPath = Path.GetFullPath(snapshotPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    await File.WriteAllTextAsync(fullPath, surface.ToJson(), cancellation.Token);
    Console.WriteLine($"API snapshot written to {fullPath}");
}

return strict && failures > 0 ? 1 : 0;

static string? ReadOption(string[] arguments, string name)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            return arguments[index + 1];
    }

    return null;
}

static string StatusLabel(AuditStatus status) => status switch
{
    AuditStatus.Pass => "PASS",
    AuditStatus.Failure => "FAIL",
    AuditStatus.Gap => "GAP",
    AuditStatus.NotRun => "NOTRUN",
    _ => "UNKNOWN"
};

internal enum AuditStatus
{
    Pass,
    Failure,
    Gap,
    NotRun
}

internal sealed record ProbeResult(
    string Id,
    string Area,
    AuditStatus Status,
    string Summary,
    string? Detail = null);

internal sealed record ApiSurfaceSnapshot(
    int TypeCount,
    int MethodCount,
    int PropertyCount,
    string Fingerprint,
    IReadOnlyList<ApiTypeSnapshot> Types)
{
    public static ApiSurfaceSnapshot Capture(Assembly assembly)
    {
        var types = assembly.GetExportedTypes()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(ApiTypeSnapshot.From)
            .ToArray();

        var canonical = new StringBuilder();
        foreach (var type in types)
        {
            canonical.AppendLine($"T:{type.FullName}");
            foreach (var method in type.Methods)
                canonical.AppendLine($"M:{method}");
            foreach (var property in type.Properties)
                canonical.AppendLine($"P:{property}");
        }

        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();

        return new ApiSurfaceSnapshot(
            types.Length,
            types.Sum(type => type.Methods.Count),
            types.Sum(type => type.Properties.Count),
            fingerprint,
            types);
    }

    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(
        this,
        new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
}

internal sealed record ApiTypeSnapshot(
    string FullName,
    IReadOnlyList<string> Methods,
    IReadOnlyList<string> Properties)
{
    public static ApiTypeSnapshot From(Type type) => new(
        type.FullName ?? type.Name,
        type.GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(FormatMethod)
            .Order(StringComparer.Ordinal)
            .ToArray(),
        type.GetProperties(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)
            .Select(property => $"{FormatType(property.PropertyType)} {property.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray());

    private static string FormatMethod(MethodInfo method)
    {
        var genericArguments = method.IsGenericMethodDefinition
            ? $"<{string.Join(",", method.GetGenericArguments().Select(argument => argument.Name))}>"
            : string.Empty;
        var parameters = string.Join(
            ",",
            method.GetParameters().Select(parameter =>
                $"{FormatType(parameter.ParameterType)} {parameter.Name}"));

        return $"{FormatType(method.ReturnType)} {method.Name}{genericArguments}({parameters})";
    }

    private static string FormatType(Type type)
    {
        if (type.IsByRef)
            return $"{FormatType(type.GetElementType()!)}&";
        if (type.IsArray)
            return $"{FormatType(type.GetElementType()!)}[]";
        if (!type.IsGenericType)
            return type.FullName ?? type.Name;

        var definition = type.GetGenericTypeDefinition();
        var name = definition.FullName ?? definition.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
            name = name[..tick];
        return $"{name}<{string.Join(",", type.GetGenericArguments().Select(FormatType))}>";
    }
}
