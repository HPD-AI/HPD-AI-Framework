// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Evaluations;

/// <summary>
/// Identifies the origin of an evaluation score stored in IScoreStore.
/// </summary>
public enum EvaluationSource
{
    /// <summary>Scored online by EvaluationMiddleware during a live agent run.</summary>
    Live,

    /// <summary>Scored offline by RunEvals during a CI batch evaluation.</summary>
    Test,

    /// <summary>Scored after-the-fact by RetroactiveScorer against stored branch messages.</summary>
    Retroactive,

    /// <summary>Submitted by a human annotator via AnnotationQueue.</summary>
    Human,
}
