namespace HPD.Base.Relational.Descriptors;

public enum RelationalObjectKind { Database, Catalog, Schema, Table, View, Column, Constraint, Index, Mapping, PolicyPlan }
public enum RelationalNamespaceKind { Database, Catalog, Schema, AttachedDatabase, ProviderNamespace }
public enum RelationalTableKind { Table, Temporary, Partitioned, External, Virtual, ProviderNative }
public enum RelationalViewKind { Normal, Materialized, Virtual, External, ProviderNative }
public enum RelationalViewMaterializationKind { None, Materialized, Incremental, ProviderNative, Unknown }
public enum RelationalColumnTypeFamily { Text, Integer, Decimal, FloatingPoint, Boolean, DateTime, Date, Time, Binary, Json, Uuid, Enum, Array, Vector, ProviderNative, Unknown }
public enum RelationalGeneratedColumnKind { None, Identity, Computed, StoredComputed, VirtualComputed, DefaultExpression, TriggerPopulated, ProviderNative }
public enum RelationalJsonStorageKind { None, TextJson, BinaryJson, Jsonb, ProviderNative }
public enum RelationalMappingKind { Table, View, QueryProjection, Hybrid, ProviderNative }
public enum RelationalPayloadMappingKind { Columns, JsonColumn, Hybrid, NativeProjection }
public enum RelationalRecordIdMappingKind { NativePrimaryKey, CompositeKey, Synthetic, RuntimeDerived, ProviderSpecific, Partial, KeylessUnavailable }
public enum RelationalConstraintEnforcementKind { Native, Store, Runtime, Advisory, Disabled, Unknown }
public enum RelationalColumnWriteBehavior { Writable, ReadOnly, StoreGenerated, RuntimeGenerated, Ignored, Unknown }
public enum RelationalFieldConversionKind { None, BaseTypeConversion, JsonSerialization, ProviderNative, Lossy, Unknown }
