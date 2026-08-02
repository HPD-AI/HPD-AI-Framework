namespace HPD.Base.AspNetCore;

/// <summary>Defines the ihpdbase module open API metadata contract.</summary>
public interface IHPDBaseModuleOpenApiMetadata
{
    /// <summary>Gets the operation ID.</summary>
    string OperationId { get; }
    /// <summary>Gets the summary.</summary>
    string Summary { get; }
    /// <summary>Gets the description.</summary>
    string Description { get; }
    /// <summary>Gets the tags.</summary>
    string[] Tags { get; }
    /// <summary>Gets the required feature IDs.</summary>
    string[] RequiredFeatureIds { get; }
}
