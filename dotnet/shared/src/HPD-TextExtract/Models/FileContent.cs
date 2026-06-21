using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.TextExtract.Models
{
    public class FileContent
    {
        [JsonPropertyOrder(0)]
        [JsonPropertyName("sections")]
        public List<Chunk> Sections { get; set; } = new List<Chunk>();

        [JsonPropertyOrder(1)]
        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; }

        public FileContent(string mimeType)
        {
            this.MimeType = mimeType;
        }
    }
}