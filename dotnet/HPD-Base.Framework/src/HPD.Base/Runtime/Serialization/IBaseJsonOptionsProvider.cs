using System.Text.Json;

namespace HPD.Base.Runtime.Serialization;

public interface IBaseJsonOptionsProvider
{
    JsonSerializerOptions Options { get; }
}
