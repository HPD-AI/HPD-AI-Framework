// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;

namespace HPD.Agent.Audio.Eot;

/// <summary>
/// Registers the built-in heuristic EOT provider.
/// </summary>
internal static class HeuristicEotProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
    public static void Initialize()
#pragma warning restore CA2255
    {
        EotProviderDiscovery.RegisterFactory("heuristic-eot", () => new HeuristicEotProviderFactory());
        EotProviderDiscovery.RegisterConfigType<EotConfig>("heuristic-eot");
    }
}
