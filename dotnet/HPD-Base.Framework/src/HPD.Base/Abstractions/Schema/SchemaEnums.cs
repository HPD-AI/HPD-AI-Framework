namespace HPD.Base;

public enum SchemaMetadataRole { ReadProjection }
public enum SchemaMode { Strict, Loose, Inferred, Native, Hybrid }
public enum UnknownFieldPolicy { Reject, Preserve, Strip, StoreNative }
public enum ValidationMode { Runtime, Store, Native, Advisory, Disabled }
public enum FieldCardinalityKind { Single, Array, Set, Map }
public enum DefaultValueKind { None, Literal, Generated, Store, Native }
public enum GenerationKind { None, Id, Timestamp, Revision, Store, Native, Custom }
public enum ValidationRuleKind { Required, Nullable, MinLength, MaxLength, MinValue, MaxValue, Regex, Enum, MimeType, MaxBytes, MinItems, MaxItems, ObjectShape, CheckExpression, NativeCheck, Custom }
public enum ValidationSeverity { Info, Warning, Error }
public enum ValidationAppliesTo { Create, Update, Patch, Replace, Query, Admin }
public enum RelationKind { Reference, ForeignKey, Inverse, Junction, EmbeddedRef, Native }
public enum RelationCardinality { One, ZeroOrOne, Many }
public enum DeleteBehavior { None, Restrict, Cascade, SetNull, ClientOnly, Native }
public enum FileReferenceShape { Name, Id, Url, ObjectRef, ModuleRef, Custom }
public enum FileCleanupPolicy { None, Module, Store, Native, Advisory }
public enum IndexKind { Key, Unique, Primary, FullText, Vector, Geo, Native, Custom }
public enum IndexStatus { Ready, Building, Invalid, Disabled, Unknown }
public enum IndexPartKind { Field, Expression, Native, Custom }
public enum IndexSortDirection { Asc, Desc }
public enum IndexNullOrder { Unspecified, First, Last }
public enum EnforcementOwner { Runtime, Store, Native, Module, Host, Advisory }
public enum SchemaSourceKind { Runtime, Store, Module, Generated, Imported, Native, Custom }
