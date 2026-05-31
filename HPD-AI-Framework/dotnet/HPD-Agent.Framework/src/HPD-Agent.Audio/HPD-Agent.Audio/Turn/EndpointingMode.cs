// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Turn;

/// <summary>
/// Selects the source of authority for committing user turns.
/// </summary>
public enum EndpointingMode
{
    /// <summary>Turns are only committed by explicit manual commit.</summary>
    Manual,

    /// <summary>VAD or recognizer speech end drives endpointing.</summary>
    Vad,

    /// <summary>STT final transcript drives endpointing.</summary>
    Stt,

    /// <summary>Recognition, VAD, and EOT signals are combined.</summary>
    Hybrid,

    /// <summary>Realtime model turn events drive endpointing.</summary>
    RealtimeModel
}
