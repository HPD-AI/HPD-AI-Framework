
namespace HPD.Base;

/// <summary>Represents a base descriptor snapshot.</summary>
public sealed class BaseDescriptorSnapshot
{
    /// <summary>Initializes a new instance.</summary>
    public BaseDescriptorSnapshot(
        BaseManifest manifest,
        SchemaMetadata schema,
        CapabilityDescriptor capabilities,
        HealthDescriptor[] health,
        DiagnosticDescriptor[] diagnostics,
        BaseRuntimeValidationResult validation)
    {
        Manifest = manifest;
        Schema = schema;
        Capabilities = capabilities;
        Health = health;
        Diagnostics = diagnostics;
        Validation = validation;
    }

    /// <summary>Gets the manifest.</summary>
    public BaseManifest Manifest { get; }
    /// <summary>Gets the schema.</summary>
    public SchemaMetadata Schema { get; }
    /// <summary>Gets the capabilities.</summary>
    public CapabilityDescriptor Capabilities { get; }
    /// <summary>Gets the health.</summary>
    public HealthDescriptor[] Health { get; }
    /// <summary>Gets the diagnostics.</summary>
    public DiagnosticDescriptor[] Diagnostics { get; }
    /// <summary>Gets the validation.</summary>
    public BaseRuntimeValidationResult Validation { get; }
}
