
namespace HPD.Base;

/// <summary>Defines the ibase capability validator contract.</summary>
public interface IBaseCapabilityValidator
{
    /// <summary>Executes the validate capabilities operation.</summary>
    BaseRuntimeValidationResult ValidateCapabilities(BaseDescriptorSnapshot snapshot);
}
