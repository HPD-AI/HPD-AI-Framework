namespace HPD.Base;

/// <summary>Defines the base runtime validation severity contract.</summary>
public enum BaseRuntimeValidationSeverity
{
    /// <summary>Identifies info.</summary>
Info,
    /// <summary>Identifies warning.</summary>
Warning,
    /// <summary>Identifies error.</summary>
Error,
    /// <summary>Identifies fatal.</summary>
Fatal
}

/// <summary>Defines the base runtime validation failure kind contract.</summary>
public enum BaseRuntimeValidationFailureKind
{
    /// <summary>Identifies duplicate ID.</summary>
DuplicateId,
    /// <summary>Identifies unresolved reference.</summary>
UnresolvedReference,
    /// <summary>Identifies inconsistent visibility.</summary>
InconsistentVisibility,
    /// <summary>Identifies capability dependency conflict.</summary>
CapabilityDependencyConflict,
    /// <summary>Identifies descriptor interface mismatch.</summary>
DescriptorInterfaceMismatch,
    /// <summary>Identifies invalid contribution.</summary>
InvalidContribution,
    /// <summary>Identifies unsafe descriptor claim.</summary>
UnsafeDescriptorClaim,
    /// <summary>Identifies invalid configuration.</summary>
InvalidConfiguration
}
