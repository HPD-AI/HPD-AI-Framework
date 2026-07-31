
namespace HPD.Base;

public interface IFileObjectKeyValidator
{
    OperationResult<FileObjectKey> Normalize(string? key);
}
