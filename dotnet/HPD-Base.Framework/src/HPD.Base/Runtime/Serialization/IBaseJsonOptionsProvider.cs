using System.Text.Json;

namespace HPD.Base;

public interface IBaseJsonOptionsProvider
{
    JsonSerializerOptions Options { get; }
}
