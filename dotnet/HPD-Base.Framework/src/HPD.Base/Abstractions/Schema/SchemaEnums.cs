namespace HPD.Base;
/// <summary>Defines schema Metadata Role.</summary>
public enum SchemaMetadataRole
{
    /// <summary>Identifies read Projection.</summary>
ReadProjection
}

/// <summary>Defines schema Mode.</summary>
public enum SchemaMode
{
    /// <summary>Identifies strict.</summary>
Strict,
    /// <summary>Identifies loose.</summary>
Loose,
    /// <summary>Identifies inferred.</summary>
Inferred,
    /// <summary>Identifies native.</summary>
Native,
    /// <summary>Identifies hybrid.</summary>
Hybrid
}

/// <summary>Defines unknown Field Policy.</summary>
public enum UnknownFieldPolicy
{
    /// <summary>Identifies reject.</summary>
Reject,
    /// <summary>Identifies preserve.</summary>
Preserve,
    /// <summary>Identifies strip.</summary>
Strip,
    /// <summary>Identifies store Native.</summary>
StoreNative
}

/// <summary>Defines validation Mode.</summary>
public enum ValidationMode
{
    /// <summary>Identifies runtime.</summary>
Runtime,
    /// <summary>Identifies store.</summary>
Store,
    /// <summary>Identifies native.</summary>
Native,
    /// <summary>Identifies advisory.</summary>
Advisory,
    /// <summary>Identifies disabled.</summary>
Disabled
}

/// <summary>Defines field Cardinality Kind.</summary>
public enum FieldCardinalityKind
{
    /// <summary>Identifies single.</summary>
Single,
    /// <summary>Identifies array.</summary>
Array,
    /// <summary>Identifies set.</summary>
Set,
    /// <summary>Identifies map.</summary>
Map
}

/// <summary>Defines default Value Kind.</summary>
public enum DefaultValueKind
{
    /// <summary>Identifies none.</summary>
None,
    /// <summary>Identifies literal.</summary>
Literal,
    /// <summary>Identifies generated.</summary>
Generated,
    /// <summary>Identifies store.</summary>
Store,
    /// <summary>Identifies native.</summary>
Native
}

/// <summary>Defines generation Kind.</summary>
public enum GenerationKind
{
    /// <summary>Identifies none.</summary>
None,
    /// <summary>Identifies id.</summary>
Id,
    /// <summary>Identifies timestamp.</summary>
Timestamp,
    /// <summary>Identifies revision.</summary>
Revision,
    /// <summary>Identifies store.</summary>
Store,
    /// <summary>Identifies native.</summary>
Native,
    /// <summary>Identifies custom.</summary>
Custom
}

/// <summary>Defines validation Rule Kind.</summary>
public enum ValidationRuleKind
{
    /// <summary>Identifies required.</summary>
Required,
    /// <summary>Identifies nullable.</summary>
Nullable,
    /// <summary>Identifies min Length.</summary>
MinLength,
    /// <summary>Identifies max Length.</summary>
MaxLength,
    /// <summary>Identifies min Value.</summary>
MinValue,
    /// <summary>Identifies max Value.</summary>
MaxValue,
    /// <summary>Identifies regex.</summary>
Regex,
    /// <summary>Identifies enum.</summary>
Enum,
    /// <summary>Identifies mime Type.</summary>
MimeType,
    /// <summary>Identifies max Bytes.</summary>
MaxBytes,
    /// <summary>Identifies min Items.</summary>
MinItems,
    /// <summary>Identifies max Items.</summary>
MaxItems,
    /// <summary>Identifies object Shape.</summary>
ObjectShape,
    /// <summary>Identifies check Expression.</summary>
CheckExpression,
    /// <summary>Identifies native Check.</summary>
NativeCheck,
    /// <summary>Identifies custom.</summary>
Custom
}

/// <summary>Defines validation Severity.</summary>
public enum ValidationSeverity
{
    /// <summary>Identifies info.</summary>
Info,
    /// <summary>Identifies warning.</summary>
Warning,
    /// <summary>Identifies error.</summary>
Error
}

/// <summary>Defines validation Applies To.</summary>
public enum ValidationAppliesTo
{
    /// <summary>Identifies create.</summary>
Create,
    /// <summary>Identifies update.</summary>
Update,
    /// <summary>Identifies patch.</summary>
Patch,
    /// <summary>Identifies replace.</summary>
Replace,
    /// <summary>Identifies query.</summary>
Query,
    /// <summary>Identifies admin.</summary>
Admin
}

/// <summary>Defines base Relation Owning Side.</summary>
public enum BaseRelationOwningSide
{
    /// <summary>Identifies source.</summary>
Source,
    /// <summary>Identifies target.</summary>
Target
}

/// <summary>Defines base Relation Multiplicity.</summary>
public enum BaseRelationMultiplicity
{
    /// <summary>Identifies zero Or One.</summary>
ZeroOrOne,
    /// <summary>Identifies exactly One.</summary>
ExactlyOne,
    /// <summary>Identifies many.</summary>
Many
}

/// <summary>Defines base Relation Delete Behavior.</summary>
public enum BaseRelationDeleteBehavior
{
    /// <summary>Identifies restrict.</summary>
Restrict,
    /// <summary>Identifies set Null.</summary>
SetNull,
    /// <summary>Identifies cascade.</summary>
Cascade
}

/// <summary>Defines file Reference Shape.</summary>
public enum FileReferenceShape
{
    /// <summary>Identifies name.</summary>
Name,
    /// <summary>Identifies id.</summary>
Id,
    /// <summary>Identifies url.</summary>
Url,
    /// <summary>Identifies object Ref.</summary>
ObjectRef,
    /// <summary>Identifies module Ref.</summary>
ModuleRef,
    /// <summary>Identifies custom.</summary>
Custom
}

/// <summary>Defines file Cleanup Policy.</summary>
public enum FileCleanupPolicy
{
    /// <summary>Identifies none.</summary>
None,
    /// <summary>Identifies module.</summary>
Module,
    /// <summary>Identifies store.</summary>
Store,
    /// <summary>Identifies native.</summary>
Native,
    /// <summary>Identifies advisory.</summary>
Advisory
}

/// <summary>Defines index Kind.</summary>
public enum IndexKind
{
    /// <summary>Identifies key.</summary>
Key,
    /// <summary>Identifies unique.</summary>
Unique,
    /// <summary>Identifies primary.</summary>
Primary,
    /// <summary>Identifies full Text.</summary>
FullText,
    /// <summary>Identifies vector.</summary>
Vector,
    /// <summary>Identifies geo.</summary>
Geo,
    /// <summary>Identifies native.</summary>
Native,
    /// <summary>Identifies custom.</summary>
Custom
}

/// <summary>Defines index Status.</summary>
public enum IndexStatus
{
    /// <summary>Identifies ready.</summary>
Ready,
    /// <summary>Identifies building.</summary>
Building,
    /// <summary>Identifies invalid.</summary>
Invalid,
    /// <summary>Identifies disabled.</summary>
Disabled,
    /// <summary>Identifies unknown.</summary>
Unknown
}

/// <summary>Defines index Part Kind.</summary>
public enum IndexPartKind
{
    /// <summary>Identifies field.</summary>
Field,
    /// <summary>Identifies expression.</summary>
Expression,
    /// <summary>Identifies native.</summary>
Native,
    /// <summary>Identifies custom.</summary>
Custom
}

/// <summary>Defines index Sort Direction.</summary>
public enum IndexSortDirection
{
    /// <summary>Identifies asc.</summary>
Asc,
    /// <summary>Identifies desc.</summary>
Desc
}

/// <summary>Defines index Null Order.</summary>
public enum IndexNullOrder
{
    /// <summary>Identifies unspecified.</summary>
Unspecified,
    /// <summary>Identifies first.</summary>
First,
    /// <summary>Identifies last.</summary>
Last
}

/// <summary>Defines enforcement Owner.</summary>
public enum EnforcementOwner
{
    /// <summary>Identifies runtime.</summary>
Runtime,
    /// <summary>Identifies store.</summary>
Store,
    /// <summary>Identifies native.</summary>
Native,
    /// <summary>Identifies module.</summary>
Module,
    /// <summary>Identifies host.</summary>
Host,
    /// <summary>Identifies advisory.</summary>
Advisory
}

/// <summary>Defines schema Source Kind.</summary>
public enum SchemaSourceKind
{
    /// <summary>Identifies runtime.</summary>
Runtime,
    /// <summary>Identifies store.</summary>
Store,
    /// <summary>Identifies module.</summary>
Module,
    /// <summary>Identifies generated.</summary>
Generated,
    /// <summary>Identifies imported.</summary>
Imported,
    /// <summary>Identifies native.</summary>
Native,
    /// <summary>Identifies custom.</summary>
Custom
}
