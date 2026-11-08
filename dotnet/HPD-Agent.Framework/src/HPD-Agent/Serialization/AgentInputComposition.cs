using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Agent.Serialization;

/// <summary>Complete reflection-free serialization metadata for one agent input contract.</summary>
public sealed record AgentInputDescriptor
{
    /// <summary>Gets the stable input discriminator.</summary>
    public required string Discriminator { get; init; }

    /// <summary>Gets the concrete input CLR type.</summary>
    public required Type InputType { get; init; }

    /// <summary>Gets the source-generated JSON metadata for the input.</summary>
    public required JsonTypeInfo JsonTypeInfo { get; init; }

    /// <summary>Gets the stable module identity that owns the input.</summary>
    public required string ModuleId { get; init; }

    /// <summary>Gets whether untrusted clients may submit this input through public endpoints.</summary>
    public bool IsPublic { get; init; }
}

/// <summary>Immutable input declarations owned by one assembly or capability module.</summary>
public sealed record AgentInputModuleFragment
{
    /// <summary>Gets the stable reverse-domain-style module identity.</summary>
    public required string ModuleId { get; init; }

    /// <summary>Gets the inputs declared by the module.</summary>
    public required IReadOnlyList<AgentInputDescriptor> Inputs { get; init; }
}

/// <summary>Assembly metadata used by the application generator to close input modules.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class HpdAgentInputModuleManifestAttribute(
    string moduleId,
    Type fragmentProviderType,
    params Type[] dependencyFragmentProviderTypes) : Attribute
{
    /// <summary>Gets the module identity.</summary>
    public string ModuleId { get; } = moduleId;

    /// <summary>Gets the generated public fragment provider.</summary>
    public Type FragmentProviderType { get; } = fragmentProviderType;

    /// <summary>Gets the explicit transitive fragment dependencies.</summary>
    public IReadOnlyList<Type> DependencyFragmentProviderTypes { get; } = dependencyFragmentProviderTypes;
}

/// <summary>Safe remotely consumable description of one input contract.</summary>
public sealed record AgentInputCatalogEntry(
    string Discriminator,
    string ContractName,
    string ModuleId,
    bool IsPublic);

/// <summary>Deterministic public catalog for an immutable input composition.</summary>
public sealed record AgentInputCatalog
{
    /// <summary>Gets input entries in canonical order.</summary>
    public required IReadOnlyList<AgentInputCatalogEntry> Inputs { get; init; }

    /// <summary>Gets the deterministic compatibility digest.</summary>
    public required string Digest { get; init; }
}

/// <summary>Thrown when a codec cannot resolve an agent input discriminator.</summary>
public sealed class UnknownAgentInputDiscriminatorException : System.Text.Json.JsonException
{
    /// <summary>Creates an unknown-input failure.</summary>
    public UnknownAgentInputDiscriminatorException(string discriminator, string codecDigest)
        : base($"Agent input discriminator '{discriminator}' is not present in codec '{codecDigest}'.")
    {
        Discriminator = discriminator;
        CodecDigest = codecDigest;
    }

    /// <summary>Gets the unknown discriminator.</summary>
    public string Discriminator { get; }

    /// <summary>Gets the rejecting composition digest.</summary>
    public string CodecDigest { get; }
}

/// <summary>The immutable input serialization authority for one application composition.</summary>
public sealed class AgentInputComposition
{
    private readonly IReadOnlyDictionary<Type, AgentInputDescriptor> _byType;
    private readonly IReadOnlyDictionary<string, AgentInputDescriptor> _byDiscriminator;

    private AgentInputComposition(
        IReadOnlyList<AgentInputModuleFragment> fragments,
        IReadOnlyDictionary<Type, AgentInputDescriptor> byType,
        IReadOnlyDictionary<string, AgentInputDescriptor> byDiscriminator,
        AgentInputCatalog catalog)
    {
        Fragments = fragments;
        _byType = byType;
        _byDiscriminator = byDiscriminator;
        Catalog = catalog;
    }

    /// <summary>Gets the immutable module fragments in canonical order.</summary>
    public IReadOnlyList<AgentInputModuleFragment> Fragments { get; }

    /// <summary>Gets all input descriptors in canonical order.</summary>
    public IReadOnlyList<AgentInputDescriptor> Inputs =>
        [.. _byDiscriminator.Values];

    /// <summary>Gets the public catalog.</summary>
    public AgentInputCatalog Catalog { get; }

    /// <summary>Gets the composition compatibility digest.</summary>
    public string Digest => Catalog.Digest;

    /// <summary>Returns whether untrusted clients may submit this input type.</summary>
    public bool IsPublic(Type inputType) =>
        _byType.TryGetValue(inputType, out var descriptor) && descriptor.IsPublic;

    /// <summary>Looks up an input descriptor by concrete type.</summary>
    public bool TryGetByType(Type inputType, out AgentInputDescriptor descriptor) =>
        _byType.TryGetValue(inputType, out descriptor!);

    /// <summary>Looks up an input descriptor by discriminator.</summary>
    public bool TryGetByDiscriminator(string discriminator, out AgentInputDescriptor descriptor) =>
        _byDiscriminator.TryGetValue(discriminator, out descriptor!);

    /// <summary>Creates and validates one immutable input composition.</summary>
    public static AgentInputComposition Create(IReadOnlyList<AgentInputModuleFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        if (fragments.Count == 0)
            throw new ArgumentException("An agent input composition requires at least one module.", nameof(fragments));

        var canonicalFragments = new List<AgentInputModuleFragment>(fragments.Count);
        var moduleIds = new HashSet<string>(StringComparer.Ordinal);
        var byType = new Dictionary<Type, AgentInputDescriptor>();
        var byDiscriminator = new Dictionary<string, AgentInputDescriptor>(StringComparer.Ordinal);

        foreach (var fragment in fragments
                     .OrderBy(static fragment => fragment.ModuleId, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(fragment.ModuleId))
                throw new ArgumentException("Input module IDs must be non-empty.", nameof(fragments));
            if (!moduleIds.Add(fragment.ModuleId))
                throw new ArgumentException($"Duplicate input module ID '{fragment.ModuleId}'.", nameof(fragments));

            var inputs = fragment.Inputs
                .OrderBy(static input => input.Discriminator, StringComparer.Ordinal)
                .ToArray();
            foreach (var descriptor in inputs)
            {
                ValidateDescriptor(fragment.ModuleId, descriptor);
                if (!byType.TryAdd(descriptor.InputType, descriptor))
                    throw new ArgumentException($"Duplicate input type '{descriptor.InputType.FullName}'.", nameof(fragments));
                if (!byDiscriminator.TryAdd(descriptor.Discriminator, descriptor))
                    throw new ArgumentException($"Duplicate input discriminator '{descriptor.Discriminator}'.", nameof(fragments));
            }
            canonicalFragments.Add(new AgentInputModuleFragment
            {
                ModuleId = fragment.ModuleId,
                Inputs = Array.AsReadOnly(inputs)
            });
        }

        var ordered = byDiscriminator.Values
            .OrderBy(static input => input.Discriminator, StringComparer.Ordinal)
            .ToArray();
        var entries = ordered.Select(static input => new AgentInputCatalogEntry(
            input.Discriminator,
            input.InputType.Name,
            input.ModuleId,
            input.IsPublic)).ToArray();
        var digest = ComputeDigest(entries);
        var catalog = new AgentInputCatalog
        {
            Inputs = Array.AsReadOnly(entries),
            Digest = digest
        };
        return new AgentInputComposition(
            Array.AsReadOnly(canonicalFragments.ToArray()),
            new ReadOnlyDictionary<Type, AgentInputDescriptor>(byType),
            new ReadOnlyDictionary<string, AgentInputDescriptor>(byDiscriminator),
            catalog);
    }

    internal static bool AreEquivalent(AgentInputModuleFragment left, AgentInputModuleFragment right) =>
        string.Equals(left.ModuleId, right.ModuleId, StringComparison.Ordinal) &&
        left.Inputs.Count == right.Inputs.Count &&
        left.Inputs.Zip(right.Inputs).All(static pair =>
            string.Equals(pair.First.Discriminator, pair.Second.Discriminator, StringComparison.Ordinal) &&
            pair.First.InputType == pair.Second.InputType &&
            pair.First.IsPublic == pair.Second.IsPublic &&
            string.Equals(pair.First.ModuleId, pair.Second.ModuleId, StringComparison.Ordinal));

    private static void ValidateDescriptor(string moduleId, AgentInputDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Discriminator))
            throw new ArgumentException("Input descriptors require a discriminator.");
        if (!typeof(AgentInputEvent).IsAssignableFrom(descriptor.InputType))
            throw new ArgumentException($"Input type '{descriptor.InputType.FullName}' does not derive from AgentInputEvent.");
        if (descriptor.InputType.IsAbstract || descriptor.InputType.IsGenericType)
            throw new ArgumentException($"Input type '{descriptor.InputType.FullName}' must be concrete and closed.");
        if (!string.Equals(descriptor.ModuleId, moduleId, StringComparison.Ordinal))
            throw new ArgumentException($"Input '{descriptor.InputType.FullName}' has module '{descriptor.ModuleId}', expected '{moduleId}'.");
        if (descriptor.JsonTypeInfo.Type != descriptor.InputType)
            throw new ArgumentException($"Input '{descriptor.InputType.FullName}' has mismatched JSON metadata.");
    }

    private static string ComputeDigest(IReadOnlyList<AgentInputCatalogEntry> entries)
    {
        var text = string.Join('\n', entries.Select(static entry =>
            $"{entry.Discriminator}\t{entry.ContractName}\t{entry.ModuleId}\t{entry.IsPublic}"));
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}

/// <summary>Global registry for generated input modules and application compositions.</summary>
public static class AgentInputCompositionHost
{
    private static readonly ConcurrentDictionary<string, AgentInputComposition> Applications =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, AgentInputModuleFragment> Modules =
        new(StringComparer.Ordinal);

    /// <summary>Registers one complete application input composition.</summary>
    public static void RegisterApplication(AgentInputComposition composition, string applicationAssemblyIdentity)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationAssemblyIdentity);
        if (!Applications.TryAdd(applicationAssemblyIdentity, composition) &&
            !ReferenceEquals(Applications[applicationAssemblyIdentity], composition))
            throw new InvalidOperationException($"Duplicate input composition for application '{applicationAssemblyIdentity}'.");
    }

    /// <summary>Registers one generated module fragment.</summary>
    public static void RegisterModule(AgentInputModuleFragment fragment, string assemblyIdentity)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyIdentity);
        if (!Modules.TryAdd(fragment.ModuleId, fragment) &&
            !AgentInputComposition.AreEquivalent(Modules[fragment.ModuleId], fragment))
            throw new InvalidOperationException($"Conflicting input module fragment '{fragment.ModuleId}'.");
    }

    /// <summary>Attempts to resolve a registered application composition.</summary>
    public static bool TryGetApplication(string applicationAssemblyIdentity, out AgentInputComposition composition) =>
        Applications.TryGetValue(applicationAssemblyIdentity, out composition!);

    /// <summary>Attempts to resolve the sole registered application composition.</summary>
    public static bool TryGetDefaultApplication(out AgentInputComposition composition)
    {
        if (HPDApplicationCompositionHost.TryGetApplication(out var application))
        {
            composition = application.Inputs;
            return true;
        }

        var values = Applications.Values.Distinct().ToArray();
        if (values.Length == 1)
        {
            composition = values[0];
            return true;
        }
        composition = null!;
        return false;
    }

    /// <summary>Composes all registered input modules.</summary>
    public static bool TryComposeRegisteredModules(out AgentInputComposition composition)
    {
        if (Modules.IsEmpty)
        {
            composition = null!;
            return false;
        }
        composition = AgentInputComposition.Create([.. Modules.Values]);
        return true;
    }

    /// <summary>Resolves the application, segment modules, or the core input surface.</summary>
    public static AgentInputComposition ResolveDefault()
    {
        if (TryGetDefaultApplication(out var application))
            return application;
        if (TryComposeRegisteredModules(out var modules))
            return modules;
        throw new InvalidOperationException(
            "No generated agent input composition is registered. Reference the HPD-Agent source generator.");
    }
}
