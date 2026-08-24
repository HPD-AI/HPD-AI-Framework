using System.Collections.Immutable;
using System.Text.Json;

namespace HPD.AI.Platform.Studio;

/// <summary>Identifies an authorized Studio transport method.</summary>
public enum BaseStudioTransportMethod : byte { Get = 1, Post, Put, Delete, WebSocket }
/// <summary>Identifies the only endpoint audience admitted to Studio.</summary>
public enum BaseStudioEndpointAudience : byte { ControlPlane = 1 }
/// <summary>Identifies a runtime transport binding.</summary>
public enum BaseStudioTransportKind : byte { SameOriginHttp = 1, SameOriginRealtime }
/// <summary>Identifies a method exposed by the sealed runtime map.</summary>
public enum BaseStudioMethodKind : byte
{ Resolve = 1, Page, Preview, Execute, ReceiptQuery, ReceiptResolve, InvalidationSubscribe, StageCreate, StageUpload, StageFinalize, StageDispose }

/// <summary>Binds one exact named L41 type descriptor without introducing executable code.</summary>
public sealed class BaseStudioNamedTypeContract
{
    private readonly byte[] _canonicalDescriptor;
    private BaseStudioNamedTypeContract(string id, byte[] descriptor, ImmutableArray<string> references, BaseStudioSha256 nodeChecksum, BaseStudioSha256 checksum)
    { TypeId = id; _canonicalDescriptor = descriptor; References = references; NodeChecksum = nodeChecksum; Checksum = checksum; }
    /// <summary>Gets the exact L41 type identity.</summary>
    public string TypeId { get; }
    /// <summary>Gets the exact L41 node checksum.</summary>
    public BaseStudioSha256 NodeChecksum { get; }
    /// <summary>Gets the canonical contract checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Gets exact referenced named-type identities in descriptor order.</summary>
    public ImmutableArray<string> References { get; }
    /// <summary>Returns defensive canonical L41 descriptor bytes.</summary>
    public byte[] GetCanonicalDescriptor() => _canonicalDescriptor.ToArray();
    public static BaseStudioNamedTypeContract Create(string id, ReadOnlySpan<byte> descriptor)
    {
        RequireL41Id(id); if (descriptor.Length is < 1 or > 65_536) throw new ArgumentOutOfRangeException(nameof(descriptor));
        byte[] owned = descriptor.ToArray();
        using JsonDocument document = JsonDocument.Parse(owned, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 32 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Studio L41 descriptor is not a closed object.", nameof(descriptor));
        ImmutableArray<string> references = ValidateNode(document.RootElement);
        BaseStudioSha256 node = BaseStudioSha256.Compute(owned);
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.named-type.v1", writer =>
        { writer.String(id); writer.Bytes(owned); writer.Checksum(node); });
        return new(id, owned, references, node, checksum);
    }

    private static ImmutableArray<string> ValidateNode(JsonElement node)
    {
        if (!node.TryGetProperty("kind", out JsonElement kindElement) || kindElement.ValueKind != JsonValueKind.String)
            throw new ArgumentException("Studio L41 node kind is missing.");
        string kind = kindElement.GetString()!; string[] allowed = kind switch
        {
            "selection-query" => ["kind","maximumNodes","maximumDepth","maximumLiterals","maximumTake"],
            "selection-previous-state" => ["kind","maximumFields"], "selection-identity" or "module-generation" or "boolean" or "decimal" or "redacted" => ["kind"],
            "selection-patch" => ["kind","patchTypeId"], "string" => ["kind","minLength","maxLength","format"],
            "integer" => ["kind","minimum","maximum","wire"], "floating" => ["kind","precision","finiteOnly"],
            "bytes" => ["kind","wire","maxBytes"], "subjectReference" => ["kind","contractId","contractVersion","subjectIdKind","maximumSubjectIdUtf8Bytes","authorityEpochBytes","incarnationBytes"],
            "literal" => ["kind","value"], "enum" => ["kind","values"], "array" => ["kind","elementTypeId","minItems","maxItems"],
            "object" => ["kind","properties","additionalProperties"], "union" => ["kind","discriminator","variants"],
            _ => throw new ArgumentException("Studio L41 node kind is not closed.")
        };
        string[] names = node.EnumerateObject().Select(static property => property.Name).ToArray();
        if (names.Length != allowed.Length || names.Any(name => !allowed.Contains(name, StringComparer.Ordinal)))
            throw new ArgumentException("Studio L41 node members are not exact.");
        switch (kind)
        {
            case "selection-query": Positive("maximumNodes"); Positive("maximumDepth"); Positive("maximumLiterals"); Positive("maximumTake"); break;
            case "selection-previous-state": Positive("maximumFields"); break;
            case "selection-patch": Id("patchTypeId"); break;
            case "string": Nonnegative("minLength"); Nonnegative("maxLength"); if (node.GetProperty("minLength").GetInt32() > node.GetProperty("maxLength").GetInt32()) Invalid(); TextProperty("format"); break;
            case "integer": Integer("minimum"); Integer("maximum"); if (System.Numerics.BigInteger.Parse(node.GetProperty("minimum").GetString()!) > System.Numerics.BigInteger.Parse(node.GetProperty("maximum").GetString()!)) Invalid(); OneOf("wire", "number", "decimal-string"); break;
            case "floating": OneOf("precision", "binary32", "binary64"); if (node.GetProperty("finiteOnly").ValueKind != JsonValueKind.True) Invalid(); break;
            case "bytes": OneOf("wire", "base64"); Positive("maxBytes"); break;
            case "subjectReference": Id("contractId"); Positive("contractVersion"); OneOf("subjectIdKind", "ordinalString", "guid", "uint64"); Positive("maximumSubjectIdUtf8Bytes"); ExactInt("authorityEpochBytes", 16); ExactInt("incarnationBytes", 16); break;
            case "literal": if (node.GetProperty("value").ValueKind is not (JsonValueKind.String or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)) Invalid(); break;
            case "enum": CanonicalStrings(node.GetProperty("values"), 1, 256); break;
            case "array": Id("elementTypeId"); Nonnegative("minItems"); Nonnegative("maxItems"); if (node.GetProperty("minItems").GetInt32() > node.GetProperty("maxItems").GetInt32()) Invalid(); break;
            case "object":
                if (node.GetProperty("additionalProperties").ValueKind != JsonValueKind.False) Invalid();
                JsonElement properties = node.GetProperty("properties"); if (properties.ValueKind != JsonValueKind.Array || properties.GetArrayLength() > 256) Invalid();
                string previousName = ""; HashSet<string> wireNames = new(StringComparer.Ordinal);
                foreach (JsonElement property in properties.EnumerateArray())
                { Exact(property, "name","wireName","typeId","required","nullable","disclosureShape"); string name = Text(property,"name");
                  if (StringComparer.Ordinal.Compare(previousName, name) >= 0 || !wireNames.Add(Text(property,"wireName"))) Invalid(); previousName = name;
                  RequireL41Id(property.GetProperty("typeId").GetString()!); Boolean(property,"required"); Boolean(property,"nullable");
                  BaseStudioNamedTypeContract.OneOf(property,"disclosureShape","none","omission","fixed-marker"); }
                break;
            case "union":
                TextProperty("discriminator"); JsonElement variants = node.GetProperty("variants"); if (variants.ValueKind != JsonValueKind.Array || variants.GetArrayLength() is < 1 or > 64) Invalid();
                string previousTag = ""; foreach (JsonElement variant in variants.EnumerateArray())
                { Exact(variant,"tag","typeId"); string tag = Text(variant,"tag"); if (StringComparer.Ordinal.Compare(previousTag, tag) >= 0) Invalid(); previousTag = tag; RequireL41Id(variant.GetProperty("typeId").GetString()!); }
                break;
        }
        IEnumerable<string> refs = kind switch
        {
            "selection-patch" => [node.GetProperty("patchTypeId").GetString()!],
            "array" => [node.GetProperty("elementTypeId").GetString()!],
            "object" => node.GetProperty("properties").EnumerateArray().Select(property => property.GetProperty("typeId").GetString()!),
            "union" => node.GetProperty("variants").EnumerateArray().Select(variant => variant.GetProperty("typeId").GetString()!),
            _ => []
        };
        ImmutableArray<string> result = refs.ToImmutableArray(); foreach (string reference in result) RequireL41Id(reference); return result;

        void Positive(string name) { if (!node.TryGetProperty(name, out JsonElement value) || !value.TryGetInt32(out int number) || number < 1) Invalid(); }
        void Nonnegative(string name) { if (!node.TryGetProperty(name, out JsonElement value) || !value.TryGetInt32(out int number) || number < 0) Invalid(); }
        void ExactInt(string name, int expected) { if (!node.TryGetProperty(name, out JsonElement value) || !value.TryGetInt32(out int number) || number != expected) Invalid(); }
        void Id(string name) { RequireL41Id(node.GetProperty(name).GetString()!); }
        void TextProperty(string name) { _ = BaseStudioNamedTypeContract.Text(node, name); }
        void Integer(string name) { string value = Text(node, name); if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^-?(?:0|[1-9][0-9]*)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) || value == "-0") Invalid(); }
        void OneOf(string name, params string[] values) => BaseStudioNamedTypeContract.OneOf(node, name, values);
    }

    private static void Exact(JsonElement value, params string[] expected)
    { if (value.ValueKind != JsonValueKind.Object) Invalid(); string[] names = value.EnumerateObject().Select(static item => item.Name).ToArray(); if (names.Length != expected.Length || names.Any(name => !expected.Contains(name, StringComparer.Ordinal))) Invalid(); }
    private static string Text(JsonElement value, string name)
    { if (!value.TryGetProperty(name, out JsonElement item) || item.ValueKind != JsonValueKind.String) return Invalid<string>(); string result = item.GetString()!; if (result.Length == 0 || System.Text.Encoding.UTF8.GetByteCount(result) > 128 || result.Any(static character => character <= '\u001f' || character == '\u007f')) return Invalid<string>(); return result; }
    private static void Boolean(JsonElement value, string name)
    { if (!value.TryGetProperty(name, out JsonElement item) || item.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Invalid(); }
    private static void OneOf(JsonElement value, string name, params string[] accepted)
    { string item = Text(value, name); if (!accepted.Contains(item, StringComparer.Ordinal)) Invalid(); }
    private static void CanonicalStrings(JsonElement value, int minimum, int maximum)
    { if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < minimum || value.GetArrayLength() > maximum) Invalid(); string previous = ""; foreach (JsonElement item in value.EnumerateArray()) { if (item.ValueKind != JsonValueKind.String) Invalid(); string current = item.GetString()!; if (current.Length == 0 || System.Text.Encoding.UTF8.GetByteCount(current) > 256 || current.Any(static character => character <= '\u001f' || character == '\u007f') || StringComparer.Ordinal.Compare(previous, current) >= 0) Invalid(); previous = current; } }
    internal static void RequireL41Id(string value)
    {
        if (string.IsNullOrEmpty(value) || System.Text.Encoding.UTF8.GetByteCount(value) > 128 || value[0] is < 'a' or > 'z') Invalid();
        bool separator = false;
        foreach (char character in value)
        {
            bool alpha = character is >= 'a' and <= 'z'; bool digit = character is >= '0' and <= '9'; bool currentSeparator = character is '.' or '-';
            if (!alpha && !digit && !currentSeparator || currentSeparator && separator) Invalid(); separator = currentSeparator;
        }
        if (separator) Invalid();
    }
    private static void Invalid() => throw new ArgumentException("Studio L41 node semantics are invalid.");
    private static T Invalid<T>() => throw new ArgumentException("Studio L41 node semantics are invalid.");
}

/// <summary>Binds one registered Studio method to one exact endpoint and L41 request/result pair.</summary>
public sealed class BaseStudioMethodBinding
{
    private BaseStudioMethodBinding(string id, BaseStudioMethodKind kind, string module, string owner, string endpoint,
        string request, string result, BaseStudioSha256 checksum)
    { RegisteredMethodId = id; Kind = kind; OwningModuleId = module; OwningPageOrCommandId = owner; EndpointId = endpoint;
      RequestTypeId = request; ResultTypeId = result; BindingChecksum = checksum; }
    /// <summary>Gets the registered method identity.</summary>
    public string RegisteredMethodId { get; }
    /// <summary>Gets the closed method kind.</summary>
    public BaseStudioMethodKind Kind { get; }
    /// <summary>Gets the owning module identity.</summary>
    public string OwningModuleId { get; }
    /// <summary>Gets the owning disclosed page or command identity.</summary>
    public string OwningPageOrCommandId { get; }
    /// <summary>Gets the endpoint identity.</summary>
    public string EndpointId { get; }
    /// <summary>Gets the request type identity.</summary>
    public string RequestTypeId { get; }
    /// <summary>Gets the result type identity.</summary>
    public string ResultTypeId { get; }
    /// <summary>Gets the canonical binding checksum.</summary>
    public BaseStudioSha256 BindingChecksum { get; }
    public static BaseStudioMethodBinding Create(string id, BaseStudioMethodKind kind, string module, string owner,
        string endpoint, string request, string result)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Enum(kind); StudioContractValidation.Id(module);
        StudioContractValidation.Id(owner); StudioContractValidation.Id(endpoint); BaseStudioNamedTypeContract.RequireL41Id(request); BaseStudioNamedTypeContract.RequireL41Id(result);
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.method-binding.v1", writer =>
        { writer.String(id); writer.Enum(kind); writer.String(module); writer.String(owner); writer.String(endpoint); writer.String(request); writer.String(result); });
        return new(id, kind, module, owner, endpoint, request, result, checksum);
    }
}

/// <summary>Defines one exact L41 endpoint binding in the runtime Studio client map.</summary>
public sealed class BaseStudioEndpointContract
{
    private BaseStudioEndpointContract(string id, int version, BaseStudioTransportMethod method, string route,
        BaseStudioEndpointAudience audience, BaseStudioTransportKind transport, string requestNode, BaseStudioSha256 requestChecksum, string resultNode, BaseStudioSha256 resultChecksum,
        string errorNode, BaseStudioSha256 errorChecksum, long requestBytes, long resultBytes, TimeSpan deadline, BaseStudioSha256 checksum)
    { EndpointId = id; Version = version; Method = method; RelativeRoute = route; Audience = audience; Transport = transport; RequestNodeId = requestNode;
      RequestNodeChecksum = requestChecksum; ResultNodeId = resultNode; ResultNodeChecksum = resultChecksum;
      ErrorNodeId = errorNode; ErrorNodeChecksum = errorChecksum; MaximumRequestBytes = requestBytes;
      MaximumResultBytes = resultBytes; Deadline = deadline; Checksum = checksum; }
    /// <summary>Gets the endpoint identity.</summary>
    public string EndpointId { get; }
    /// <summary>Gets the endpoint version.</summary>
    public int Version { get; }
    /// <summary>Gets the transport method.</summary>
    public BaseStudioTransportMethod Method { get; }
    /// <summary>Gets the registered relative route.</summary>
    public string RelativeRoute { get; }
    /// <summary>Gets the fixed ControlPlane audience.</summary>
    public BaseStudioEndpointAudience Audience { get; }
    /// <summary>Gets the transport kind.</summary>
    public BaseStudioTransportKind Transport { get; }
    /// <summary>Gets the request-node identity.</summary>
    public string RequestNodeId { get; }
    /// <summary>Gets the request-node checksum.</summary>
    public BaseStudioSha256 RequestNodeChecksum { get; }
    /// <summary>Gets the result-node identity.</summary>
    public string ResultNodeId { get; }
    /// <summary>Gets the result-node checksum.</summary>
    public BaseStudioSha256 ResultNodeChecksum { get; }
    /// <summary>Gets the safe error-node identity.</summary>
    public string ErrorNodeId { get; }
    /// <summary>Gets the safe error-node checksum.</summary>
    public BaseStudioSha256 ErrorNodeChecksum { get; }
    /// <summary>Gets maximum request bytes.</summary>
    public long MaximumRequestBytes { get; }
    /// <summary>Gets maximum result bytes.</summary>
    public long MaximumResultBytes { get; }
    /// <summary>Gets the operation deadline.</summary>
    public TimeSpan Deadline { get; }
    /// <summary>Gets the canonical endpoint checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates and checksums one endpoint contract.</summary>
    public static BaseStudioEndpointContract Create(string id, int version, BaseStudioTransportMethod method, string route,
        BaseStudioEndpointAudience audience, BaseStudioTransportKind transport,
        string requestNode, BaseStudioSha256 requestChecksum, string resultNode, BaseStudioSha256 resultChecksum,
        string errorNode, BaseStudioSha256 errorChecksum, long requestBytes, long resultBytes, TimeSpan deadline)
    {
        StudioContractValidation.Id(id); BaseStudioNamedTypeContract.RequireL41Id(requestNode); BaseStudioNamedTypeContract.RequireL41Id(resultNode);
        BaseStudioNamedTypeContract.RequireL41Id(errorNode); StudioContractValidation.Enum(method); StudioContractValidation.Enum(audience); StudioContractValidation.Enum(transport);
        if (version < 1 || requestBytes < 1 || resultBytes < 1 || deadline <= TimeSpan.Zero ||
            deadline > TimeSpan.FromMinutes(5) || deadline.Ticks % TimeSpan.TicksPerMillisecond != 0 ||
            string.IsNullOrWhiteSpace(route) || route.Length > 256 || !route.StartsWith("/", StringComparison.Ordinal) ||
            route.Contains("//", StringComparison.Ordinal) || route.Contains('?') || route.Contains('#'))
            throw new ArgumentException("Studio endpoint contract is invalid.");
        BaseStudioSha256 rq = BaseStudioSha256.FromBytes(requestChecksum.ToArray());
        BaseStudioSha256 rs = BaseStudioSha256.FromBytes(resultChecksum.ToArray());
        BaseStudioSha256 er = BaseStudioSha256.FromBytes(errorChecksum.ToArray());
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.endpoint-contract.v1", writer =>
        { writer.String(id); writer.Int32(version); writer.Enum(method); writer.String(route); writer.Enum(audience); writer.Enum(transport); writer.String(requestNode); writer.Checksum(rq);
          writer.String(resultNode); writer.Checksum(rs); writer.String(errorNode); writer.Checksum(er); writer.Int64(requestBytes);
          writer.Int64(resultBytes); writer.Int64(checked((long)deadline.TotalMilliseconds)); });
        return new(id, version, method, route, audience, transport, requestNode, rq, resultNode, rs, errorNode, er, requestBytes, resultBytes, deadline, checksum);
    }
}

/// <summary>Contains the principal-filtered canonical L41 runtime graph and endpoints.</summary>
public sealed class BaseStudioContractMap
{
    internal BaseStudioContractMap(string protocol, string serialization, string errors, string realtime,
        BaseStudioSha256 runtimeAbi, BaseStudioSha256 vectors, ImmutableArray<BaseStudioNamedTypeContract> types,
        ImmutableArray<BaseStudioEndpointContract> endpoints, ImmutableArray<BaseStudioMethodBinding> methods, BaseStudioSha256 checksum)
    { ProtocolVersion = protocol; SerializationProfile = serialization; ErrorTaxonomy = errors; RealtimeProtocol = realtime;
      RuntimeAbiChecksum = runtimeAbi; InterpreterVectorChecksum = vectors;
      Types = types; Endpoints = endpoints; Methods = methods; Checksum = checksum; }
    /// <summary>Gets the L41 protocol version.</summary>
    public string ProtocolVersion { get; }
    /// <summary>Gets the serialization profile.</summary>
    public string SerializationProfile { get; }
    /// <summary>Gets the stable error taxonomy identity.</summary>
    public string ErrorTaxonomy { get; }
    /// <summary>Gets the realtime protocol identity.</summary>
    public string RealtimeProtocol { get; }
    /// <summary>Gets the pinned browser runtime ABI checksum.</summary>
    public BaseStudioSha256 RuntimeAbiChecksum { get; }
    /// <summary>Gets the interpreter vector checksum.</summary>
    public BaseStudioSha256 InterpreterVectorChecksum { get; }
    /// <summary>Gets reachable named L41 types in identity order.</summary>
    public ImmutableArray<BaseStudioNamedTypeContract> Types { get; }
    /// <summary>Gets endpoint contracts in identity/version order.</summary>
    public ImmutableArray<BaseStudioEndpointContract> Endpoints { get; }
    /// <summary>Gets disclosed method bindings in registered identity order.</summary>
    public ImmutableArray<BaseStudioMethodBinding> Methods { get; }
    /// <summary>Gets the map checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Creates and checksums one principal-filtered runtime contract map.</summary>
    public static BaseStudioContractMap Create(string protocol, string serialization, string errors, string realtime,
        BaseStudioSha256 runtimeAbi, BaseStudioSha256 vectors,
        IEnumerable<BaseStudioNamedTypeContract> types, IEnumerable<BaseStudioEndpointContract> endpoints,
        IEnumerable<BaseStudioMethodBinding> methods, IReadOnlySet<(string ModuleId, string OwnerId)> disclosedOwners)
    {
        StudioContractValidation.Id(protocol); StudioContractValidation.Id(serialization); StudioContractValidation.Id(errors); StudioContractValidation.Id(realtime);
        ImmutableArray<BaseStudioNamedTypeContract> ownedTypes = StudioGraphValidation.OrderedIdentity(types, 2_048, static value => value.TypeId, nameof(types));
        ImmutableArray<BaseStudioEndpointContract> ownedEndpoints = StudioGraphValidation.Ordered(endpoints, 512,
            static value => (value.EndpointId, value.Version), nameof(endpoints), true);
        ImmutableArray<BaseStudioMethodBinding> ownedMethods = StudioGraphValidation.OrderedIdentity(methods, 1_024, static value => value.RegisteredMethodId, nameof(methods));
        HashSet<string> typeIds = ownedTypes.Select(static value => value.TypeId).ToHashSet(StringComparer.Ordinal);
        HashSet<string> endpointIds = ownedEndpoints.Select(static value => value.EndpointId).ToHashSet(StringComparer.Ordinal);
        if (ownedTypes.Any(value => value.References.Any(reference => !typeIds.Contains(reference))) ||
            ownedEndpoints.Any(value => !typeIds.Contains(value.RequestNodeId) || !typeIds.Contains(value.ResultNodeId) || !typeIds.Contains(value.ErrorNodeId) ||
                !BaseStudioSha256.FixedTimeEquals(value.RequestNodeChecksum, ownedTypes.Single(type => type.TypeId == value.RequestNodeId).NodeChecksum) ||
                !BaseStudioSha256.FixedTimeEquals(value.ResultNodeChecksum, ownedTypes.Single(type => type.TypeId == value.ResultNodeId).NodeChecksum) ||
                !BaseStudioSha256.FixedTimeEquals(value.ErrorNodeChecksum, ownedTypes.Single(type => type.TypeId == value.ErrorNodeId).NodeChecksum)) ||
            ownedMethods.Any(value => !endpointIds.Contains(value.EndpointId) || !typeIds.Contains(value.RequestTypeId) || !typeIds.Contains(value.ResultTypeId) ||
                !disclosedOwners.Contains((value.OwningModuleId, value.OwningPageOrCommandId)) || !MethodMatches(value)))
            throw new ArgumentException("Studio contract map contains a dangling or undisclosed binding.");
        HashSet<string> reachable = []; HashSet<string> visiting = []; foreach (BaseStudioEndpointContract endpoint in ownedEndpoints)
        { Visit(endpoint.RequestNodeId, 1); Visit(endpoint.ResultNodeId, 1); Visit(endpoint.ErrorNodeId, 1); }
        if (reachable.Count != ownedTypes.Length) throw new ArgumentException("Studio contract map contains unreachable types.", nameof(types));
        void Visit(string id, int depth)
        { if (depth > 32) throw new ArgumentException("Studio L41 graph exceeds maximum depth.", nameof(types)); if (reachable.Contains(id)) return;
          if (!visiting.Add(id)) throw new ArgumentException("Studio L41 graph is recursive.", nameof(types));
          foreach (string reference in ownedTypes.Single(type => type.TypeId == id).References) Visit(reference, depth + 1);
          visiting.Remove(id); reachable.Add(id); }
        bool MethodMatches(BaseStudioMethodBinding binding)
        { BaseStudioEndpointContract endpoint = ownedEndpoints.Single(value => value.EndpointId == binding.EndpointId);
          if (!StringComparer.Ordinal.Equals(binding.RequestTypeId, endpoint.RequestNodeId) || !StringComparer.Ordinal.Equals(binding.ResultTypeId, endpoint.ResultNodeId)) return false;
          return binding.Kind switch { BaseStudioMethodKind.InvalidationSubscribe => endpoint.Transport == BaseStudioTransportKind.SameOriginRealtime && endpoint.Method == BaseStudioTransportMethod.WebSocket,
            BaseStudioMethodKind.StageUpload => endpoint.Transport == BaseStudioTransportKind.SameOriginHttp && endpoint.Method == BaseStudioTransportMethod.Put,
            _ => endpoint.Transport == BaseStudioTransportKind.SameOriginHttp && endpoint.Method != BaseStudioTransportMethod.WebSocket }; }
        BaseStudioSha256 abi = BaseStudioSha256.FromBytes(runtimeAbi.ToArray()); BaseStudioSha256 vector = BaseStudioSha256.FromBytes(vectors.ToArray());
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.contract-map.v1", writer =>
        { writer.String(protocol); writer.String(serialization); writer.String(errors); writer.String(realtime); writer.Checksum(abi); writer.Checksum(vector);
          StudioGraphValidation.Encode(writer, ownedTypes, static value => value.Checksum);
          StudioGraphValidation.Encode(writer, ownedEndpoints, static value => value.Checksum);
          StudioGraphValidation.Encode(writer, ownedMethods, static value => value.BindingChecksum); });
        return new(protocol, serialization, errors, realtime, abi, vector, ownedTypes, ownedEndpoints, ownedMethods, checksum);
    }
}

/// <summary>Defines the browser shell's bounded retained-state and inventory limits.</summary>
public sealed class BaseStudioShellLimits
{
    internal BaseStudioShellLimits(int modules, int pages, int commands, int resolvers, int clients,
        long bootstrapBytes, long retainedBytes, TimeSpan bootstrapDeadline, BaseStudioSha256 checksum)
    { MaximumModules = modules; MaximumPages = pages; MaximumCommands = commands; MaximumResolvers = resolvers;
      MaximumClients = clients; MaximumBootstrapBytes = bootstrapBytes; MaximumRetainedBytes = retainedBytes;
      BootstrapDeadline = bootstrapDeadline; Checksum = checksum; }
    /// <summary>Gets maximum disclosed modules.</summary>
    public int MaximumModules { get; }
    /// <summary>Gets maximum disclosed pages.</summary>
    public int MaximumPages { get; }
    /// <summary>Gets maximum disclosed commands.</summary>
    public int MaximumCommands { get; }
    /// <summary>Gets maximum disclosed resolvers.</summary>
    public int MaximumResolvers { get; }
    /// <summary>Gets maximum disclosed clients.</summary>
    public int MaximumClients { get; }
    /// <summary>Gets maximum bootstrap bytes.</summary>
    public long MaximumBootstrapBytes { get; }
    /// <summary>Gets maximum retained protected bytes.</summary>
    public long MaximumRetainedBytes { get; }
    /// <summary>Gets bootstrap acquisition deadline.</summary>
    public TimeSpan BootstrapDeadline { get; }
    /// <summary>Gets canonical limits checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates and checksums effective shell limits.</summary>
    public static BaseStudioShellLimits Create(int modules, int pages, int commands, int resolvers, int clients,
        long bootstrapBytes, long retainedBytes, TimeSpan bootstrapDeadline)
    {
        if (modules is < 1 or > 64 || pages is < 1 or > 512 || commands is < 0 or > 256 || resolvers is < 0 or > 128 ||
            clients is < 1 or > 32 || bootstrapBytes is < 1 or > 16_777_216 || retainedBytes is < 1 or > 67_108_864 ||
            bootstrapDeadline <= TimeSpan.Zero || bootstrapDeadline > TimeSpan.FromMinutes(1) || bootstrapDeadline.Ticks % TimeSpan.TicksPerMillisecond != 0)
            throw new ArgumentOutOfRangeException(nameof(modules));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.shell-limits.v1", writer =>
        { writer.Int32(modules); writer.Int32(pages); writer.Int32(commands); writer.Int32(resolvers); writer.Int32(clients);
          writer.Int64(bootstrapBytes); writer.Int64(retainedBytes); writer.Int64(checked((long)bootstrapDeadline.TotalMilliseconds)); });
        return new(modules, pages, commands, resolvers, clients, bootstrapBytes, retainedBytes, bootstrapDeadline, checksum);
    }
}
