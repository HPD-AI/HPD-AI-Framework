namespace HPD.Base.Runtime;

public enum BaseRuntimeValidationSeverity
{
    Info,
    Warning,
    Error,
    Fatal
}

public enum BaseRuntimeValidationFailureKind
{
    DuplicateId,
    UnresolvedReference,
    InconsistentVisibility,
    CapabilityDependencyConflict,
    DescriptorInterfaceMismatch,
    InvalidContribution,
    UnsafeDescriptorClaim,
    InvalidConfiguration
}
