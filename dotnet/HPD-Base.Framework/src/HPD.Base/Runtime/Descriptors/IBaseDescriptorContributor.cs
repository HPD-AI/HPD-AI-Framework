
namespace HPD.Base;

public interface IBaseDescriptorContributor
{
    string Id { get; }
    void Contribute(IBaseDescriptorContributionBuilder builder);
}

public interface IBaseDescriptorContributionBuilder
{
    void AddModule(BaseModuleDescriptor module);
    void AddProjection(ProjectionDescriptor projection);
    void AddDtoContract(DtoContractDescriptor dtoContract);
    void AddEventType(EventTypeDescriptor eventType);
    void AddHealthRef(HealthRefDescriptor healthRef);
    void AddDiagnosticRef(DiagnosticRefDescriptor diagnosticRef);
    void AddFieldAnnotation(FieldAnnotationDescriptor fieldAnnotation);
    void AddSchema(SchemaMetadata schema);
    void AddCollection(CollectionDefinition collection);
    void AddCapabilities(CapabilityDescriptor capabilities);
    void AddHealth(HealthDescriptor health);
    void AddDiagnostic(DiagnosticDescriptor diagnostic);
}
