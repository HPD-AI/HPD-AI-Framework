using System.Collections.Immutable;
using System.Text.Json;

namespace HPD.AI.Platform.Studio;

/// <summary>Contains one bounded canonical L41 JSON document crossing a Studio producer boundary.</summary>
public sealed class BaseStudioCanonicalJson
{
    private readonly byte[] _bytes;
    private BaseStudioCanonicalJson(byte[] bytes) => _bytes = bytes;
    /// <summary>Creates a deeply owned, syntactically closed JSON document.</summary>
    public static BaseStudioCanonicalJson Create(ReadOnlySpan<byte> bytes, int maximumBytes)
    {
        if (maximumBytes < 2 || bytes.Length is < 2 || bytes.Length > maximumBytes) throw new ArgumentOutOfRangeException(nameof(bytes));
        byte[] owned = bytes.ToArray();
        using JsonDocument document = JsonDocument.Parse(owned, new JsonDocumentOptions
        { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 32 });
        if (document.RootElement.ValueKind is JsonValueKind.Undefined) throw new ArgumentException("Studio JSON is invalid.", nameof(bytes));
        RequireUniqueMembers(document.RootElement);
        return new(owned);
        static void RequireUniqueMembers(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in value.EnumerateObject())
                { if (!names.Add(property.Name)) throw new ArgumentException("Studio JSON repeats an object member.", nameof(bytes)); RequireUniqueMembers(property.Value); }
            }
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (JsonElement item in value.EnumerateArray()) RequireUniqueMembers(item);
        }
    }
    /// <summary>Returns defensive canonical JSON bytes.</summary>
    public byte[] ToArray() => _bytes.ToArray();
}

/// <summary>Captures the common authority and exact registered owner of one producer call.</summary>
public sealed record BaseStudioProducerInvocation(
    BaseStudioBootstrapInvocation Bootstrap,
    BaseStudioResponseAuthority Authority,
    string RegisteredMethodId,
    BaseStudioCanonicalJson Request);

/// <summary>Produces one registered finite Studio view through its owning Runtime service.</summary>
public interface IBaseStudioViewProducer
{
    /// <summary>Reads the exact registered view and returns its L41 result document.</summary>
    ValueTask<BaseStudioCanonicalJson?> ReadAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken);
}

/// <summary>Resolves one registered typed resource without enumerating unauthorized identities.</summary>
public interface IBaseStudioResourceProducer
{
    /// <summary>Resolves the exact registered resource and returns unavailable as <see langword="null"/>.</summary>
    ValueTask<BaseStudioCanonicalJson?> ResolveAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken);
}

/// <summary>Resolves registered typed cross-resource links.</summary>
public interface IBaseStudioLinkProducer
{
    /// <summary>Returns only currently disclosed links for the registered source relation.</summary>
    ValueTask<BaseStudioCanonicalJson?> ResolveAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken);
}

/// <summary>Previews and executes one exact registered semantic command.</summary>
public interface IBaseStudioCommandProducer
{
    /// <summary>Creates a fresh bounded preview through the owning Runtime service.</summary>
    ValueTask<BaseStudioCanonicalJson?> PreviewAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken);
    /// <summary>Executes the reviewed command through the owning Runtime service.</summary>
    ValueTask<BaseStudioCanonicalJson?> ExecuteAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken);
}

/// <summary>Signals that a command producer proved it returned before any owning-subsystem influence.</summary>
public sealed class BaseStudioCommandFailedBeforeInfluenceException : Exception
{
    /// <summary>Creates the stable before-influence signal.</summary>
    public BaseStudioCommandFailedBeforeInfluenceException() : base("base.studio.failedBeforeInfluence") { }
}

/// <summary>Signals that command influence cannot be excluded and only receipt resolution is safe.</summary>
public sealed class BaseStudioCommandIndeterminateException : Exception
{
    /// <summary>Creates the stable indeterminate signal.</summary>
    public BaseStudioCommandIndeterminateException() : base("base.studio.commandIndeterminate") { }
}

/// <summary>Binds one exact method to one closed module-owned executable producer kind.</summary>
public abstract record BaseStudioProducerBinding(string RegisteredMethodId, BaseStudioMethodKind Kind);
/// <summary>Binds one page method to a view producer.</summary>
public sealed record BaseStudioViewProducerBinding(string MethodId, IBaseStudioViewProducer Producer)
    : BaseStudioProducerBinding(MethodId, BaseStudioMethodKind.Page);
/// <summary>Binds one resolver method to a resource producer.</summary>
public sealed record BaseStudioResourceProducerBinding(string MethodId, IBaseStudioResourceProducer Producer)
    : BaseStudioProducerBinding(MethodId, BaseStudioMethodKind.Resolve);
/// <summary>Binds one link resolver method to a link producer.</summary>
public sealed record BaseStudioLinkProducerBinding(string MethodId, IBaseStudioLinkProducer Producer)
    : BaseStudioProducerBinding(MethodId, BaseStudioMethodKind.Resolve);
/// <summary>Binds one preview method to a command producer.</summary>
public sealed record BaseStudioCommandPreviewProducerBinding(string MethodId, IBaseStudioCommandProducer Producer)
    : BaseStudioProducerBinding(MethodId, BaseStudioMethodKind.Preview);
/// <summary>Binds one execute method to a command producer.</summary>
public sealed record BaseStudioCommandExecuteProducerBinding(string MethodId, IBaseStudioCommandProducer Producer)
    : BaseStudioProducerBinding(MethodId, BaseStudioMethodKind.Execute);

/// <summary>Contributes one module's exact L41 map nodes and executable producer bindings.</summary>
public sealed class BaseStudioModuleRuntimeContribution
{
    private BaseStudioModuleRuntimeContribution(string moduleId, int version, BaseStudioSha256 registration,
        ImmutableArray<BaseStudioNamedTypeContract> types, ImmutableArray<BaseStudioEndpointContract> endpoints,
        ImmutableArray<BaseStudioMethodBinding> methods, ImmutableArray<BaseStudioProducerBinding> producers)
    { ModuleId = moduleId; Version = version; RegistrationChecksum = registration; Types = types; Endpoints = endpoints; Methods = methods; Producers = producers; }
    /// <summary>Gets the owning module identity.</summary>
    public string ModuleId { get; }
    /// <summary>Gets the owning module version.</summary>
    public int Version { get; }
    /// <summary>Gets the exact immutable module-registration checksum.</summary>
    public BaseStudioSha256 RegistrationChecksum { get; }
    /// <summary>Gets named L41 types in canonical identity order.</summary>
    public ImmutableArray<BaseStudioNamedTypeContract> Types { get; }
    /// <summary>Gets endpoints in canonical identity/version order.</summary>
    public ImmutableArray<BaseStudioEndpointContract> Endpoints { get; }
    /// <summary>Gets methods in canonical registered identity order.</summary>
    public ImmutableArray<BaseStudioMethodBinding> Methods { get; }
    /// <summary>Gets executable producer bindings in canonical registered identity order.</summary>
    public ImmutableArray<BaseStudioProducerBinding> Producers { get; }

    /// <summary>Creates and validates a module-owned runtime contribution.</summary>
    public static BaseStudioModuleRuntimeContribution Create(BaseStudioModuleRegistration module,
        IEnumerable<BaseStudioNamedTypeContract> types, IEnumerable<BaseStudioEndpointContract> endpoints,
        IEnumerable<BaseStudioMethodBinding> methods, IEnumerable<BaseStudioProducerBinding> producers)
    {
        ArgumentNullException.ThrowIfNull(module);
        ImmutableArray<BaseStudioNamedTypeContract> ts = StudioGraphValidation.OrderedIdentity(types, 2_048, static x => x.TypeId, nameof(types));
        ImmutableArray<BaseStudioEndpointContract> es = StudioGraphValidation.Ordered(endpoints, 512, static x => (x.EndpointId, x.Version), nameof(endpoints), true);
        ImmutableArray<BaseStudioMethodBinding> ms = StudioGraphValidation.OrderedIdentity(methods, 1_024, static x => x.RegisteredMethodId, nameof(methods));
        ImmutableArray<BaseStudioProducerBinding> ps = StudioContractValidation.Materialize(producers, 1_024, true, nameof(producers));
        if (!ps.Select(static x => x.RegisteredMethodId).SequenceEqual(ps.Select(static x => x.RegisteredMethodId).Order(StringComparer.Ordinal)) ||
            ps.Select(static x => x.RegisteredMethodId).Distinct(StringComparer.Ordinal).Count() != ps.Length ||
            ms.Select(static x => x.RegisteredMethodId).Except(ps.Select(static x => x.RegisteredMethodId), StringComparer.Ordinal).Any() ||
            ps.Select(static x => x.RegisteredMethodId).Except(ms.Select(static x => x.RegisteredMethodId), StringComparer.Ordinal).Any())
            throw new ArgumentException("Studio producer correspondence is invalid.", nameof(producers));
        foreach (BaseStudioProducerBinding binding in ps)
        {
            BaseStudioMethodBinding method = ms.Single(x => StringComparer.Ordinal.Equals(x.RegisteredMethodId, binding.RegisteredMethodId));
            if (method.Kind != binding.Kind || !StringComparer.Ordinal.Equals(method.OwningModuleId, module.Identity.ModuleId) || !Compatible(binding))
                throw new ArgumentException("Studio producer kind or owner is invalid.", nameof(producers));
        }
        return new(module.Identity.ModuleId, module.Identity.Version, BaseStudioSha256.FromDigest(module.Identity.Checksum.ToArray()), ts, es, ms, ps);
    }

    private static bool Compatible(BaseStudioProducerBinding binding) => binding.Kind switch
    {
        BaseStudioMethodKind.Resolve => binding is BaseStudioResourceProducerBinding { Producer: not null } or BaseStudioLinkProducerBinding { Producer: not null },
        BaseStudioMethodKind.Page => binding is BaseStudioViewProducerBinding { Producer: not null },
        BaseStudioMethodKind.Preview => binding is BaseStudioCommandPreviewProducerBinding { Producer: not null },
        BaseStudioMethodKind.Execute => binding is BaseStudioCommandExecuteProducerBinding { Producer: not null },
        _ => false,
    };
}

/// <summary>Builds one module's executable Runtime contribution from its finalized registration.</summary>
public interface IBaseStudioModuleRuntimeContributionFactory
{
    /// <summary>Gets the exact module identity owned by this factory.</summary>
    string ModuleId { get; }
    /// <summary>Creates the exact contribution for the supplied graph-owned module.</summary>
    BaseStudioModuleRuntimeContribution Create(BaseStudioModuleRegistration module);
}
