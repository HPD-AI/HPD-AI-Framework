using HPD.Base.AspNetCore;

namespace HPD.Base.AspNetCore;

public sealed record HPDBaseFilesOpenApiMetadata(
    string OperationId,
    string Summary,
    string Description,
    string[] Tags,
    string[] RequiredFeatureIds) : IHPDBaseModuleOpenApiMetadata;
