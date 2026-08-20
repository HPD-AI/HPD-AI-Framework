namespace HPD.Base;

/// <summary>Declares a public marker for one generated exported logical-subject contract.</summary>
/// <param name="id">The stable exported-contract identifier.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BaseExportedSubjectAttribute(string id) : Attribute
{
    /// <summary>Gets the stable exported-contract identifier.</summary>
    public string Id { get; } = id;
    /// <summary>Gets or sets the positive contract version.</summary>
    public int Version { get; set; } = 1;
    /// <summary>Gets or sets the installed module that owns the contract.</summary>
    public required string OwningModuleId { get; set; }
    /// <summary>Gets or sets the canonical subject-identifier grammar.</summary>
    public BaseSubjectIdKind SubjectIdKind { get; set; } = BaseSubjectIdKind.OrdinalString;
    /// <summary>Gets or sets the maximum canonical UTF-8 subject-identifier length.</summary>
    public int MaximumSubjectIdUtf8Bytes { get; set; } = 256;
    /// <summary>Gets or sets the private system collection record type.</summary>
    public required Type PrivateRecordType { get; set; }
    /// <summary>Gets or sets the exact acquisition grant identifier.</summary>
    public required string AcquisitionGrantId { get; set; }
    /// <summary>Gets or sets the exact mutation-bound validation grant identifier.</summary>
    public required string ValidationGrantId { get; set; }
    /// <summary>Gets or sets the exact authority-epoch administration grant identifier.</summary>
    public required string AdministrationGrantId { get; set; }
    /// <summary>Gets or sets the stable validation-plan identifier.</summary>
    public required string ValidationPlanId { get; set; }
    /// <summary>Gets or sets the positive validation-plan version.</summary>
    public int ValidationPlanVersion { get; set; } = 1;
    /// <summary>Gets or sets the logical subject scope.</summary>
    public BaseSubjectScopeKind Scope { get; set; } = BaseSubjectScopeKind.Global;
    /// <summary>Gets or sets the stable required Boolean active-state field ID.</summary>
    public string? ActiveFieldId { get; set; }
    /// <summary>Gets or sets the Boolean value representing active state.</summary>
    public bool ActiveValue { get; set; } = true;
    /// <summary>Gets or sets the stable required Boolean tombstone-state field ID.</summary>
    public required string TombstoneFieldId { get; set; }
    /// <summary>Gets or sets whether coordinated retirement may be installed.</summary>
    public bool SupportsCoordinatedRetirement { get; set; }
    /// <summary>Gets or sets the stable required ordinal scope field ID.</summary>
    public string? ScopeFieldId { get; set; }
    /// <summary>Gets or sets the audiences as a flags-free array supplied by generated metadata.</summary>
    public HPDBaseEndpointAudience[] Audiences { get; set; } = [HPDBaseEndpointAudience.Application];
}

/// <summary>Declares one generated scalar field as an exported logical-subject reference.</summary>
/// <param name="subjectType">The public exported-subject marker type.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class BaseSubjectReferenceAttribute(Type subjectType) : Attribute
{
    /// <summary>Gets the public exported-subject marker type.</summary>
    public Type SubjectType { get; } = subjectType;
    /// <summary>Gets or sets the required logical validity state.</summary>
    public BaseSubjectReferenceRequirement Requirement { get; set; } = BaseSubjectReferenceRequirement.Exists;
    /// <summary>Gets or sets the required transaction validation guarantee.</summary>
    public BaseSubjectValidationGuarantee Guarantee { get; set; } = BaseSubjectValidationGuarantee.TransactionSnapshot;
}
