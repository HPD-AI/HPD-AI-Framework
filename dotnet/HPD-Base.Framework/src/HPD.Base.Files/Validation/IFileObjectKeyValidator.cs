using HPD.Base.Files.Objects;
using HPD.Base.Results;

namespace HPD.Base.Files.Validation;

public interface IFileObjectKeyValidator
{
    OperationResult<FileObjectKey> Normalize(string? key);
}
