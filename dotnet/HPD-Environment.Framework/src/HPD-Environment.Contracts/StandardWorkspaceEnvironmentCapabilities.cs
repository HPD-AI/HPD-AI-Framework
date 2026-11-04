namespace HPD.Environment.Contracts;

/// <summary>
/// Stable capability identifiers reported by providers that expose portable
/// workspace file operations.
/// </summary>
public static class StandardWorkspaceEnvironmentCapabilities
{
    /// <summary>Common prefix for portable workspace capability identifiers.</summary>
    public const string Prefix = "hpd.environment.workspace.";

    /// <summary>Read access through logical workspace paths.</summary>
    public static CapabilityId Read { get; } =
        new("hpd.environment.workspace.read");

    /// <summary>Text and binary mutation support.</summary>
    public static CapabilityId Write { get; } =
        new("hpd.environment.workspace.write");

    /// <summary>File and directory deletion support.</summary>
    public static CapabilityId Delete { get; } =
        new("hpd.environment.workspace.delete");

    /// <summary>Bounded directory enumeration.</summary>
    public static CapabilityId List { get; } =
        new("hpd.environment.workspace.list");

    /// <summary>Root-scoped text search.</summary>
    public static CapabilityId Search { get; } =
        new("hpd.environment.workspace.search");

    /// <summary>Bounded binary content reads.</summary>
    public static CapabilityId Binary { get; } =
        new("hpd.environment.workspace.binary");

    /// <summary>Create-only and expected-version mutation checks.</summary>
    public static CapabilityId ConditionalWrite { get; } =
        new("hpd.environment.workspace.conditional-write");

    /// <summary>Mapping between logical and execution paths.</summary>
    public static CapabilityId PathMapping { get; } =
        new("hpd.environment.workspace.path-mapping");

    /// <summary>Provider-reported path case sensitivity.</summary>
    public static CapabilityId CaseSensitivity { get; } =
        new("hpd.environment.workspace.case-sensitivity");

    /// <summary>Provider-enforced symbolic-link handling.</summary>
    public static CapabilityId SymlinkControl { get; } =
        new("hpd.environment.workspace.symlink-control");

    /// <summary>Gets the capabilities required for one workspace request.</summary>
    public static IReadOnlyList<CapabilityId> RequiredFor(
        AccessMode access,
        bool includeSearch = true)
    {
        List<CapabilityId> required =
        [
            Read,
            List,
            Binary,
            PathMapping,
            CaseSensitivity,
            SymlinkControl,
        ];
        if (access is not AccessMode.ReadOnly)
        {
            required.Add(Write);
            required.Add(Delete);
            required.Add(ConditionalWrite);
        }
        if (includeSearch)
        {
            required.Add(Search);
        }
        return required;
    }
}
