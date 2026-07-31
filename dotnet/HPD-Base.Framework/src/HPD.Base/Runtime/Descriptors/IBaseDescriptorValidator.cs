namespace HPD.Base;

public interface IBaseDescriptorValidator
{
    BaseRuntimeValidationResult Validate(BaseDescriptorSnapshot snapshot);
}
