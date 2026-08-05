
namespace HPD.Base;

/// <summary>Defines the ihpdbase runtime contract.</summary>
public interface IHPDBaseRuntime
{
    /// <summary>Gets the descriptors.</summary>
    IBaseDescriptorProvider Descriptors { get; }
    /// <summary>Gets the schema.</summary>
    IBaseSchemaProvider Schema { get; }
    /// <summary>Gets the capabilities.</summary>
    IBaseCapabilityProvider Capabilities { get; }
    /// <summary>Gets the records.</summary>
    IBaseRecordRuntime Records { get; }
    /// <summary>Gets the health.</summary>
    IBaseHealthProvider Health { get; }
    /// <summary>Gets the diagnostics.</summary>
    IBaseDiagnosticProvider Diagnostics { get; }
    /// <summary>Gets the JSON.</summary>
    IBaseJsonOptionsProvider Json { get; }

    /// <summary>Executes the validate async operation.</summary>
    ValueTask<BaseRuntimeValidationResult> ValidateAsync(
        CancellationToken cancellationToken = default);
}
