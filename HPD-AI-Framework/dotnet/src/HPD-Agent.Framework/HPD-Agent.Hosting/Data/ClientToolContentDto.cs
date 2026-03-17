namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Client tool content item.
/// </summary>
/// <param name="Type">Content type (text, image, data)</param>
/// <param name="Text">Text content (for type=text)</param>
/// <param name="Data">Binary data (for type=image or type=data)</param>
/// <param name="MediaType">MIME type (for binary data)</param>
public record ClientToolContentDto(
    string Type,
    string? Text,
    byte[]? Data,
    string? MediaType);
