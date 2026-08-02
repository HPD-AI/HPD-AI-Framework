using HPD.Base.AspNetCore;

namespace HPD.Base.AspNetCore;

/// <summary>Represents a hpdbase files open API metadata.</summary>
public sealed record HPDBaseFilesOpenApiMetadata(
    string OperationId,
    string Summary,
    string Description,
    string[] Tags,
    string[] RequiredFeatureIds) : IHPDBaseModuleOpenApiMetadata;
