namespace HPD.Base.Runtime.Descriptors;

public interface IBaseDescriptorValidator
{
    BaseRuntimeValidationResult Validate(BaseDescriptorSnapshot snapshot);
}
