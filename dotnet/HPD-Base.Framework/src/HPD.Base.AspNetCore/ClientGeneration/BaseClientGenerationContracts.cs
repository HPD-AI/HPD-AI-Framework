namespace HPD.Base.AspNetCore;

/// <summary>Contains the complete immutable version 2 client-generation snapshot.</summary>
public sealed record BaseClientGenerationSnapshotV2
{
    /// <summary>Gets the protocol contract.</summary>
    public required BaseClientProtocolDescriptor Protocol { get; init; }
    /// <summary>Gets the installed application contract.</summary>
    public required BaseClientApplicationDescriptor Application { get; init; }
    /// <summary>Gets the installed logical schema.</summary>
    public required BaseClientSchemaDescriptor Schema { get; init; }
    /// <summary>Gets the materialized endpoint inventory.</summary>
    public required BaseClientEndpointDescriptor[] Endpoints { get; init; }
    /// <summary>Gets the exact installed capabilities.</summary>
    public required BaseClientCapabilityDescriptor[] Capabilities { get; init; }
    /// <summary>Gets the registered relational reads.</summary>
    public required BaseClientReadDescriptor[] RegisteredReads { get; init; }
    /// <summary>Gets the public dependency templates.</summary>
    public required BaseClientDependencyTemplateDescriptor[] DependencyTemplates { get; init; }
    /// <summary>Gets installed vector indexes.</summary>
    public required BaseClientVectorIndexDescriptor[] VectorIndexes { get; init; }
    /// <summary>Gets projected transaction-bound selection mutations.</summary>
    public required BaseClientSelectionMutationDescriptor[] SelectionMutations { get; init; }
    /// <summary>Gets the stable error taxonomy.</summary>
    public required BaseClientErrorDescriptor[] Errors { get; init; }
    /// <summary>Gets the canonical structural SHA-256 digest.</summary>
    public required string Digest { get; init; }
}

/// <summary>Describes one generated transaction-bound selection mutation.</summary>
public sealed record BaseClientSelectionMutationDescriptor
{
    /// <summary>Gets the request graph type identifier.</summary>
    public required string RequestTypeId { get; init; }
    /// <summary>Gets the result graph type identifier.</summary>
    public required string ResultTypeId { get; init; }
    /// <summary>Gets the stable profile identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the semantic profile version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the finalized semantic checksum.</summary>
    public required string Checksum { get; init; }
    /// <summary>Gets the owning collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the deterministic generated method name.</summary>
    public required string GeneratedName { get; init; }
    /// <summary>Gets mergePatch or delete.</summary>
    public required string MutationKind { get; init; }
    /// <summary>Gets the materialized endpoint identifier.</summary>
    public required string EndpointId { get; init; }
    /// <summary>Gets the concrete route.</summary>
    public required string Route { get; init; }
    /// <summary>Gets the maximum selected record count.</summary>
    public required int MaximumSelectedRecords { get; init; }
    /// <summary>Gets the maximum request body bytes.</summary>
    public required int MaximumRequestBodyBytes { get; init; }
}

/// <summary>Describes protocol compatibility for one snapshot.</summary>
public sealed record BaseClientProtocolDescriptor
{
    /// <summary>Gets the protocol major version.</summary>
    public int ProtocolMajor { get; init; } = 2;
    /// <summary>Gets the protocol minor version.</summary>
    public int ProtocolMinor { get; init; } = 1;
    /// <summary>Gets the minimum compatible client minor.</summary>
    public int MinimumClientMinor { get; init; }
    /// <summary>Gets the snapshot schema version.</summary>
    public int SnapshotSchemaVersion { get; init; } = 3;
    /// <summary>Gets the application identifier.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the logical schema generation.</summary>
    public required string SchemaGeneration { get; init; }
    /// <summary>Gets the materialized endpoint inventory digest.</summary>
    public required string EndpointInventoryDigest { get; init; }
    /// <summary>Gets the stable error taxonomy version.</summary>
    public int ErrorTaxonomyVersion { get; init; } = 1;
    /// <summary>Gets the realtime protocol version.</summary>
    public int RealtimeProtocolVersion { get; init; } = 2;
    /// <summary>Gets the live-query protocol version.</summary>
    public int LiveQueryProtocolVersion { get; init; } = 1;
    /// <summary>Gets the serialization profile.</summary>
    public string SerializationProfile { get; init; } = "base-json-v1";
    /// <summary>Gets the informational generation timestamp.</summary>
    public required string GeneratedAt { get; init; }
}

/// <summary>Describes one generated client artifact.</summary>
public sealed record BaseClientApplicationDescriptor
{
    /// <summary>Gets the application identifier.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the selected endpoint audience.</summary>
    public required string Audience { get; init; }
    /// <summary>Gets the canonical BASE path.</summary>
    public required string BasePath { get; init; }
}

/// <summary>Describes the closed generated logical schema.</summary>
public sealed record BaseClientSchemaDescriptor
{
    /// <summary>Gets the schema generation.</summary>
    public required string Generation { get; init; }
    /// <summary>Gets the generated collections.</summary>
    public required BaseClientCollectionDescriptor[] Collections { get; init; }
    /// <summary>Gets the closed named type graph.</summary>
    public required BaseClientNamedTypeDescriptor[] Types { get; init; }
}

/// <summary>Describes one generated collection.</summary>
public sealed record BaseClientCollectionDescriptor
{
    /// <summary>Gets the stable collection identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the deterministic TypeScript name.</summary>
    public required string GeneratedName { get; init; }
    /// <summary>Gets the output record type identifier.</summary>
    public required string RecordTypeId { get; init; }
    /// <summary>Gets the create input type identifier.</summary>
    public required string CreateTypeId { get; init; }
    /// <summary>Gets the replace input type identifier.</summary>
    public required string ReplaceTypeId { get; init; }
    /// <summary>Gets the patch input type identifier.</summary>
    public required string PatchTypeId { get; init; }
    /// <summary>Gets generated fields.</summary>
    public required BaseClientFieldDescriptor[] Fields { get; init; }
    /// <summary>Gets operations proven usable through the installed audience.</summary>
    public required string[] Operations { get; init; }
    /// <summary>Gets the cursor guarantee.</summary>
    public required string Pagination { get; init; }
    /// <summary>Gets the maximum page size.</summary>
    public required int MaxPageSize { get; init; }
}

/// <summary>Describes one generated field.</summary>
public sealed record BaseClientFieldDescriptor
{
    /// <summary>Gets the stable field identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the exact stored JSON property name used on the wire.</summary>
    public required string WireName { get; init; }
    /// <summary>Gets the deterministic TypeScript name.</summary>
    public required string GeneratedName { get; init; }
    /// <summary>Gets the named value type identifier.</summary>
    public required string ValueTypeId { get; init; }
    /// <summary>Gets whether the value is server generated.</summary>
    public bool ServerGenerated { get; init; }
    /// <summary>Gets whether mutation input may change the field.</summary>
    public bool Mutable { get; init; }
    /// <summary>Gets the closed outward disclosure shape: none, omission, or fixed-marker.</summary>
    public required string DisclosureShape { get; init; }
    /// <summary>Gets the accepted query operators.</summary>
    public required string[] Operators { get; init; }
}

/// <summary>Describes one named node in the closed language-neutral graph.</summary>
public sealed record BaseClientNamedTypeDescriptor
{
    /// <summary>Gets the stable DTO or value type identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the closed node.</summary>
    public required BaseClientTypeNode Node { get; init; }
}

/// <summary>Describes one closed language-neutral type node.</summary>
public sealed record BaseClientTypeNode
{
    /// <summary>Gets the closed node kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets the optional string format.</summary>
    public string? Format { get; init; }
    /// <summary>Gets the optional floating precision.</summary>
    public string? Precision { get; init; }
    /// <summary>Gets whether floating values must be finite.</summary>
    public bool? FiniteOnly { get; init; }
    /// <summary>Gets an optional exact integral lower bound encoded as decimal text.</summary>
    public string? Minimum { get; init; }
    /// <summary>Gets an optional exact integral upper bound encoded as decimal text.</summary>
    public string? Maximum { get; init; }
    /// <summary>Gets the closed wire representation.</summary>
    public string? Wire { get; init; }
    /// <summary>Gets a literal value for a literal node.</summary>
    public object? Value { get; init; }
    /// <summary>Gets the closed values for an enum node.</summary>
    public string[]? Values { get; init; }
    /// <summary>Gets the referenced element type for an array node.</summary>
    public string? ElementTypeId { get; init; }
    /// <summary>Gets the maximum byte count for a bytes node.</summary>
    public int? MaxBytes { get; init; }
    /// <summary>Gets the maximum array item count.</summary>
    public int? MaxItems { get; init; }
    /// <summary>Gets the minimum array item count.</summary>
    public int? MinItems { get; init; }
    /// <summary>Gets the required discriminator for a union node.</summary>
    public string? Discriminator { get; init; }
    /// <summary>Gets the closed union variants.</summary>
    public BaseClientUnionVariantDescriptor[]? Variants { get; init; }
    /// <summary>Gets the optional minimum string length.</summary>
    public int? MinLength { get; init; }
    /// <summary>Gets the optional maximum length or item count.</summary>
    public int? MaxLength { get; init; }
    /// <summary>Gets the maximum selection-query node count.</summary>
    public int? MaximumNodes { get; init; }
    /// <summary>Gets the maximum selection-query depth.</summary>
    public int? MaximumDepth { get; init; }
    /// <summary>Gets the maximum selection-query literal count.</summary>
    public int? MaximumLiterals { get; init; }
    /// <summary>Gets the maximum selection-query take.</summary>
    public int? MaximumTake { get; init; }
    /// <summary>Gets the maximum previous-state field requirement count.</summary>
    public int? MaximumFields { get; init; }
    /// <summary>Gets the application patch graph type wrapped by a selection patch node.</summary>
    public string? PatchTypeId { get; init; }
    /// <summary>Gets the exported logical-subject contract identifier.</summary>
    public string? ContractId { get; init; }
    /// <summary>Gets the exported logical-subject contract version.</summary>
    public int? ContractVersion { get; init; }
    /// <summary>Gets the canonical subject-identifier grammar.</summary>
    public string? SubjectIdKind { get; init; }
    /// <summary>Gets the maximum canonical UTF-8 subject-identifier byte count.</summary>
    public int? MaximumSubjectIdUtf8Bytes { get; init; }
    /// <summary>Gets the fixed authority-epoch byte count.</summary>
    public int? AuthorityEpochBytes { get; init; }
    /// <summary>Gets the fixed incarnation byte count.</summary>
    public int? IncarnationBytes { get; init; }
    /// <summary>Gets object properties.</summary>
    public BaseClientPropertyDescriptor[]? Properties { get; init; }
    /// <summary>Gets whether unknown properties are accepted.</summary>
    public bool? AdditionalProperties { get; init; }
}

/// <summary>Describes one closed discriminated-union variant.</summary>
public sealed record BaseClientUnionVariantDescriptor
{
    /// <summary>Gets the discriminator literal.</summary>
    public required string Tag { get; init; }
    /// <summary>Gets the referenced object type.</summary>
    public required string TypeId { get; init; }
}

/// <summary>Describes one exact object property.</summary>
public sealed record BaseClientPropertyDescriptor
{
    /// <summary>Gets the deterministic generated application property name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the serialized wire property name.</summary>
    public required string WireName { get; init; }
    /// <summary>Gets the referenced named type.</summary>
    public required string TypeId { get; init; }
    /// <summary>Gets whether the property must be present.</summary>
    public bool Required { get; init; }
    /// <summary>Gets whether a present property may be null.</summary>
    public bool Nullable { get; init; }
    /// <summary>Gets the closed outward disclosure shape: none, omission, or fixed-marker.</summary>
    public required string DisclosureShape { get; init; }
}

/// <summary>Describes one materialized endpoint and its DTO contracts.</summary>
public sealed record BaseClientEndpointDescriptor
{
    /// <summary>Gets the exact endpoint ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the HTTP method.</summary>
    public required string Method { get; init; }
    /// <summary>Gets the canonical route template.</summary>
    public required string Route { get; init; }
    /// <summary>Gets the exact audience.</summary>
    public required string Audience { get; init; }
    /// <summary>Gets the closed operation.</summary>
    public required string Operation { get; init; }
    /// <summary>Gets the required capability.</summary>
    public string? Capability { get; init; }
    /// <summary>Gets the request DTO ID, when present.</summary>
    public string? RequestTypeId { get; init; }
    /// <summary>Gets the response DTO ID, when present.</summary>
    public string? ResponseTypeId { get; init; }
    /// <summary>Gets accepted successful statuses.</summary>
    public required int[] SuccessStatuses { get; init; }
    /// <summary>Gets the closed stable errors that may be returned by the endpoint.</summary>
    public required string[] ErrorCodes { get; init; }
    /// <summary>Gets the maximum accepted request-body bytes, or zero when the endpoint has no body.</summary>
    public required long MaximumRequestBodyBytes { get; init; }
    /// <summary>Gets the bounded response mode.</summary>
    public required string ResponseMode { get; init; }
    /// <summary>Gets the delivery replay semantics when relevant.</summary>
    public required string Replay { get; init; }
    /// <summary>Gets the delivery resume semantics when relevant.</summary>
    public required string Resume { get; init; }
    /// <summary>Gets the client cache semantics.</summary>
    public required string Cache { get; init; }
}

/// <summary>Describes one installed capability.</summary>
public sealed record BaseClientCapabilityDescriptor
{
    /// <summary>Gets the stable capability ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets whether it is available.</summary>
    public required bool Available { get; init; }
}

/// <summary>Describes one generated registered read.</summary>
public sealed record BaseClientReadDescriptor
{
    /// <summary>Gets the stable read ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the deterministic generated name.</summary>
    public required string GeneratedName { get; init; }
    /// <summary>Gets the materialized endpoint ID.</summary>
    public required string EndpointId { get; init; }
    /// <summary>Gets the parameter DTO ID.</summary>
    public required string ParameterTypeId { get; init; }
    /// <summary>Gets the row DTO ID.</summary>
    public required string RowTypeId { get; init; }
    /// <summary>Gets the maximum page size.</summary>
    public required int MaxPageSize { get; init; }
    /// <summary>Gets whether a complete live replacement is supported.</summary>
    public required bool Watchable { get; init; }
}

/// <summary>Describes one safe dependency template.</summary>
public sealed record BaseClientDependencyTemplateDescriptor
{
    /// <summary>Gets the opaque template ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the closed dependency kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets the exposure.</summary>
    public required string Visibility { get; init; }
    /// <summary>Gets parameter type IDs.</summary>
    public required string[] ParameterTypeIds { get; init; }
}

/// <summary>Describes one policy-safe vector index.</summary>
public sealed record BaseClientVectorIndexDescriptor
{
    /// <summary>Gets the owning collection.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the stable index ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the deterministic generated name.</summary>
    public required string GeneratedName { get; init; }
    /// <summary>Gets the exact dimensions.</summary>
    public required int Dimensions { get; init; }
    /// <summary>Gets the closed measure.</summary>
    public required string Measure { get; init; }
    /// <summary>Gets policy-filterable field IDs.</summary>
    public required string[] FilterFieldIds { get; init; }
}

/// <summary>Describes one stable server error.</summary>
public sealed record BaseClientErrorDescriptor
{
    /// <summary>Gets the stable code.</summary>
    public required string Code { get; init; }
    /// <summary>Gets its safe category.</summary>
    public required string Category { get; init; }
    /// <summary>Gets whether exact retry may succeed.</summary>
    public required bool Retryable { get; init; }
}
