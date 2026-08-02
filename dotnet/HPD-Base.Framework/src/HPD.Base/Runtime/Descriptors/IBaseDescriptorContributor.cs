
namespace HPD.Base;

/// <summary>Defines the ibase descriptor contributor contract.</summary>
public interface IBaseDescriptorContributor
{
    /// <summary>Gets the ID.</summary>
    string Id { get; }
    /// <summary>Executes the contribute operation.</summary>
    void Contribute(IBaseDescriptorContributionBuilder builder);
}

/// <summary>Defines the ibase descriptor contribution builder contract.</summary>
public interface IBaseDescriptorContributionBuilder
{
    /// <summary>Executes the add module operation.</summary>
    void AddModule(BaseModuleDescriptor module);
    /// <summary>Executes the add projection operation.</summary>
    void AddProjection(ProjectionDescriptor projection);
    /// <summary>Executes the add DTO contract operation.</summary>
    void AddDtoContract(DtoContractDescriptor dtoContract);
    /// <summary>Executes the add event type operation.</summary>
    void AddEventType(EventTypeDescriptor eventType);
    /// <summary>Executes the add health ref operation.</summary>
    void AddHealthRef(HealthRefDescriptor healthRef);
    /// <summary>Executes the add diagnostic ref operation.</summary>
    void AddDiagnosticRef(DiagnosticRefDescriptor diagnosticRef);
    /// <summary>Executes the add field annotation operation.</summary>
    void AddFieldAnnotation(FieldAnnotationDescriptor fieldAnnotation);
    /// <summary>Executes the add schema operation.</summary>
    void AddSchema(SchemaMetadata schema);
    /// <summary>Executes the add collection operation.</summary>
    void AddCollection(CollectionDefinition collection);
    /// <summary>Executes the add capabilities operation.</summary>
    void AddCapabilities(CapabilityDescriptor capabilities);
    /// <summary>Executes the add health operation.</summary>
    void AddHealth(HealthDescriptor health);
    /// <summary>Executes the add diagnostic operation.</summary>
    void AddDiagnostic(DiagnosticDescriptor diagnostic);
}
