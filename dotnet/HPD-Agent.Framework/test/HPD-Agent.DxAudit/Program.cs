using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.AspNetCore.Evaluations;
using HPD.Agent.MultiAgent.AspNetCore;
using HPD.Agent.MultiAgent.HpdBase;
using HPD.Agent.DxAudit;
using HPD.MultiAgent;

var strict = args.Contains("--strict", StringComparer.OrdinalIgnoreCase);
var strictMultiAgent = args.Contains("--strict-multiagent", StringComparer.OrdinalIgnoreCase);
var strictEvaluations = args.Contains("--strict-evaluations", StringComparer.OrdinalIgnoreCase) ||
                        strict;
var apiOnly = args.Contains("--api-only", StringComparer.OrdinalIgnoreCase);
var snapshotPath = ReadOption(args, "--api-snapshot");
var baselinePath = ReadOption(args, "--api-baseline");
var updateBaselinePath = ReadOption(args, "--update-api-baseline");
baselinePath ??= updateBaselinePath is null
    ? FindDefaultApiBaseline("api-surface.json")
    : null;
var multiAgentSnapshotPath = ReadOption(args, "--multiagent-api-snapshot");
var multiAgentBaselinePath = ReadOption(args, "--multiagent-api-baseline");
var updateMultiAgentBaselinePath = ReadOption(args, "--update-multiagent-api-baseline");
multiAgentBaselinePath ??= updateMultiAgentBaselinePath is null
    ? FindDefaultApiBaseline("multiagent-api-surface.json")
    : null;
var evaluationSnapshotPath = ReadOption(args, "--evaluation-api-snapshot");
var evaluationBaselinePath = ReadOption(args, "--evaluation-api-baseline");
var updateEvaluationBaselinePath = ReadOption(args, "--update-evaluation-api-baseline");
evaluationBaselinePath ??= updateEvaluationBaselinePath is null
    ? FindDefaultApiBaseline("evaluations-api-surface.json")
    : null;

using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var results = apiOnly ? [] : await AuditSuite.RunAsync(cancellation.Token);
var surface = ApiSurfaceSnapshot.Capture(typeof(Agent).Assembly);
var multiAgentSurface = ApiSurfaceSnapshot.Capture(
    typeof(AgentWorkflowInstance).Assembly,
    typeof(IHPDMultiAgentWorkflowBackend).Assembly,
    typeof(HPDMultiAgentHpdBaseServiceCollectionExtensions).Assembly);
var evaluationSurface = ApiSurfaceSnapshot.Capture(
    typeof(HPD.Agent.Evaluations.Eval).Assembly,
    typeof(HPDAgentEvaluationServiceCollectionExtensions).Assembly);

Console.WriteLine("# HPD-Agent DX audit");
Console.WriteLine();
Console.WriteLine($"API surface: {surface.TypeCount} types, {surface.MethodCount} methods, " +
    $"{surface.ConstructorCount} constructors, {surface.PropertyCount} properties, " +
    $"{surface.FieldCount} fields, {surface.EventCount} events, SHA-256 {surface.Fingerprint}");
Console.WriteLine();

foreach (var result in results)
{
    Console.WriteLine($"{StatusLabel(result.Status),-7} {result.Id} [{result.Area}]");
    Console.WriteLine($"        {result.Summary}");
    if (!string.IsNullOrWhiteSpace(result.Detail))
        Console.WriteLine($"        {result.Detail}");
}

var probeFailures = results.Count(result =>
    result.Status is AuditStatus.Failure or AuditStatus.Gap);
var apiDifferences = await CompareBaselineAsync(surface, baselinePath, cancellation.Token);
var multiAgentApiDifferences = await CompareBaselineAsync(
    multiAgentSurface, multiAgentBaselinePath, cancellation.Token);
var evaluationApiDifferences = await CompareBaselineAsync(
    evaluationSurface, evaluationBaselinePath, cancellation.Token);

Console.WriteLine();
Console.WriteLine("API baseline");
if (baselinePath is null)
{
    Console.WriteLine("        NOT CHECKED");
}
else if (apiDifferences.Length == 0)
{
    Console.WriteLine($"        MATCH ({Path.GetFullPath(baselinePath)})");
}
else
{
    Console.WriteLine($"        CHANGED ({apiDifferences.Length} differences)");
    foreach (var difference in apiDifferences)
        Console.WriteLine($"        {difference}");
}

Console.WriteLine();
Console.WriteLine("Multi-agent API baseline");
Console.WriteLine($"        {multiAgentSurface.TypeCount} types, {multiAgentSurface.MethodCount} methods, " +
    $"{multiAgentSurface.ConstructorCount} constructors, {multiAgentSurface.PropertyCount} properties, " +
    $"{multiAgentSurface.FieldCount} fields, {multiAgentSurface.EventCount} events, " +
    $"SHA-256 {multiAgentSurface.Fingerprint}");
if (multiAgentBaselinePath is null)
{
    Console.WriteLine("        NOT CHECKED");
}
else if (multiAgentApiDifferences.Length == 0)
{
    Console.WriteLine($"        MATCH ({Path.GetFullPath(multiAgentBaselinePath)})");
}
else
{
    Console.WriteLine($"        CHANGED ({multiAgentApiDifferences.Length} differences)");
    foreach (var difference in multiAgentApiDifferences)
        Console.WriteLine($"        {difference}");
}

Console.WriteLine();
Console.WriteLine("Evaluations API baseline");
Console.WriteLine($"        {evaluationSurface.TypeCount} types, {evaluationSurface.MethodCount} methods, " +
                  $"{evaluationSurface.ConstructorCount} constructors, {evaluationSurface.PropertyCount} properties, " +
                  $"{evaluationSurface.FieldCount} fields, {evaluationSurface.EventCount} events, " +
                  $"SHA-256 {evaluationSurface.Fingerprint}");
if (evaluationBaselinePath is null)
{
    Console.WriteLine("        NOT CHECKED");
}
else if (evaluationApiDifferences.Length == 0)
{
    Console.WriteLine($"        MATCH ({Path.GetFullPath(evaluationBaselinePath)})");
}
else
{
    Console.WriteLine($"        CHANGED ({evaluationApiDifferences.Length} differences)");
    foreach (var difference in evaluationApiDifferences)
        Console.WriteLine($"        {difference}");
}

Console.WriteLine();
Console.WriteLine(
    $"Summary: {results.Count(result => result.Status == AuditStatus.Pass)} passed, " +
    $"{results.Count(result => result.Status == AuditStatus.Failure)} failed, " +
    $"{results.Count(result => result.Status == AuditStatus.Gap)} surface gaps, " +
    $"{results.Count(result => result.Status == AuditStatus.NotRun)} not run, " +
    $"{apiDifferences.Length} core API differences, " +
    $"{multiAgentApiDifferences.Length} multi-agent API differences, " +
    $"{evaluationApiDifferences.Length} evaluation API differences.");

if (snapshotPath is not null)
{
    var fullPath = Path.GetFullPath(snapshotPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    await File.WriteAllTextAsync(fullPath, surface.ToJson(), cancellation.Token);
    Console.WriteLine($"API snapshot written to {fullPath}");
}

if (multiAgentSnapshotPath is not null)
{
    var fullPath = Path.GetFullPath(multiAgentSnapshotPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    await File.WriteAllTextAsync(fullPath, multiAgentSurface.ToJson(), cancellation.Token);
    Console.WriteLine($"Multi-agent API snapshot written to {fullPath}");
}

if (evaluationSnapshotPath is not null)
{
    var fullPath = Path.GetFullPath(evaluationSnapshotPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    await File.WriteAllTextAsync(fullPath, evaluationSurface.ToJson(), cancellation.Token);
    Console.WriteLine($"Evaluation API snapshot written to {fullPath}");
}

if (updateBaselinePath is not null)
{
    var fullPath = Path.GetFullPath(updateBaselinePath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    await File.WriteAllTextAsync(fullPath, surface.ToJson(), cancellation.Token);
    Console.WriteLine($"API baseline written to {fullPath}");
}

if (updateMultiAgentBaselinePath is not null)
{
    var fullPath = Path.GetFullPath(updateMultiAgentBaselinePath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    await File.WriteAllTextAsync(fullPath, multiAgentSurface.ToJson(), cancellation.Token);
    Console.WriteLine($"Multi-agent API baseline written to {fullPath}");
}

if (updateEvaluationBaselinePath is not null)
{
    var fullPath = Path.GetFullPath(updateEvaluationBaselinePath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    await File.WriteAllTextAsync(fullPath, evaluationSurface.ToJson(), cancellation.Token);
    Console.WriteLine($"Evaluation API baseline written to {fullPath}");
}

var failed = strict && (probeFailures > 0 || apiDifferences.Length > 0) ||
             strictMultiAgent && multiAgentApiDifferences.Length > 0 ||
             strictEvaluations && evaluationApiDifferences.Length > 0;
return failed ? 1 : 0;

static string? ReadOption(string[] arguments, string name)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            return arguments[index + 1];
    }

    return null;
}

static async Task<string[]> CompareBaselineAsync(
    ApiSurfaceSnapshot actual,
    string? baselinePath,
    CancellationToken cancellationToken)
{
    if (baselinePath is null)
        return [];

    var fullPath = Path.GetFullPath(baselinePath);
    if (!File.Exists(fullPath))
        return [$"API baseline does not exist: {fullPath}"];

    var json = await File.ReadAllTextAsync(fullPath, cancellationToken);
    var baseline = JsonSerializer.Deserialize<ApiSurfaceSnapshot>(json)
        ?? throw new InvalidOperationException($"API baseline is empty: {fullPath}");
    return actual.Diff(baseline);
}

static string? FindDefaultApiBaseline(string fileName)
{
    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
         directory is not null;
         directory = directory.Parent)
    {
        var candidate = Path.Combine(
            directory.FullName,
            "test",
            "HPD-Agent.DxAudit",
            "baseline",
            fileName);
        if (File.Exists(candidate))
            return candidate;
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
    int ConstructorCount,
    int PropertyCount,
    int FieldCount,
    int EventCount,
    string Fingerprint,
    IReadOnlyList<ApiTypeSnapshot> Types)
{
    public static ApiSurfaceSnapshot Capture(params Assembly[] assemblies)
    {
        var types = assemblies
            .SelectMany(static assembly => assembly.GetExportedTypes())
            .GroupBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(ApiTypeSnapshot.From)
            .ToArray();

        var canonical = new StringBuilder();
        foreach (var type in types)
        {
            canonical.AppendLine($"T:{type.FullName}");
            foreach (var attribute in type.Attributes)
                canonical.AppendLine($"A:{attribute}");
            canonical.AppendLine($"B:{type.BaseType}");
            foreach (var interfaceType in type.Interfaces)
                canonical.AppendLine($"I:{interfaceType}");
            foreach (var constructor in type.Constructors)
                canonical.AppendLine($"C:{constructor}");
            foreach (var method in type.Methods)
                canonical.AppendLine($"M:{method}");
            foreach (var property in type.Properties)
                canonical.AppendLine($"P:{property}");
            foreach (var field in type.Fields)
                canonical.AppendLine($"F:{field}");
            foreach (var @event in type.Events)
                canonical.AppendLine($"E:{@event}");
        }

        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();

        return new ApiSurfaceSnapshot(
            types.Length,
            types.Sum(type => type.Methods.Count),
            types.Sum(type => type.Constructors.Count),
            types.Sum(type => type.Properties.Count),
            types.Sum(type => type.Fields.Count),
            types.Sum(type => type.Events.Count),
            fingerprint,
            types);
    }

    public string[] Diff(ApiSurfaceSnapshot baseline)
    {
        var expectedTypes = baseline.Types.ToDictionary(type => type.FullName, StringComparer.Ordinal);
        var actualTypes = Types.ToDictionary(type => type.FullName, StringComparer.Ordinal);
        var differences = new List<string>();

        foreach (var removed in expectedTypes.Keys.Except(actualTypes.Keys, StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
            differences.Add($"- type {removed}");
        foreach (var added in actualTypes.Keys.Except(expectedTypes.Keys, StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
            differences.Add($"+ type {added}");
        foreach (var shared in expectedTypes.Keys.Intersect(actualTypes.Keys, StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            var expected = expectedTypes[shared];
            var actual = actualTypes[shared];
            DiffMembers("constructor", expected.Constructors, actual.Constructors, differences);
            DiffMembers("method", expected.Methods, actual.Methods, differences);
            DiffMembers("property", expected.Properties, actual.Properties, differences);
            DiffMembers("field", expected.Fields, actual.Fields, differences);
            DiffMembers("event", expected.Events, actual.Events, differences);
            DiffMembers("attribute", expected.Attributes, actual.Attributes, differences);
            DiffMembers("base type", [expected.BaseType], [actual.BaseType], differences);
            DiffMembers("interface", expected.Interfaces, actual.Interfaces, differences);
        }

        return differences.ToArray();

        static void DiffMembers(
            string kind,
            IReadOnlyList<string?> expected,
            IReadOnlyList<string?> actual,
            List<string> differences)
        {
            foreach (var removed in expected.Except(actual, StringComparer.Ordinal)
                         .OfType<string>().Order(StringComparer.Ordinal))
                differences.Add($"- {kind} {removed}");
            foreach (var added in actual.Except(expected, StringComparer.Ordinal)
                         .OfType<string>().Order(StringComparer.Ordinal))
                differences.Add($"+ {kind} {added}");
        }
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
    IReadOnlyList<string> Attributes,
    string? BaseType,
    IReadOnlyList<string> Interfaces,
    IReadOnlyList<string> Constructors,
    IReadOnlyList<string> Methods,
    IReadOnlyList<string> Properties,
    IReadOnlyList<string> Fields,
    IReadOnlyList<string> Events)
{
    public static ApiTypeSnapshot From(Type type) => new(
        type.FullName ?? type.Name,
        FormatAttributes(type.GetCustomAttributesData()),
        type.BaseType is null ? null : FormatType(type.BaseType, NullabilityState.NotNull),
        type.GetInterfaces()
            .Select(interfaceType => FormatType(interfaceType, NullabilityState.NotNull))
            .Order(StringComparer.Ordinal)
            .ToArray(),
        type.GetConstructors(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly)
            .Select(FormatConstructor)
            .Order(StringComparer.Ordinal)
            .ToArray(),
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
            .Select(FormatProperty)
            .Order(StringComparer.Ordinal)
            .ToArray(),
        type.GetFields(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)
            .Select(FormatField)
            .Order(StringComparer.Ordinal)
            .ToArray(),
        type.GetEvents(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)
            .Select(FormatEvent)
            .Order(StringComparer.Ordinal)
            .ToArray());

    private static string FormatMethod(MethodInfo method)
    {
        var nullability = new NullabilityInfoContext();
        var genericArguments = method.IsGenericMethodDefinition
            ? $"<{string.Join(",", method.GetGenericArguments().Select(argument => argument.Name))}>" +
              FormatGenericConstraints(method.GetGenericArguments())
            : string.Empty;
        var parameters = string.Join(
            ",",
            method.GetParameters().Select(parameter =>
                FormatParameter(parameter, nullability.Create(parameter))));
        var modifiers = FormatMethodModifiers(method);
        var returnType = FormatType(method.ReturnType, nullability.Create(method.ReturnParameter).ReadState);

        return $"{FormatAttributes(method.GetCustomAttributesData())}" +
            $"{FormatAttributes(method.ReturnParameter.GetCustomAttributesData())}" +
            $"{modifiers}{returnType} {method.Name}{genericArguments}({parameters})";
    }

    private static string FormatConstructor(ConstructorInfo constructor)
    {
        var nullability = new NullabilityInfoContext();
        var parameters = string.Join(
            ",",
            constructor.GetParameters().Select(parameter =>
                FormatParameter(parameter, nullability.Create(parameter))));
        return $"{FormatAttributes(constructor.GetCustomAttributesData())}.ctor({parameters})";
    }

    private static string FormatProperty(PropertyInfo property)
    {
        var nullabilityContext = new NullabilityInfoContext();
        var nullability = nullabilityContext.Create(property);
        var modifier = property.GetMethod?.IsStatic == true ||
            property.SetMethod?.IsStatic == true
            ? "static "
            : string.Empty;
        var indexParameters = string.Join(
            ",",
            property.GetIndexParameters().Select(parameter =>
                FormatParameter(parameter, nullabilityContext.Create(parameter))));
        var name = property.GetIndexParameters().Length == 0
            ? property.Name
            : $"{property.Name}[{indexParameters}]";
        var accessors = new List<string>();
        if (property.GetMethod is not null)
            accessors.Add("get;");
        if (property.SetMethod is { IsPublic: true })
            accessors.Add(IsInitOnly(property.SetMethod) ? "init;" : "set;");
        else if (property.SetMethod is not null)
            accessors.Add($"{Accessibility(property.SetMethod)} set;");

        return $"{FormatAttributes(property.GetCustomAttributesData())}" +
            $"{modifier}{FormatType(property.PropertyType, nullability.ReadState)} {name} {{ " +
            $"{string.Join(' ', accessors)} }}";
    }

    private static string FormatField(FieldInfo field)
    {
        var nullability = new NullabilityInfoContext().Create(field).ReadState;
        var modifiers = new List<string>();
        if (field.IsLiteral)
            modifiers.Add("const");
        else if (field.IsStatic)
            modifiers.Add("static");
        if (field.IsInitOnly)
            modifiers.Add("readonly");
        var suffix = string.Empty;
        if (field.IsLiteral && field.GetRawConstantValue() is { } value)
            suffix = $" = {FormatConstant(value)}";
        return $"{FormatAttributes(field.GetCustomAttributesData())}" +
            $"{string.Join(' ', modifiers)}{(modifiers.Count > 0 ? " " : string.Empty)}" +
            $"{FormatType(field.FieldType, nullability)} {field.Name}{suffix}";
    }

    private static string FormatEvent(EventInfo @event)
    {
        var nullability = new NullabilityInfoContext().Create(@event).ReadState;
        var modifier = @event.AddMethod?.IsStatic == true ? "static " : string.Empty;
        return $"{FormatAttributes(@event.GetCustomAttributesData())}" +
            $"{modifier}{FormatType(@event.EventHandlerType!, nullability)} {@event.Name}";
    }

    private static string FormatParameter(ParameterInfo parameter, NullabilityInfo nullability)
    {
        var prefix = parameter.IsOut
            ? "out "
            : parameter.ParameterType.IsByRef && parameter.IsIn
                ? "in "
                : parameter.ParameterType.IsByRef
                    ? "ref "
                    : parameter.GetCustomAttribute<ParamArrayAttribute>() is not null
                        ? "params "
                        : string.Empty;
        var suffix = parameter.HasDefaultValue
            ? $" = {FormatConstant(parameter.DefaultValue)}"
            : string.Empty;
        return $"{FormatAttributes(parameter.GetCustomAttributesData())}{prefix}" +
            $"{FormatType(parameter.ParameterType, nullability.ReadState)} " +
            $"{parameter.Name}{suffix}";
    }

    private static string FormatMethodModifiers(MethodInfo method)
    {
        var modifiers = new List<string>();
        if (method.IsStatic)
            modifiers.Add("static");
        if (method.IsAbstract)
            modifiers.Add("abstract");
        else if (method.IsVirtual && method.GetBaseDefinition() != method)
            modifiers.Add("override");
        else if (method.IsVirtual && !method.IsFinal)
            modifiers.Add("virtual");
        return modifiers.Count == 0 ? string.Empty : $"{string.Join(' ', modifiers)} ";
    }

    private static string FormatGenericConstraints(IEnumerable<Type> arguments) =>
        string.Concat(arguments.Select(argument =>
        {
            var constraints = new List<string>();
            var attributes = argument.GenericParameterAttributes;
            if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                constraints.Add("class");
            if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                constraints.Add("struct");
            constraints.AddRange(argument.GetGenericParameterConstraints()
                .Where(constraint => constraint != typeof(ValueType))
                .Select(constraint => FormatType(constraint, NullabilityState.NotNull))
                .Order(StringComparer.Ordinal));
            if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0 &&
                (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
                constraints.Add("new()");
            return constraints.Count == 0
                ? string.Empty
                : $" where {argument.Name} : {string.Join(",", constraints)}";
        }));

    private static string Accessibility(MethodInfo accessor) =>
        accessor.IsPublic
            ? "public"
            : accessor.IsAssembly
                ? "internal"
                : accessor.IsFamily
                    ? "protected"
                    : accessor.IsFamilyOrAssembly
                        ? "protected internal"
                        : accessor.IsFamilyAndAssembly
                            ? "private protected"
                            : "private";

    private static IReadOnlyList<string> FormatAttributes(
        IEnumerable<CustomAttributeData> attributes) =>
        attributes
            .Where(static attribute => IsApiAttribute(attribute.AttributeType))
            .Select(static attribute =>
            {
                var positional = attribute.ConstructorArguments
                    .Select(FormatAttributeArgument);
                var named = attribute.NamedArguments
                    .Select(argument =>
                        $"{argument.MemberName}={FormatAttributeValue(argument.TypedValue)}");
                var arguments = positional.Concat(named).ToArray();
                return arguments.Length == 0
                    ? $"[{attribute.AttributeType.Name}]"
                    : $"[{attribute.AttributeType.Name}({string.Join(",", arguments)})]";
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsApiAttribute(Type attributeType)
    {
        const string codeAnalysis = "System.Diagnostics.CodeAnalysis.";
        const string json = "System.Text.Json.Serialization.";
        var fullName = attributeType.FullName ?? attributeType.Name;
        return fullName.StartsWith(codeAnalysis, StringComparison.Ordinal) ||
            fullName.StartsWith(json, StringComparison.Ordinal) ||
            fullName.StartsWith("HPD.", StringComparison.Ordinal) ||
            fullName is
                "System.ObsoleteAttribute" or
                "System.FlagsAttribute" or
                "System.AttributeUsageAttribute" or
                "System.ComponentModel.EditorBrowsableAttribute";
    }

    private static string FormatAttributeArgument(CustomAttributeTypedArgument argument) =>
        FormatAttributeValue(argument);

    private static string FormatAttributeValue(CustomAttributeTypedArgument argument)
    {
        if (argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> arguments)
            return $"[{string.Join(",", arguments.Select(FormatAttributeArgument))}]";
        return FormatConstant(argument.Value);
    }

    private static string FormatConstant(object? value) => value switch
    {
        null => "null",
        string text => JsonSerializer.Serialize(text),
        char character => JsonSerializer.Serialize(character.ToString()),
        bool boolean => boolean ? "true" : "false",
        Type type => $"typeof({FormatType(type, NullabilityState.NotNull)})",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "null"
    };

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Contains(typeof(IsExternalInit));

    private static string FormatType(Type type, NullabilityState nullability)
    {
        if (type.IsByRef)
            type = type.GetElementType()!;
        if (type.IsArray)
        {
            var element = FormatType(type.GetElementType()!, NullabilityState.NotNull);
            return nullability == NullabilityState.Nullable ? $"{element}[]?" : $"{element}[]";
        }
        if (!type.IsGenericType)
            return $"{type.FullName ?? type.Name}" +
                (nullability == NullabilityState.Nullable ? "?" : string.Empty);

        var definition = type.GetGenericTypeDefinition();
        var name = definition.FullName ?? definition.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
            name = name[..tick];
        var formatted = $"{name}<" +
            $"{string.Join(",", type.GetGenericArguments().Select(argument =>
                FormatType(argument, NullabilityState.NotNull)))}>";
        return nullability == NullabilityState.Nullable ? $"{formatted}?" : formatted;
    }
}
