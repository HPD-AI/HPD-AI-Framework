
namespace HPD.Base;

public sealed class BaseDescriptorSnapshot
{
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

    public BaseManifest Manifest { get; }
    public SchemaMetadata Schema { get; }
    public CapabilityDescriptor Capabilities { get; }
    public HealthDescriptor[] Health { get; }
    public DiagnosticDescriptor[] Diagnostics { get; }
    public BaseRuntimeValidationResult Validation { get; }
}
