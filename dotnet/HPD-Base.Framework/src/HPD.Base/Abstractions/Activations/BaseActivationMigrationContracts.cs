using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Requests one exact live activation as input to an installed migration projection.</summary>
public sealed record BaseActivationMigrationCandidateRequest
{
    /// <summary>Gets the application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the protected semantic scope seek.</summary>
    public required BaseOwnedScopeSeekAuthority Scope { get; init; }
    /// <summary>Gets the exact source definition.</summary>
    public required BaseActivationDefinitionKey SourceDefinition { get; init; }
    /// <summary>Gets the activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the expected live activation generation.</summary>
    public required long ExpectedGeneration { get; init; }
    /// <summary>Gets trusted accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the effective provider limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Contains exact immutable source authority for one migration projection.</summary>
public sealed record BaseActivationMigrationCandidate
{
    /// <summary>Gets the activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the exact source definition.</summary>
    public required BaseActivationDefinitionKey SourceDefinition { get; init; }
    /// <summary>Gets the current activation generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets the current live state.</summary>
    public required BaseActivationState State { get; init; }
    /// <summary>Gets the canonical source input.</summary>
    public required ImmutableArray<byte> CanonicalInput { get; init; }
    /// <summary>Gets the SHA-256 source-input checksum.</summary>
    public required ImmutableArray<byte> InputChecksum { get; init; }
    /// <summary>Gets the source activation control checksum.</summary>
    public required ImmutableArray<byte> ControlChecksum { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
}

/// <summary>Requests one atomic old-to-new activation migration.</summary>
public sealed record BaseActivationMigrationRequest
{
    /// <summary>Gets the application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the protected semantic scope seek.</summary>
    public required BaseOwnedScopeSeekAuthority Scope { get; init; }
    /// <summary>Gets the exact source definition.</summary>
    public required BaseActivationDefinitionKey SourceDefinition { get; init; }
    /// <summary>Gets the source activation identity.</summary>
    public required string SourceActivationId { get; init; }
    /// <summary>Gets the exact expected source generation.</summary>
    public required long ExpectedSourceGeneration { get; init; }
    /// <summary>Gets the expected source input checksum.</summary>
    public required ImmutableArray<byte> ExpectedSourceInputChecksum { get; init; }
    /// <summary>Gets the Runtime-derived replacement activation identity.</summary>
    public required string ReplacementActivationId { get; init; }
    /// <summary>Gets the complete replacement activation intent.</summary>
    public required BaseActivationCreateIntent Replacement { get; init; }
    /// <summary>Gets the installed migration definition identity.</summary>
    public required string MigrationId { get; init; }
    /// <summary>Gets the installed migration definition version.</summary>
    public required int MigrationVersion { get; init; }
    /// <summary>Gets the installed migration checksum.</summary>
    public required ImmutableArray<byte> MigrationChecksum { get; init; }
    /// <summary>Gets trusted accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the identified semantic operation.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets the effective provider limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Contains the two committed facts owned by one migration receipt.</summary>
public sealed record BaseActivationMigrationResult
{
    /// <summary>Gets the migrated source activation identity.</summary>
    public required string SourceActivationId { get; init; }
    /// <summary>Gets the committed source generation.</summary>
    public required long SourceGeneration { get; init; }
    /// <summary>Gets the source control checksum.</summary>
    public required ImmutableArray<byte> SourceControlChecksum { get; init; }
    /// <summary>Gets the replacement activation identity.</summary>
    public required string ReplacementActivationId { get; init; }
    /// <summary>Gets the committed replacement generation.</summary>
    public required long ReplacementGeneration { get; init; }
    /// <summary>Gets the replacement control checksum.</summary>
    public required ImmutableArray<byte> ReplacementControlChecksum { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets request disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
}
