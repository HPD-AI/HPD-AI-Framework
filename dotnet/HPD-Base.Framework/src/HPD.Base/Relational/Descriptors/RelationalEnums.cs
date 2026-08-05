namespace HPD.Base;

/// <summary>Defines the relational object kind contract.</summary>
public enum RelationalObjectKind { /// <summary>Identifies database.</summary>
Database, /// <summary>Identifies catalog.</summary>
Catalog, /// <summary>Identifies schema.</summary>
Schema, /// <summary>Identifies table.</summary>
Table, /// <summary>Identifies view.</summary>
View, /// <summary>Identifies column.</summary>
Column, /// <summary>Identifies constraint.</summary>
Constraint, /// <summary>Identifies index.</summary>
Index, /// <summary>Identifies mapping.</summary>
Mapping, /// <summary>Identifies policy plan.</summary>
PolicyPlan }
/// <summary>Defines the relational namespace kind contract.</summary>
public enum RelationalNamespaceKind { /// <summary>Identifies database.</summary>
Database, /// <summary>Identifies catalog.</summary>
Catalog, /// <summary>Identifies schema.</summary>
Schema, /// <summary>Identifies attached database.</summary>
AttachedDatabase, /// <summary>Identifies provider namespace.</summary>
ProviderNamespace }
/// <summary>Defines the relational table kind contract.</summary>
public enum RelationalTableKind { /// <summary>Identifies table.</summary>
Table, /// <summary>Identifies temporary.</summary>
Temporary, /// <summary>Identifies partitioned.</summary>
Partitioned, /// <summary>Identifies external.</summary>
External, /// <summary>Identifies virtual.</summary>
Virtual, /// <summary>Identifies provider native.</summary>
ProviderNative }
/// <summary>Defines the relational view kind contract.</summary>
public enum RelationalViewKind { /// <summary>Identifies normal.</summary>
Normal, /// <summary>Identifies materialized.</summary>
Materialized, /// <summary>Identifies virtual.</summary>
Virtual, /// <summary>Identifies external.</summary>
External, /// <summary>Identifies provider native.</summary>
ProviderNative }
/// <summary>Defines the relational view materialization kind contract.</summary>
public enum RelationalViewMaterializationKind { /// <summary>Identifies none.</summary>
None, /// <summary>Identifies materialized.</summary>
Materialized, /// <summary>Identifies incremental.</summary>
Incremental, /// <summary>Identifies provider native.</summary>
ProviderNative, /// <summary>Identifies unknown.</summary>
Unknown }
/// <summary>Defines the relational column type family contract.</summary>
public enum RelationalColumnTypeFamily { /// <summary>Identifies text.</summary>
Text, /// <summary>Identifies integer.</summary>
Integer, /// <summary>Identifies decimal.</summary>
Decimal, /// <summary>Identifies floating point.</summary>
FloatingPoint, /// <summary>Identifies boolean.</summary>
Boolean, /// <summary>Identifies date time.</summary>
DateTime, /// <summary>Identifies date.</summary>
Date, /// <summary>Identifies time.</summary>
Time, /// <summary>Identifies binary.</summary>
Binary, /// <summary>Identifies JSON.</summary>
Json, /// <summary>Identifies uuid.</summary>
Uuid, /// <summary>Identifies enum.</summary>
Enum, /// <summary>Identifies array.</summary>
Array, /// <summary>Identifies vector.</summary>
Vector, /// <summary>Identifies provider native.</summary>
ProviderNative, /// <summary>Identifies unknown.</summary>
Unknown }
/// <summary>Defines the relational generated column kind contract.</summary>
public enum RelationalGeneratedColumnKind { /// <summary>Identifies none.</summary>
None, /// <summary>Identifies identity.</summary>
Identity, /// <summary>Identifies computed.</summary>
Computed, /// <summary>Identifies stored computed.</summary>
StoredComputed, /// <summary>Identifies virtual computed.</summary>
VirtualComputed, /// <summary>Identifies default expression.</summary>
DefaultExpression, /// <summary>Identifies trigger populated.</summary>
TriggerPopulated, /// <summary>Identifies provider native.</summary>
ProviderNative }
/// <summary>Defines the relational JSON storage kind contract.</summary>
public enum RelationalJsonStorageKind { /// <summary>Identifies none.</summary>
None, /// <summary>Identifies text JSON.</summary>
TextJson, /// <summary>Identifies binary JSON.</summary>
BinaryJson, /// <summary>Identifies jsonb.</summary>
Jsonb, /// <summary>Identifies provider native.</summary>
ProviderNative }
/// <summary>Defines the relational mapping kind contract.</summary>
public enum RelationalMappingKind { /// <summary>Identifies table.</summary>
Table, /// <summary>Identifies view.</summary>
View, /// <summary>Identifies query projection.</summary>
QueryProjection, /// <summary>Identifies hybrid.</summary>
Hybrid, /// <summary>Identifies provider native.</summary>
ProviderNative }
/// <summary>Defines the relational payload mapping kind contract.</summary>
public enum RelationalPayloadMappingKind { /// <summary>Identifies columns.</summary>
Columns, /// <summary>Identifies JSON column.</summary>
JsonColumn, /// <summary>Identifies hybrid.</summary>
Hybrid, /// <summary>Identifies native projection.</summary>
NativeProjection }
/// <summary>Defines the relational record ID mapping kind contract.</summary>
public enum RelationalRecordIdMappingKind { /// <summary>Identifies native primary key.</summary>
NativePrimaryKey, /// <summary>Identifies composite key.</summary>
CompositeKey, /// <summary>Identifies synthetic.</summary>
Synthetic, /// <summary>Identifies runtime derived.</summary>
RuntimeDerived, /// <summary>Identifies provider specific.</summary>
ProviderSpecific, /// <summary>Identifies partial.</summary>
Partial, /// <summary>Identifies keyless unavailable.</summary>
KeylessUnavailable }
/// <summary>Defines the relational constraint enforcement kind contract.</summary>
public enum RelationalConstraintEnforcementKind { /// <summary>Identifies native.</summary>
Native, /// <summary>Identifies store.</summary>
Store, /// <summary>Identifies runtime.</summary>
Runtime, /// <summary>Identifies advisory.</summary>
Advisory, /// <summary>Identifies disabled.</summary>
Disabled, /// <summary>Identifies unknown.</summary>
Unknown }
/// <summary>Defines the relational column write behavior contract.</summary>
public enum RelationalColumnWriteBehavior { /// <summary>Identifies writable.</summary>
Writable, /// <summary>Identifies read only.</summary>
ReadOnly, /// <summary>Identifies store generated.</summary>
StoreGenerated, /// <summary>Identifies runtime generated.</summary>
RuntimeGenerated, /// <summary>Identifies ignored.</summary>
Ignored, /// <summary>Identifies unknown.</summary>
Unknown }
/// <summary>Defines the relational field conversion kind contract.</summary>
public enum RelationalFieldConversionKind { /// <summary>Identifies none.</summary>
None, /// <summary>Identifies base type conversion.</summary>
BaseTypeConversion, /// <summary>Identifies JSON serialization.</summary>
JsonSerialization, /// <summary>Identifies provider native.</summary>
ProviderNative, /// <summary>Identifies lossy.</summary>
Lossy, /// <summary>Identifies unknown.</summary>
Unknown }
