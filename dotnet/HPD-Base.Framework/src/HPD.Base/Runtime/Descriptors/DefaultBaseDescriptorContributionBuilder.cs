
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

    public BaseModuleDescriptor[] Modules => _modules.ToArray();
    public ProjectionDescriptor[] Projections => _projections.ToArray();
    public DtoContractDescriptor[] DtoContracts => _dtoContracts.ToArray();
    public EventTypeDescriptor[] EventTypes => _eventTypes.ToArray();
    public HealthRefDescriptor[] HealthRefs => _healthRefs.ToArray();
    public DiagnosticRefDescriptor[] DiagnosticRefs => _diagnosticRefs.ToArray();
    public FieldAnnotationDescriptor[] FieldAnnotations => _fieldAnnotations.ToArray();
    public SchemaMetadata[] Schemas => _schemas.ToArray();
    public CollectionDefinition[] Collections => _collections.ToArray();
    public CapabilityDescriptor[] Capabilities => _capabilities.ToArray();
    public HealthDescriptor[] Health => _health.ToArray();
    public DiagnosticDescriptor[] Diagnostics => _diagnostics.ToArray();

    public void AddModule(BaseModuleDescriptor module) => Add(module, _modules);
    public void AddProjection(ProjectionDescriptor projection) => Add(projection, _projections);
    public void AddDtoContract(DtoContractDescriptor dtoContract) => Add(dtoContract, _dtoContracts);
    public void AddEventType(EventTypeDescriptor eventType) => Add(eventType, _eventTypes);
    public void AddHealthRef(HealthRefDescriptor healthRef) => Add(healthRef, _healthRefs);
    public void AddDiagnosticRef(DiagnosticRefDescriptor diagnosticRef) => Add(diagnosticRef, _diagnosticRefs);
    public void AddFieldAnnotation(FieldAnnotationDescriptor fieldAnnotation) => Add(fieldAnnotation, _fieldAnnotations);
    public void AddSchema(SchemaMetadata schema) => Add(schema, _schemas);
    public void AddCollection(CollectionDefinition collection) => Add(collection, _collections);
    public void AddCapabilities(CapabilityDescriptor capabilities) => Add(capabilities, _capabilities);
    public void AddHealth(HealthDescriptor health) => Add(health, _health);
    public void AddDiagnostic(DiagnosticDescriptor diagnostic) => Add(diagnostic, _diagnostics);

    private static void Add<T>(T value, List<T> target)
    {
        ArgumentNullException.ThrowIfNull(value);
        target.Add(value);
    }
}
