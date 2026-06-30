using HPD.Base.AspNetCore.OpenApi;

namespace HPD.Base.Files.AspNetCore.OpenApi;

public sealed record HPDBaseFilesOpenApiMetadata(
    string OperationId,
    string Summary,
    string Description,
    string[] Tags,
    string[] RequiredFeatureIds) : IHPDBaseModuleOpenApiMetadata;
