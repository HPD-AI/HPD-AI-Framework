namespace HPD.Base;

/// <summary>Defines the ibase descriptor validator contract.</summary>
public interface IBaseDescriptorValidator
{
    /// <summary>Executes the validate operation.</summary>
    BaseRuntimeValidationResult Validate(BaseDescriptorSnapshot snapshot);
}
