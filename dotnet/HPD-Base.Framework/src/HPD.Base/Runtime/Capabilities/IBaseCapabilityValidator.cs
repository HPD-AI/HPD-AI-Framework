
namespace HPD.Base;

public interface IBaseCapabilityValidator
{
    BaseRuntimeValidationResult ValidateCapabilities(BaseDescriptorSnapshot snapshot);
}
