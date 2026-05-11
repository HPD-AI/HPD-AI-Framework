// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Tts;

/// <summary>
/// Text selected for one TTS synthesis request.
/// </summary>
public sealed record TtsTextSegment(
    string Text,
    bool IsFinal,
    string Reason);
