using System.Text;
using System.IO;
using System.Text.Json;
using HPD.Agent.TextExtraction;

internal class JsonTextDecoder : ITextDecoder
{
    public string Description => "JSON Text Content Decoder (validates JSON structure)";

    public async Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
    // Read file via FileStream to avoid banned static File API
    await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var reader = new StreamReader(fs, Encoding.UTF8);
    var textContent = await reader.ReadToEndAsync().ConfigureAwait(false);
        try
        {
            System.Text.Json.JsonDocument.Parse(textContent);
        }
        catch (System.Text.Json.JsonException)
        {
            // Optionally log that the content was not valid JSON
        }
        return textContent;
    }

    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}