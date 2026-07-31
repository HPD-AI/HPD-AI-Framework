namespace HPD.Base.AspNetCore;

public interface IHPDBaseModuleOpenApiMetadata
{
    string OperationId { get; }
    string Summary { get; }
    string Description { get; }
    string[] Tags { get; }
    string[] RequiredFeatureIds { get; }
}
