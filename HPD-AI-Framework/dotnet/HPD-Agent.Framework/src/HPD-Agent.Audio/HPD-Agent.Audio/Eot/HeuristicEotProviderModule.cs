// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using HPD.Agent.Providers;

namespace HPD.Agent.Audio.Eot;

/// <summary>
/// Registers the built-in heuristic EOT provider.
/// </summary>
public static class HeuristicEotProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
    public static void Initialize()
#pragma warning restore CA2255
    {
        ProviderDiscovery.RegisterProviderFactory(() => new HeuristicEotProvider());
        ProviderDiscovery.RegisterProviderConfigType<EotConfig>(
            "heuristic-eot",
            ProviderClientFamily.EndOfTurnDetection,
            json => System.Text.Json.JsonSerializer.Deserialize<EotConfig>(json),
            config => System.Text.Json.JsonSerializer.Serialize(config));
    }
}
