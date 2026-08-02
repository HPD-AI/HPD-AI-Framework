
namespace HPD.Base;

internal sealed class DefaultBaseDescriptorContributionBuilder : IBaseDescriptorContributionBuilder
{
    private readonly List<BaseModuleDescriptor> _modules = [];
    private readonly List<ProjectionDescriptor> _projections = [];
    private readonly List<DtoContractDescriptor> _dtoContracts = [];
    private readonly List<EventTypeDescriptor> _eventTypes = [];
    private readonly List<HealthRefDescriptor> _healthRefs = [];
    private readonly List<DiagnosticRefDescriptor> _diagnosticRefs = [];
    private readonly List<FieldAnnotationDescriptor> _fieldAnnotations = [];
    private readonly List<SchemaMetadata> _schemas = [];
    private readonly List<CollectionDefinition> _collections = [];
    private readonly List<CapabilityDescriptor> _capabilities = [];
    private readonly List<HealthDescriptor> _health = [];
    private readonly List<DiagnosticDescriptor> _diagnostics = [];

    /// <summary>Gets the modules.</summary>
    public BaseModuleDescriptor[] Modules => _modules.ToArray();
    /// <summary>Gets the projections.</summary>
    public ProjectionDescriptor[] Projections => _projections.ToArray();
    /// <summary>Gets the DTO contracts.</summary>
    public DtoContractDescriptor[] DtoContracts => _dtoContracts.ToArray();
    /// <summary>Gets the event types.</summary>
    public EventTypeDescriptor[] EventTypes => _eventTypes.ToArray();
    /// <summary>Gets the health refs.</summary>
    public HealthRefDescriptor[] HealthRefs => _healthRefs.ToArray();
    /// <summary>Gets the diagnostic refs.</summary>
    public DiagnosticRefDescriptor[] DiagnosticRefs => _diagnosticRefs.ToArray();
    /// <summary>Gets the field annotations.</summary>
    public FieldAnnotationDescriptor[] FieldAnnotations => _fieldAnnotations.ToArray();
    /// <summary>Gets the schemas.</summary>
    public SchemaMetadata[] Schemas => _schemas.ToArray();
    /// <summary>Gets the collections.</summary>
    public CollectionDefinition[] Collections => _collections.ToArray();
    /// <summary>Gets the capabilities.</summary>
    public CapabilityDescriptor[] Capabilities => _capabilities.ToArray();
    /// <summary>Gets the health.</summary>
    public HealthDescriptor[] Health => _health.ToArray();
    /// <summary>Gets the diagnostics.</summary>
    public DiagnosticDescriptor[] Diagnostics => _diagnostics.ToArray();

    /// <summary>Executes the add module operation.</summary>
    public void AddModule(BaseModuleDescriptor module) => Add(module, _modules);
    /// <summary>Executes the add projection operation.</summary>
    public void AddProjection(ProjectionDescriptor projection) => Add(projection, _projections);
    /// <summary>Executes the add DTO contract operation.</summary>
    public void AddDtoContract(DtoContractDescriptor dtoContract) => Add(dtoContract, _dtoContracts);
    /// <summary>Executes the add event type operation.</summary>
    public void AddEventType(EventTypeDescriptor eventType) => Add(eventType, _eventTypes);
    /// <summary>Executes the add health ref operation.</summary>
    public void AddHealthRef(HealthRefDescriptor healthRef) => Add(healthRef, _healthRefs);
    /// <summary>Executes the add diagnostic ref operation.</summary>
    public void AddDiagnosticRef(DiagnosticRefDescriptor diagnosticRef) => Add(diagnosticRef, _diagnosticRefs);
    /// <summary>Executes the add field annotation operation.</summary>
    public void AddFieldAnnotation(FieldAnnotationDescriptor fieldAnnotation) => Add(fieldAnnotation, _fieldAnnotations);
    /// <summary>Executes the add schema operation.</summary>
    public void AddSchema(SchemaMetadata schema) => Add(schema, _schemas);
    /// <summary>Executes the add collection operation.</summary>
    public void AddCollection(CollectionDefinition collection) => Add(collection, _collections);
    /// <summary>Executes the add capabilities operation.</summary>
    public void AddCapabilities(CapabilityDescriptor capabilities) => Add(capabilities, _capabilities);
    /// <summary>Executes the add health operation.</summary>
    public void AddHealth(HealthDescriptor health) => Add(health, _health);
    /// <summary>Executes the add diagnostic operation.</summary>
    public void AddDiagnostic(DiagnosticDescriptor diagnostic) => Add(diagnostic, _diagnostics);

    private static void Add<T>(T value, List<T> target)
    {
        ArgumentNullException.ThrowIfNull(value);
        target.Add(value);
    }
}
