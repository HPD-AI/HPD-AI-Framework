using HPD.Base.Runtime.Descriptors;

namespace HPD.Base.Runtime.Capabilities;

public interface IBaseCapabilityValidator
{
    BaseRuntimeValidationResult ValidateCapabilities(BaseDescriptorSnapshot snapshot);
}
