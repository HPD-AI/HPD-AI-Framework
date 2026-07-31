
namespace HPD.Base;

public interface IHPDBaseRuntime
{
    IBaseDescriptorProvider Descriptors { get; }
    IBaseSchemaProvider Schema { get; }
    IBaseCapabilityProvider Capabilities { get; }
    IBaseRecordRuntime Records { get; }
    IBaseHealthProvider Health { get; }
    IBaseDiagnosticProvider Diagnostics { get; }
    IBaseJsonOptionsProvider Json { get; }

    ValueTask<BaseRuntimeValidationResult> ValidateAsync(
        CancellationToken cancellationToken = default);
}
