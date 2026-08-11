using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using HPD.Gateway.PublicApiLedger.Tests;

if (args.Length == 3 && args[0] == "--validate-consolidated-root")
{
    ValidateConsolidatedRoot(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
    return 0;
}
if (args.Length == 3 && args[0] == "--validate-consolidated-control-plane")
{
    ValidateConsolidatedControlPlane(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
    return 0;
}

if (args.Length is < 2 or > 4)
{
    Console.Error.WriteLine("Usage: HPD.Gateway.PublicApiLedger <assembly-directory> <output-file> [classification-file] [product-manifest-file]");
    return 2;
}

string assemblyDirectory = Path.GetFullPath(args[0]);
string outputFile = Path.GetFullPath(args[1]);
if (!Directory.Exists(assemblyDirectory))
    throw new DirectoryNotFoundException(assemblyDirectory);

RunSignatureClosureSelfTests();
Console.WriteLine("Signature-closure adversarial fixtures passed: type generic constraint and indexer parameter leaks rejected.");

var ownership = new Dictionary<string, Ownership>(StringComparer.Ordinal)
{
    ["HPD.Gateway"] = new("RootPublic", "HPD.Gateway", "HPD.Gateway"),
    ["HPD.Gateway.Abstractions"] = new("RootPublic", "HPD.Gateway", "HPD.Gateway"),
    ["HPD.Gateway.Core"] = new("RootPublic", "HPD.Gateway", "HPD.Gateway"),
    ["HPD.Gateway.Effective"] = new("RootPublic", "HPD.Gateway", "HPD.Gateway"),
    ["HPD.Gateway.Hosting"] = new("RootPublic", "HPD.Gateway", "HPD.Gateway"),
    ["HPD.Gateway.Inspection"] = new("RootPublic", "HPD.Gateway", "HPD.Gateway"),
    ["HPD.Gateway.OutputCaching"] = new("RootPublic", "HPD.Gateway", "HPD.Gateway"),
    ["HPD.Gateway.Resilience"] = new("RootPublic", "HPD.Gateway", "HPD.Gateway"),
    ["HPD.Gateway.Status"] = new("RootPublic", "HPD.Gateway", "HPD.Gateway"),
    ["HPD.Gateway.Yarp"] = new("RootPublic", "HPD.Gateway", "HPD.Gateway"),
    ["HPD.Gateway.Admin"] = new("ControlPlanePublic", "HPD.Gateway.ControlPlane", "HPD.Gateway.ControlPlane"),
    ["HPD.Gateway.Management"] = new("ControlPlanePublic", "HPD.Gateway.ControlPlane", "HPD.Gateway.ControlPlane"),
    ["HPD.Gateway.Studio"] = new("ControlPlanePublic", "HPD.Gateway.ControlPlane", "HPD.Gateway.ControlPlane"),
    ["HPD.Gateway.HPDAuth"] = new("HpdAuthPublic", "HPD.Gateway.ControlPlane.HPDAuth", "HPD.Gateway.ControlPlane.HPDAuth"),
    ["HPD.Gateway.Discovery.Microsoft"] = new("MicrosoftDiscoveryPublic", "HPD.Gateway.Discovery.Microsoft", "HPD.Gateway.Discovery.Microsoft")
};

var records = new List<LedgerRecord>();
foreach ((string assemblyName, Ownership target) in ownership.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
{
    string assemblyPath = Path.Combine(assemblyDirectory, assemblyName + ".dll");
    if (!File.Exists(assemblyPath))
        throw new FileNotFoundException($"Required Gateway assembly '{assemblyName}' was not found.", assemblyPath);

    using var stream = File.OpenRead(assemblyPath);
    using var pe = new PEReader(stream);
    MetadataReader metadata = pe.GetMetadataReader();
    foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
    {
        TypeDefinition definition = metadata.GetTypeDefinition(handle);
        if (!IsPublic(definition.Attributes))
            continue;

        string name = metadata.GetString(definition.Name);
        if (name == "<Module>")
            continue;

        string currentNamespace = GetEffectiveNamespace(metadata, handle);
        string typePath = GetTypePath(metadata, handle);
        string currentName = string.IsNullOrEmpty(currentNamespace) ? typePath : currentNamespace + "." + typePath;
        string finalName = target.Namespace + "." + typePath;
        records.Add(new(
            assemblyName,
            currentNamespace,
            currentName,
            target.Disposition,
            target.Product,
            target.Namespace,
            finalName,
            "PublicPendingFixtureOrContract",
            "Pending",
            "Pending"));
    }
}

records.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.CurrentType, right.CurrentType));
var duplicateFinalNames = records
    .GroupBy(static record => record.FinalType, StringComparer.Ordinal)
    .Where(static group => group.Count() > 1)
    .Select(static group => group.Key)
    .OrderBy(static value => value, StringComparer.Ordinal)
    .ToArray();
if (duplicateFinalNames.Length != 0)
    throw new InvalidOperationException("Duplicate final type names: " + string.Join(", ", duplicateFinalNames));

var envelope = new LedgerEnvelope(
    "hpd-gateway-public-type-ownership/v1",
    records.Count,
    records);
Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
byte[] json = JsonSerializer.SerializeToUtf8Bytes(envelope, LedgerJsonContext.Default.LedgerEnvelope);
File.WriteAllBytes(outputFile, json.Concat([(byte)'\n']).ToArray());
Console.WriteLine($"Generated {records.Count} public type ownership records at {outputFile}.");
if (args.Length == 3)
{
    ValidateClassification(Path.GetFullPath(args[2]), assemblyDirectory, records, ownership.Keys, null);
    Console.WriteLine("Classification coverage and public signature closure passed.");
}
else if (args.Length == 4)
{
    IReadOnlyDictionary<string, ProductManifest> products = ValidateProductManifests(Path.GetFullPath(args[3]), ownership.Keys);
    ValidateClassification(Path.GetFullPath(args[2]), assemblyDirectory, records, ownership.Keys, products);
    Console.WriteLine("Classification, signature closure, and product manifests passed.");
}
return 0;

static void ValidateConsolidatedRoot(string assemblyPath, string classificationFile)
{
    if (!File.Exists(assemblyPath))
        throw new FileNotFoundException("Consolidated HPD.Gateway assembly was not found.", assemblyPath);

    using JsonDocument classification = JsonDocument.Parse(File.ReadAllBytes(classificationFile));
    string[] expected = classification.RootElement.GetProperty("records").EnumerateArray()
        .Where(static record => record.GetProperty("finalProduct").GetString() == "HPD.Gateway" &&
            record.GetProperty("disposition").GetString() == "RootPublic")
        .Select(static record => record.GetProperty("finalType").GetString()!)
        .Order(StringComparer.Ordinal).ToArray();

    using var stream = File.OpenRead(assemblyPath);
    using var pe = new PEReader(stream);
    MetadataReader metadata = pe.GetMetadataReader();
    string[] actual = metadata.TypeDefinitions
        .Where(handle => IsPublic(metadata.GetTypeDefinition(handle).Attributes))
        .Select(handle =>
        {
            string typeNamespace = GetEffectiveNamespace(metadata, handle);
            string typePath = GetTypePath(metadata, handle);
            return string.IsNullOrEmpty(typeNamespace) ? typePath : typeNamespace + "." + typePath;
        })
        .Where(static name => name != "<Module>")
        .Order(StringComparer.Ordinal).ToArray();

    string[] missing = expected.Except(actual, StringComparer.Ordinal).ToArray();
    string[] unexpected = actual.Except(expected, StringComparer.Ordinal).ToArray();
    if (missing.Length != 0 || unexpected.Length != 0)
        throw new InvalidOperationException(
            $"Consolidated root public surface differs from the accepted classification. " +
            $"Missing: [{string.Join(", ", missing)}]. Unexpected: [{string.Join(", ", unexpected)}].");
    if (actual.Any(static name => !name.StartsWith("HPD.Gateway.", StringComparison.Ordinal) ||
            name.StartsWith("HPD.Gateway.Abstractions.", StringComparison.Ordinal) ||
            name.StartsWith("HPD.Gateway.Core.", StringComparison.Ordinal) ||
            name.StartsWith("HPD.Gateway.Hosting.", StringComparison.Ordinal) ||
            name.StartsWith("HPD.Gateway.Yarp.", StringComparison.Ordinal)))
        throw new InvalidOperationException("Consolidated root contains an obsolete or foreign public namespace.");

    Console.WriteLine($"Consolidated HPD.Gateway public surface matches all {actual.Length} accepted root contracts.");
}

static void ValidateConsolidatedControlPlane(string assemblyPath, string classificationFile)
{
    if (!File.Exists(assemblyPath))
        throw new FileNotFoundException("Consolidated HPD.Gateway.ControlPlane assembly was not found.", assemblyPath);
    using JsonDocument classification = JsonDocument.Parse(File.ReadAllBytes(classificationFile));
    var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["HPD.Gateway.ControlPlane.GatewayManagementBuilder"] =
            "HPD.Gateway.ControlPlane.GatewayControlPlaneBuilder",
        ["HPD.Gateway.ControlPlane.GatewayManagementServiceCollectionExtensions"] =
            "HPD.Gateway.ControlPlane.GatewayControlPlaneServiceCollectionExtensions",
        ["HPD.Gateway.ControlPlane.GatewayAdminEndpointOptions"] =
            "HPD.Gateway.ControlPlane.GatewayAdminApiOptions",
        ["HPD.Gateway.ControlPlane.GatewayAdminEndpointRouteBuilderExtensions"] =
            "HPD.Gateway.ControlPlane.GatewayControlPlaneEndpointRouteBuilderExtensions",
    };
    var removed = new HashSet<string>(StringComparer.Ordinal)
    {
        "HPD.Gateway.ControlPlane.GatewayAdminServiceCollectionExtensions",
        "HPD.Gateway.ControlPlane.GatewayStudioExtensions",
    };
    string[] expected = classification.RootElement.GetProperty("records").EnumerateArray()
        .Where(static record => record.GetProperty("finalProduct").GetString() == "HPD.Gateway.ControlPlane" &&
            record.GetProperty("disposition").GetString() == "ControlPlanePublic")
        .Select(static record => record.GetProperty("finalType").GetString()!)
        .Where(name => !removed.Contains(name))
        .Select(name => replacements.GetValueOrDefault(name, name))
        .Order(StringComparer.Ordinal).ToArray();

    using var stream = File.OpenRead(assemblyPath);
    using var pe = new PEReader(stream);
    MetadataReader metadata = pe.GetMetadataReader();
    string[] actual = metadata.TypeDefinitions
        .Where(handle => IsEffectivelyPublic(metadata, handle))
        .Select(handle => GetQualifiedTypeName(metadata, handle))
        .Where(static name => name != "<Module>")
        .Order(StringComparer.Ordinal).ToArray();
    string[] missing = expected.Except(actual, StringComparer.Ordinal).ToArray();
    string[] unexpected = actual.Except(expected, StringComparer.Ordinal).ToArray();
    if (missing.Length != 0 || unexpected.Length != 0)
        throw new InvalidOperationException(
            $"Consolidated control-plane public surface differs from the Slice 3 contract. " +
            $"Missing: [{string.Join(", ", missing)}]. Unexpected: [{string.Join(", ", unexpected)}].");
    if (actual.Any(static name => !name.StartsWith("HPD.Gateway.ControlPlane.", StringComparison.Ordinal)))
        throw new InvalidOperationException("Consolidated control plane contains a foreign public namespace.");
    Console.WriteLine($"Consolidated HPD.Gateway.ControlPlane surface matches all {actual.Length} clean-break contracts.");
}

static bool IsEffectivelyPublic(MetadataReader metadata, TypeDefinitionHandle handle)
{
    TypeDefinition definition = metadata.GetTypeDefinition(handle);
    if (!IsPublic(definition.Attributes)) return false;
    TypeDefinitionHandle declaring = definition.GetDeclaringType();
    return declaring.IsNil || IsEffectivelyPublic(metadata, declaring);
}

static string GetQualifiedTypeName(MetadataReader metadata, TypeDefinitionHandle handle)
{
    string typeNamespace = GetEffectiveNamespace(metadata, handle);
    string typePath = GetTypePath(metadata, handle);
    return string.IsNullOrEmpty(typeNamespace) ? typePath : typeNamespace + "." + typePath;
}

static bool IsPublic(TypeAttributes attributes)
{
    TypeAttributes visibility = attributes & TypeAttributes.VisibilityMask;
    return visibility is TypeAttributes.Public or TypeAttributes.NestedPublic;
}

static string GetEffectiveNamespace(MetadataReader metadata, TypeDefinitionHandle handle)
{
    TypeDefinition definition = metadata.GetTypeDefinition(handle);
    string value = metadata.GetString(definition.Namespace);
    TypeDefinitionHandle declaring = definition.GetDeclaringType();
    return string.IsNullOrEmpty(value) && !declaring.IsNil
        ? GetEffectiveNamespace(metadata, declaring)
        : value;
}

static string GetTypePath(MetadataReader metadata, TypeDefinitionHandle handle)
{
    TypeDefinition definition = metadata.GetTypeDefinition(handle);
    string name = metadata.GetString(definition.Name);
    TypeDefinitionHandle declaring = definition.GetDeclaringType();
    return declaring.IsNil ? name : GetTypePath(metadata, declaring) + "+" + name;
}

static void ValidateClassification(
    string classificationFile,
    string assemblyDirectory,
    IReadOnlyList<LedgerRecord> inventory,
    IEnumerable<string> gatewayAssemblies,
    IReadOnlyDictionary<string, ProductManifest>? products)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(classificationFile));
    JsonElement root = document.RootElement;
    if (root.GetProperty("classificationVersion").GetString() != "hpd-gateway-public-type-classification/v1")
        throw new InvalidOperationException("Unsupported public-type classification version.");
    if (root.GetProperty("recordCount").GetInt32() != inventory.Count)
        throw new InvalidOperationException("Classification recordCount does not match the current inventory.");

    var classifications = new Dictionary<string, ClassifiedType>(StringComparer.Ordinal);
    var publicContracts = new HashSet<string>(StringComparer.Ordinal)
    {
        "Decision0001CandidateAdmissionAndDecision0011Composition",
        "Decision0001DeclarationContract",
        "Decision0004InspectionExtensionContract",
        "Decision0005ResilienceProfiles",
        "Decision0007OutputCacheProfiles",
        "Decision0008HostingContract",
        "Decision0009StatusContract",
        "Decision0010AppliedAndEffectiveTruth",
        "Decision0011CompositionAndActivation",
        "Decision0011PublicationOutcomeContract",
        "Decision0012ProgrammaticControlPlane",
        "Decision0013AdminHostExtensionContract",
        "Decision0013OptionalHpdAuthAdapter",
        "Decision0014StudioHostExtensionContract",
        "Decision0015MicrosoftDiscoveryProfile"
    };
    var publicAotConsequences = new HashSet<string>(StringComparer.Ordinal)
    {
        "GatewayEffectiveJsonSerializerContext",
        "GatewayJsonSerializerContext",
        "GatewayManagementJsonContextWhenSerialized",
        "GatewayStatusJsonContext",
        "NoGeneratedSerializationRoot"
    };
    foreach (JsonElement element in root.GetProperty("records").EnumerateArray())
    {
        RequireExactMembers(element,
        [
            "currentAssembly", "currentNamespace", "currentType", "disposition",
            "finalProduct", "finalNamespace", "finalType", "finalAccessibility",
            "consumerOrContract", "nativeAotConsequence"
        ]);
        string currentType = RequiredString(element, "currentType");
        var classified = new ClassifiedType(
            RequiredString(element, "currentAssembly"),
            RequiredString(element, "currentNamespace"),
            currentType,
            RequiredString(element, "disposition"),
            RequiredString(element, "finalProduct"),
            RequiredString(element, "finalNamespace"),
            RequiredString(element, "finalType"),
            RequiredString(element, "finalAccessibility"),
            RequiredString(element, "consumerOrContract"),
            RequiredString(element, "nativeAotConsequence"));
        if (!classifications.TryAdd(currentType, classified))
            throw new InvalidOperationException($"Duplicate classification for '{currentType}'.");
        if (classified.Accessibility is not ("Public" or "Internal"))
            throw new InvalidOperationException($"Invalid final accessibility for '{currentType}'.");
        if (classified.ConsumerOrContract is "Pending" or "")
            throw new InvalidOperationException($"Missing contract ownership for '{currentType}'.");
        if (classified.NativeAotConsequence is "Pending" or "")
            throw new InvalidOperationException($"Missing Native AOT consequence for '{currentType}'.");
        if (classified.Accessibility == "Public" &&
            (!publicContracts.Contains(classified.ConsumerOrContract) ||
             !publicAotConsequences.Contains(classified.NativeAotConsequence)))
            throw new InvalidOperationException($"Public classification authority is not closed for '{currentType}'.");
        if (classified.Accessibility == "Internal" &&
            (classified.ConsumerOrContract != "NoExternalConsumerImplementationOnly" ||
             classified.NativeAotConsequence is not ("NoPublicRoot" or "RetainInternalGeneratedContext")))
            throw new InvalidOperationException($"Internal classification authority is not closed for '{currentType}'.");
    }

    string[] inventoryNames = inventory.Select(static record => record.CurrentType).Order(StringComparer.Ordinal).ToArray();
    string[] classificationNames = classifications.Keys.Order(StringComparer.Ordinal).ToArray();
    if (!inventoryNames.SequenceEqual(classificationNames, StringComparer.Ordinal))
        throw new InvalidOperationException("Classification does not exactly cover the current public-type inventory.");

    foreach (LedgerRecord record in inventory)
    {
        ClassifiedType classified = classifications[record.CurrentType];
        string expectedDisposition = classified.Accessibility == "Public" ? record.Disposition : "ImplementationInternal";
        if (classified.CurrentAssembly != record.CurrentAssembly ||
            classified.CurrentNamespace != record.CurrentNamespace ||
            classified.CurrentType != record.CurrentType ||
            classified.Disposition != expectedDisposition ||
            classified.FinalProduct != record.FinalProduct ||
            classified.FinalNamespace != record.FinalNamespace ||
            classified.FinalType != record.FinalType)
            throw new InvalidOperationException($"Classification ownership drift for '{record.CurrentType}'.");
        if (products is not null)
        {
            if (!products.TryGetValue(classified.FinalProduct, out ProductManifest? product))
                throw new InvalidOperationException($"Classification for '{record.CurrentType}' names unknown product '{classified.FinalProduct}'.");
            if (classified.FinalNamespace != product.RootNamespace)
                throw new InvalidOperationException($"Classification for '{record.CurrentType}' does not use product '{classified.FinalProduct}' root namespace.");
        }
    }

    string[] duplicateClassifiedNames = classifications.Values
        .GroupBy(static record => record.FinalType, StringComparer.Ordinal)
        .Where(static group => group.Count() > 1)
        .Select(static group => group.Key)
        .Order(StringComparer.Ordinal)
        .ToArray();
    if (duplicateClassifiedNames.Length != 0)
        throw new InvalidOperationException("Duplicate classified final type names: " + string.Join(", ", duplicateClassifiedNames));

    string[] trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
    string[] productAssemblies = Directory.GetFiles(assemblyDirectory, "*.dll", SearchOption.TopDirectoryOnly);
    using var context = new MetadataLoadContext(new PathAssemblyResolver(trustedPlatformAssemblies.Concat(productAssemblies)));
    var errors = new SortedSet<string>(StringComparer.Ordinal);
    var gatewayAssemblySet = gatewayAssemblies.ToHashSet(StringComparer.Ordinal);

    foreach (LedgerRecord record in inventory)
    {
        ClassifiedType classified = classifications[record.CurrentType];
        if (classified.Accessibility != "Public")
            continue;

        Assembly assembly = context.LoadFromAssemblyPath(Path.Combine(assemblyDirectory, record.CurrentAssembly + ".dll"));
        Type type = assembly.GetType(record.CurrentType, throwOnError: true)!;
        CheckTypeSignature(type, record, classifications, gatewayAssemblySet, errors);
    }

    if (errors.Count != 0)
        throw new InvalidOperationException("Public signature closure failed:\n" + string.Join('\n', errors));
}

static void CheckTypeSignature(
    Type type,
    LedgerRecord record,
    IReadOnlyDictionary<string, ClassifiedType> classifications,
    IReadOnlySet<string> gatewayAssemblySet,
    ISet<string> errors)
{
    CheckReferencedType(type.BaseType, record, "base type", classifications, gatewayAssemblySet, errors);
    foreach (Type contract in type.GetInterfaces())
        CheckReferencedType(contract, record, "implemented interface", classifications, gatewayAssemblySet, errors);
    foreach (Type argument in type.GetGenericArguments().Where(static argument => argument.IsGenericParameter))
        foreach (Type constraint in argument.GetGenericParameterConstraints())
            CheckReferencedType(constraint, record, $"type generic parameter '{argument.Name}' constraint", classifications, gatewayAssemblySet, errors);

    const BindingFlags declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static |
                                  BindingFlags.Public | BindingFlags.NonPublic;
    foreach (ConstructorInfo constructor in type.GetConstructors(declared).Where(IsVisible))
        foreach (ParameterInfo parameter in constructor.GetParameters())
            CheckReferencedType(parameter.ParameterType, record, $"constructor parameter '{parameter.Name}'", classifications, gatewayAssemblySet, errors);
    foreach (MethodInfo method in type.GetMethods(declared).Where(IsVisible))
    {
        CheckReferencedType(method.ReturnType, record, $"method '{method.Name}' return", classifications, gatewayAssemblySet, errors);
        foreach (ParameterInfo parameter in method.GetParameters())
            CheckReferencedType(parameter.ParameterType, record, $"method '{method.Name}' parameter '{parameter.Name}'", classifications, gatewayAssemblySet, errors);
        foreach (Type argument in method.GetGenericArguments())
            foreach (Type constraint in argument.GetGenericParameterConstraints())
                CheckReferencedType(constraint, record, $"method '{method.Name}' generic constraint", classifications, gatewayAssemblySet, errors);
    }
    foreach (FieldInfo field in type.GetFields(declared).Where(static field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly))
        CheckReferencedType(field.FieldType, record, $"field '{field.Name}'", classifications, gatewayAssemblySet, errors);
    foreach (PropertyInfo property in type.GetProperties(declared).Where(static property =>
                 property.GetAccessors(nonPublic: true).Any(IsVisible)))
    {
        CheckReferencedType(property.PropertyType, record, $"property '{property.Name}'", classifications, gatewayAssemblySet, errors);
        foreach (ParameterInfo parameter in property.GetIndexParameters())
            CheckReferencedType(parameter.ParameterType, record, $"property '{property.Name}' index parameter '{parameter.Name}'", classifications, gatewayAssemblySet, errors);
    }
    foreach (EventInfo eventInfo in type.GetEvents(declared).Where(static eventInfo =>
                 eventInfo.AddMethod is not null && IsVisible(eventInfo.AddMethod)))
        CheckReferencedType(eventInfo.EventHandlerType, record, $"event '{eventInfo.Name}'", classifications, gatewayAssemblySet, errors);
}

static bool IsVisible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

static void CheckReferencedType(
    Type? type,
    LedgerRecord owner,
    string location,
    IReadOnlyDictionary<string, ClassifiedType> classifications,
    IReadOnlySet<string> gatewayAssemblies,
    ISet<string> errors)
{
    if (type is null || type == typeof(void) || type.IsGenericParameter)
        return;
    if (type.HasElementType)
    {
        CheckReferencedType(type.GetElementType(), owner, location, classifications, gatewayAssemblies, errors);
        return;
    }
    if (type.IsConstructedGenericType)
    {
        CheckReferencedType(type.GetGenericTypeDefinition(), owner, location, classifications, gatewayAssemblies, errors);
        foreach (Type argument in type.GetGenericArguments())
            CheckReferencedType(argument, owner, location, classifications, gatewayAssemblies, errors);
        return;
    }

    string? assemblyName = type.Assembly.GetName().Name;
    if (assemblyName is null || !gatewayAssemblies.Contains(assemblyName))
        return;
    string identity = type.FullName ?? type.Name;
    if (!classifications.TryGetValue(identity, out ClassifiedType? referenced) || referenced.Accessibility != "Public")
        errors.Add($"{owner.CurrentType} exposes non-public Gateway type {identity} through {location}.");
}

static string RequiredString(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.String)
        throw new InvalidOperationException($"Classification member '{propertyName}' is required.");
    return property.GetString()!;
}

static void RequireExactMembers(JsonElement element, string[] expected)
{
    string[] actual = element.EnumerateObject().Select(static property => property.Name).Order(StringComparer.Ordinal).ToArray();
    string[] orderedExpected = expected.Order(StringComparer.Ordinal).ToArray();
    if (!actual.SequenceEqual(orderedExpected, StringComparer.Ordinal))
        throw new InvalidOperationException("Classification record members do not match the closed v1 schema.");
}

static void RunSignatureClosureSelfTests()
{
    string assemblyName = typeof(LedgerSignatureInternal).Assembly.GetName().Name!;
    var classifications = new Dictionary<string, ClassifiedType>(StringComparer.Ordinal)
    {
        [typeof(LedgerSignatureInternal).FullName!] = new(
            assemblyName, nameof(HPD) + ".Gateway.PublicApiLedger.Tests",
            typeof(LedgerSignatureInternal).FullName!, "ImplementationInternal", "HPD.Gateway",
            "HPD.Gateway", "HPD.Gateway.LedgerSignatureInternal", "Internal", "self-test", "self-test")
    };
    var assemblies = new HashSet<string>(StringComparer.Ordinal) { assemblyName };
    var genericErrors = new SortedSet<string>(StringComparer.Ordinal);
    var indexerErrors = new SortedSet<string>(StringComparer.Ordinal);
    var owner = new LedgerRecord(
        assemblyName, "HPD.Gateway.PublicApiLedger.Tests", "self-test", "RootPublic",
        "HPD.Gateway", "HPD.Gateway", "HPD.Gateway.SelfTest", "Public", "self-test", "self-test");
    CheckTypeSignature(typeof(LedgerGenericConstraintLeak<>), owner, classifications, assemblies, genericErrors);
    CheckTypeSignature(typeof(LedgerIndexerLeak), owner, classifications, assemblies, indexerErrors);
    if (!genericErrors.Any(static error => error.Contains("type generic parameter", StringComparison.Ordinal)) ||
        !indexerErrors.Any(static error => error.Contains("index parameter", StringComparison.Ordinal)))
        throw new InvalidOperationException("Signature-closure adversarial self-tests failed.");
}

static IReadOnlyDictionary<string, ProductManifest> ValidateProductManifests(string manifestFile, IEnumerable<string> currentAssemblies)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifestFile));
    JsonElement root = document.RootElement;
    if (root.GetProperty("manifestVersion").GetString() != "hpd-gateway-product-manifests/v1")
        throw new InvalidOperationException("Unsupported product-manifest version.");

    var products = new Dictionary<string, ProductManifest>(StringComparer.Ordinal);
    var packageIds = new HashSet<string>(StringComparer.Ordinal);
    var namespaces = new HashSet<string>(StringComparer.Ordinal);
    var absorbed = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (JsonElement element in root.GetProperty("products").EnumerateArray())
    {
        string project = RequiredString(element, "project");
        string rootNamespace = RequiredString(element, "rootNamespace");
        bool packable = element.GetProperty("packable").GetBoolean();
        string? packageId = element.GetProperty("packageId").ValueKind == JsonValueKind.Null
            ? null
            : element.GetProperty("packageId").GetString();
        string[] dependencies = element.GetProperty("productDependencies").EnumerateArray()
            .Select(static item => item.GetString() ?? throw new InvalidOperationException("Null product dependency."))
            .ToArray();
        string[] absorbedProjects = element.GetProperty("absorbs").EnumerateArray()
            .Select(static item => item.GetString() ?? throw new InvalidOperationException("Null absorbed project."))
            .ToArray();
        var product = new ProductManifest(project, packageId, rootNamespace, packable, dependencies, absorbedProjects);
        if (!products.TryAdd(project, product))
            throw new InvalidOperationException($"Duplicate product project '{project}'.");
        if (!namespaces.Add(rootNamespace))
            throw new InvalidOperationException($"Duplicate product root namespace '{rootNamespace}'.");
        if (packable != (packageId is not null))
            throw new InvalidOperationException($"Product '{project}' has inconsistent packability and package ID.");
        if (packageId is not null && !packageIds.Add(packageId))
            throw new InvalidOperationException($"Duplicate package ID '{packageId}'.");
        foreach (string current in absorbedProjects)
            if (!absorbed.TryAdd(current, project))
                throw new InvalidOperationException($"Current project '{current}' is absorbed by multiple products.");
    }

    var expectedProducts = new Dictionary<string, ExpectedProduct>(StringComparer.Ordinal)
    {
        ["HPD.Gateway"] = new("HPD.Gateway", "HPD.Gateway", true, []),
        ["HPD.Gateway.ControlPlane"] = new("HPD.Gateway.ControlPlane", "HPD.Gateway.ControlPlane", true, ["HPD.Gateway"]),
        ["HPD.Gateway.ControlPlane.Sqlite"] = new("HPD.Gateway.ControlPlane.Sqlite", "HPD.Gateway.ControlPlane.Sqlite", true, ["HPD.Gateway.ControlPlane"]),
        ["HPD.Gateway.ControlPlane.HPDAuth"] = new("HPD.Gateway.ControlPlane.HPDAuth", "HPD.Gateway.ControlPlane.HPDAuth", true, ["HPD.Gateway.ControlPlane"]),
        ["HPD.Gateway.Discovery.Microsoft"] = new("HPD.Gateway.Discovery.Microsoft", "HPD.Gateway.Discovery.Microsoft", true, ["HPD.Gateway"]),
        ["HPD.Gateway.Standalone"] = new(null, "HPD.Gateway.Standalone", false,
        [
            "HPD.Gateway",
            "HPD.Gateway.ControlPlane",
            "HPD.Gateway.ControlPlane.Sqlite",
            "HPD.Gateway.ControlPlane.HPDAuth"
        ])
    };
    if (!products.Keys.Order(StringComparer.Ordinal).SequenceEqual(expectedProducts.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        throw new InvalidOperationException("Product manifest must contain exactly the six Decision 0016 products.");
    foreach ((string project, ExpectedProduct expected) in expectedProducts)
    {
        ProductManifest actual = products[project];
        if (actual.PackageId != expected.PackageId ||
            actual.RootNamespace != expected.RootNamespace ||
            actual.Packable != expected.Packable ||
            !actual.Dependencies.SequenceEqual(expected.Dependencies, StringComparer.Ordinal))
            throw new InvalidOperationException($"Product '{project}' does not match its closed Decision 0016 identity and dependency record.");
    }
    if (packageIds.Count != 5)
        throw new InvalidOperationException("Exactly five library products must be packable.");

    foreach (ProductManifest product in products.Values)
        foreach (string dependency in product.Dependencies)
            if (!products.ContainsKey(dependency))
                throw new InvalidOperationException($"Product '{product.Project}' has unknown dependency '{dependency}'.");
    foreach (string project in products.Keys)
        VisitProduct(project, products, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));

    string[] expectedAbsorbed = currentAssemblies
        .Where(static assembly => assembly is not "HPD.Gateway" and not "HPD.Gateway.Discovery.Microsoft")
        .Order(StringComparer.Ordinal)
        .ToArray();
    string[] actualAbsorbed = absorbed.Keys.Order(StringComparer.Ordinal).ToArray();
    if (!expectedAbsorbed.SequenceEqual(actualAbsorbed, StringComparer.Ordinal))
        throw new InvalidOperationException("Product manifests do not exactly own every replaced packable project.");

    return products;
}

static void VisitProduct(
    string project,
    IReadOnlyDictionary<string, ProductManifest> products,
    ISet<string> visiting,
    ISet<string> visited)
{
    if (visited.Contains(project)) return;
    if (!visiting.Add(project))
        throw new InvalidOperationException($"Product dependency cycle includes '{project}'.");
    foreach (string dependency in products[project].Dependencies)
        VisitProduct(dependency, products, visiting, visited);
    visiting.Remove(project);
    visited.Add(project);
}

internal sealed record Ownership(string Disposition, string Product, string Namespace);

internal sealed record LedgerRecord(
    string CurrentAssembly,
    string CurrentNamespace,
    string CurrentType,
    string Disposition,
    string FinalProduct,
    string FinalNamespace,
    string FinalType,
    string FinalAccessibility,
    string ConsumerOrContract,
    string NativeAotConsequence);

internal sealed record LedgerEnvelope(
    string LedgerVersion,
    int RecordCount,
    IReadOnlyList<LedgerRecord> Records);

internal sealed record ClassifiedType(
    string CurrentAssembly,
    string CurrentNamespace,
    string CurrentType,
    string Disposition,
    string FinalProduct,
    string FinalNamespace,
    string FinalType,
    string Accessibility,
    string ConsumerOrContract,
    string NativeAotConsequence);

internal sealed record ProductManifest(
    string Project,
    string? PackageId,
    string RootNamespace,
    bool Packable,
    string[] Dependencies,
    string[] AbsorbedProjects);

internal sealed record ExpectedProduct(
    string? PackageId,
    string RootNamespace,
    bool Packable,
    string[] Dependencies);

[System.Text.Json.Serialization.JsonSerializable(typeof(LedgerEnvelope))]
[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
internal sealed partial class LedgerJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

namespace HPD.Gateway.PublicApiLedger.Tests
{
    public class LedgerSignatureInternal;

    public class LedgerGenericConstraintLeak<T> where T : LedgerSignatureInternal;

    public class LedgerIndexerLeak
    {
        public string this[LedgerSignatureInternal value] => value.ToString()!;
    }
}
