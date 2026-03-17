// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using HPD.Agent.Audio.Vad;

namespace HPD.Agent.AudioProviders.Silero;

/// <summary>
/// Auto-registers the Silero VAD provider on assembly load.
/// </summary>
public static class SileroVadProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        VadProviderDiscovery.RegisterFactory("silero-vad", () => new SileroVadProviderFactory());
        VadProviderDiscovery.RegisterConfigType<SileroVadConfig>("silero-vad");
    }
}
