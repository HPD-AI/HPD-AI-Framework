
namespace HPD.Base;

internal sealed class DefaultHPDBaseRuntime : IHPDBaseRuntime
{
    private readonly IBaseDescriptorRegistry _descriptorRegistry;

    /// <summary>Initializes a new instance.</summary>
    public DefaultHPDBaseRuntime(
        IBaseDescriptorProvider descriptors,
        IBaseSchemaProvider schema,
        IBaseCapabilityProvider capabilities,
        IBaseRecordRuntime records,
        IBaseHealthProvider health,
        IBaseDiagnosticProvider diagnostics,
        IBaseJsonOptionsProvider json,
        IBaseDescriptorRegistry descriptorRegistry)
    {
        Descriptors = descriptors;
        Schema = schema;
        Capabilities = capabilities;
        Records = records;
        Health = health;
        Diagnostics = diagnostics;
        Json = json;
        _descriptorRegistry = descriptorRegistry;
    }

    /// <summary>Gets the descriptors.</summary>
    public IBaseDescriptorProvider Descriptors { get; }
    /// <summary>Gets the schema.</summary>
    public IBaseSchemaProvider Schema { get; }
    /// <summary>Gets the capabilities.</summary>
    public IBaseCapabilityProvider Capabilities { get; }
    /// <summary>Gets the records.</summary>
    public IBaseRecordRuntime Records { get; }
    /// <summary>Gets the health.</summary>
    public IBaseHealthProvider Health { get; }
    /// <summary>Gets the diagnostics.</summary>
    public IBaseDiagnosticProvider Diagnostics { get; }
    /// <summary>Gets the JSON.</summary>
    public IBaseJsonOptionsProvider Json { get; }

    /// <summary>Executes the validate async operation.</summary>
    public ValueTask<BaseRuntimeValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return HPDBaseRuntimeTelemetry.TraceRuntimeValidationAsync(() =>
            ValueTask.FromResult(_descriptorRegistry.Current.Validation));
    }
}
