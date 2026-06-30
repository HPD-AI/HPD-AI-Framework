// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent.Evaluations;

/// <summary>
/// Identifies the origin of an evaluation score stored in IScoreStore.
/// </summary>
public enum EvaluationSource
{
    /// <summary>Scored online by LiveEvaluationMiddleware during a live agent run.</summary>
    Live,

    /// <summary>Scored offline by RunEvals during a CI batch evaluation.</summary>
    Test,

    /// <summary>Scored after-the-fact by RetroactiveScorer against stored thread messages.</summary>
    Retroactive,

    /// <summary>Submitted by a human annotator via AnnotationQueue.</summary>
    Human,
}
