using HPD.Base.Runtime.Capabilities;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Health;
using HPD.Base.Runtime.Operations;
using HPD.Base.Runtime.Schema;
using HPD.Base.Runtime.Serialization;

namespace HPD.Base.Runtime;

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
