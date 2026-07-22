using HPD.Agent.ToolHarness.Coding.Debugging.Generated;
using System.Collections.Immutable;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public static class BuiltInDebugAdapterCatalog
{
    private static readonly IReadOnlyList<DebugAdapterCatalogEntry> FrozenEntries =
        GeneratedDebugAdapterCatalogProvider_HPD_Agent_Harness_Coding.All
            .Select(DebugAdapterCatalogSnapshot.Freeze)
            .ToImmutableArray();

    public static IReadOnlyList<DebugAdapterCatalogEntry> Entries
        => FrozenEntries;

    public static IDebugAdapterCatalogProvider CreateProvider()
        => new GeneratedDebugAdapterCatalogProvider_HPD_Agent_Harness_Coding();
}
