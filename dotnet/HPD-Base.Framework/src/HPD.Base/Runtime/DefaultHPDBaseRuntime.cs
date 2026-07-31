
namespace HPD.Base;

internal sealed class DefaultHPDBaseRuntime : IHPDBaseRuntime
{
    private readonly IBaseDescriptorRegistry _descriptorRegistry;

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

    public IBaseDescriptorProvider Descriptors { get; }
    public IBaseSchemaProvider Schema { get; }
    public IBaseCapabilityProvider Capabilities { get; }
    public IBaseRecordRuntime Records { get; }
    public IBaseHealthProvider Health { get; }
    public IBaseDiagnosticProvider Diagnostics { get; }
    public IBaseJsonOptionsProvider Json { get; }

    public ValueTask<BaseRuntimeValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return HPDBaseRuntimeTelemetry.TraceRuntimeValidationAsync(() =>
            ValueTask.FromResult(_descriptorRegistry.Current.Validation));
    }
}
