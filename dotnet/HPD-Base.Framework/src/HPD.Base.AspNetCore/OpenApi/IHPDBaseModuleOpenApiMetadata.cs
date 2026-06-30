namespace HPD.Base.AspNetCore.OpenApi;

public interface IHPDBaseModuleOpenApiMetadata
{
    string OperationId { get; }
    string Summary { get; }
    string Description { get; }
    string[] Tags { get; }
    string[] RequiredFeatureIds { get; }
}
