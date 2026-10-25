// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent;

/// <summary>Describes a generated function's function-local activation contract.</summary>
public sealed record FunctionCollapseMetadata
{
    /// <summary>Additional-properties key for this metadata.</summary>
    public const string AdditionalPropertiesKey = "HPD.FunctionCollapse";

    /// <summary>Gets the stable owner-harness identity.</summary>
    public required string OwnerHarnessIdentity { get; init; }

    /// <summary>Gets the source declaration member name.</summary>
    public required string DeclarationMemberName { get; init; }

    /// <summary>Gets the generated target capability identity.</summary>
    public required CapabilityId TargetCapabilityId { get; init; }

    /// <summary>Gets the original model-facing function name.</summary>
    public required string TargetName { get; init; }

    /// <summary>Gets the generated activation capability identity.</summary>
    public required CapabilityId ActivationCapabilityId { get; init; }

    /// <summary>Gets the model-facing activation name.</summary>
    public required string ActivationName { get; init; }

    /// <summary>Gets the activation description.</summary>
    public required string Description { get; init; }

    /// <summary>Gets instructions returned by activation.</summary>
    public string? FunctionResult { get; init; }

    /// <summary>Gets instructions injected while the function is active.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Gets the canonical declaration selector.</summary>
    public string DeclarationSelector => $"{OwnerHarnessIdentity}::{DeclarationMemberName}";
}
