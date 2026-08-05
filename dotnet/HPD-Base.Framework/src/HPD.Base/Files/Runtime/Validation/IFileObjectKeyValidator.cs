
namespace HPD.Base;

/// <summary>Defines the ifile object key validator contract.</summary>
public interface IFileObjectKeyValidator
{
    /// <summary>Executes the normalize operation.</summary>
    OperationResult<FileObjectKey> Normalize(string? key);
}
