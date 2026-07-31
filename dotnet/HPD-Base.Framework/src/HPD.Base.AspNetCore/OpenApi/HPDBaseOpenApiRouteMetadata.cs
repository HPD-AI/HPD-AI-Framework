namespace HPD.Base.AspNetCore;

internal sealed record HPDBaseOpenApiRouteMetadata(
    string OperationId,
    bool IsAdmin,
    bool IsRecord,
    string Summary,
    string Description,
    string[] Tags,
    string RouteVisibility,
    string AuthRequirement,
    string? RequestDtoId,
    string ResponseDtoId,
    string ErrorDtoId,
    string[] RequiredFeatureIds);
