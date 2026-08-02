using System.Text.Json;

namespace HPD.Base;

/// <summary>Defines the ibase JSON options provider contract.</summary>
public interface IBaseJsonOptionsProvider
{
    /// <summary>Gets the options.</summary>
    JsonSerializerOptions Options { get; }
}
