using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.DxAudit;

var strict = args.Contains("--strict", StringComparer.OrdinalIgnoreCase);
var snapshotPath = ReadOption(args, "--api-snapshot");
var baselinePath = ReadOption(args, "--api-baseline");
var updateBaselinePath = ReadOption(args, "--update-api-baseline");
baselinePath ??= updateBaselinePath is null
    ? FindDefaultApiBaseline()
    : null;

using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var results = await AuditSuite.RunAsync(cancellation.Token);
var surface = ApiSurfaceSnapshot.Capture(typeof(Agent).Assembly);

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
var apiDifferences = Array.Empty<string>();
if (baselinePath is not null)
{
    var fullPath = Path.GetFullPath(baselinePath);
    if (!File.Exists(fullPath))
    {
        apiDifferences = [$"API baseline does not exist: {fullPath}"];
    }
    else
    {
        var json = await File.ReadAllTextAsync(fullPath, cancellation.Token);
        var baseline = JsonSerializer.Deserialize<ApiSurfaceSnapshot>(json)
            ?? throw new InvalidOperationException($"API baseline is empty: {fullPath}");
        apiDifferences = surface.Diff(baseline);
    }
}

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
Console.WriteLine(
    $"Summary: {results.Count(result => result.Status == AuditStatus.Pass)} passed, " +
    $"{results.Count(result => result.Status == AuditStatus.Failure)} failed, " +
    $"{results.Count(result => result.Status == AuditStatus.Gap)} surface gaps, " +
    $"{results.Count(result => result.Status == AuditStatus.NotRun)} not run, " +
    $"{apiDifferences.Length} API differences.");

if (snapshotPath is not null)
{
    var fullPath = Path.GetFullPath(snapshotPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    await File.WriteAllTextAsync(fullPath, surface.ToJson(), cancellation.Token);
    Console.WriteLine($"API snapshot written to {fullPath}");
}

if (updateBaselinePath is not null)
{
    var fullPath = Path.GetFullPath(updateBaselinePath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    await File.WriteAllTextAsync(fullPath, surface.ToJson(), cancellation.Token);
    Console.WriteLine($"API baseline written to {fullPath}");
}

return strict && (probeFailures > 0 || apiDifferences.Length > 0) ? 1 : 0;

static string? ReadOption(string[] arguments, string name)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            return arguments[index + 1];
    }

    return null;
}

static string? FindDefaultApiBaseline()
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
            "api-surface.json");
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
