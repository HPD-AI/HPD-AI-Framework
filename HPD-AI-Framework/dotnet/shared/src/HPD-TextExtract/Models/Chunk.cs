using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace HPD.TextExtract.Models
{
    public class Chunk
    {
        // Metadata keys
        private const string MetaSentencesAreComplete = "completeSentences";
        private const string MetaPageNumber = "pageNumber";

        /// <summary>
        /// Text page number/Audio segment number/Video scene number
        /// </summary>
        [JsonPropertyOrder(0)]
        [JsonPropertyName("number")]
        public int Number { get; }

        /// <summary>
        /// Page text content
        /// </summary>
        [JsonPropertyOrder(1)]
        [JsonPropertyName("content")]
        public string Content { get; set; }

        /// <summary>
        /// Optional metadata attached to the section.
        /// Values are JSON strings to be serialized/deserialized.
        /// </summary>
        [JsonPropertyOrder(10)]
        [JsonPropertyName("metadata")]
        public Dictionary<string, object?> Metadata { get; set; }

        [JsonIgnore]
        public bool IsSeparator { get; set; }

        /// <summary>
        /// Whether the first/last sentence may continue from the previous/into
        /// the next section (e.g. like PDF docs).
        /// </summary>
        [JsonIgnore]
        public bool SentencesAreComplete
        {
            get
            {
                return this.Metadata.TryGetValue(MetaSentencesAreComplete, out var value) && value switch
                {
                    bool b => b,
                    string s => bool.Parse(s),
                    _ => false
                };
            }
        }

        [JsonIgnore]
        public int PageNumber
        {
            get
            {
                if (this.Metadata.TryGetValue(MetaPageNumber, out var value))
                {
                    return value switch
                    {
                        int i => i,
                        string s => int.Parse(s, CultureInfo.InvariantCulture),
                        _ => -1
                    };
                }

                return -1;
            }
        }

        /// <summary>
        /// Create new instance
        /// </summary>
        /// <param name="text">Text content</param>
        /// <param name="number">Position within the parent content container</param>
        public Chunk(string? text, int number)
        {
            this.Content = text ?? string.Empty;
            this.Number = number;
            this.Metadata = new Dictionary<string, object?>();
        }

        /// <summary>
        /// Create new instance
        /// </summary>
        /// <param name="text">Text content</param>
        /// <param name="number">Position within the parent content container</param>
        public Chunk(char text, int number)
        {
            this.Content = text.ToString();
            this.Number = number;
            this.Metadata = new Dictionary<string, object?>();
        }

        /// <summary>
        /// Create new instance
        /// </summary>
        /// <param name="text">Text content</param>
        /// <param name="number">Position within the parent content container</param>
        public Chunk(StringBuilder text, int number)
        {
            this.Content = text.ToString();
            this.Number = number;
            this.Metadata = new Dictionary<string, object?>();
        }

        /// <summary>
        /// Create new instance
        /// </summary>
        /// <param name="text">Text content</param>
        /// <param name="number">Position within the parent content container</param>
        /// <param name="metadata">Chunk metadata</param>
        public Chunk(string? text, int number, Dictionary<string, object?> metadata)
        {
            this.Content = text ?? string.Empty;
            this.Number = number;
            this.Metadata = metadata;
        }

        /// <summary>
        /// Metadata builder
        /// </summary>
        /// <param name="sentencesAreComplete">Whether the first/last sentence may continue from the previous/into the next section</param>
        /// <param name="pageNumber">Number of the page where the content is extracted from</param>
        public static Dictionary<string, object?> Meta(
            bool? sentencesAreComplete = null,
            int? pageNumber = null,
            BoundingBox? boundingBox = null,
            float? confidence = null,
            TextExtractionSource? source = null)
        {
            var result = new Dictionary<string, object?>();

            if (sentencesAreComplete.HasValue)
            {
                result.Add(MetaSentencesAreComplete, sentencesAreComplete.Value);
            }

            if (pageNumber.HasValue)
            {
                result.Add(MetaPageNumber, pageNumber.Value);
            }

            if (boundingBox.HasValue)
            {
                result.Add("boundingBox", boundingBox.Value);
            }

            if (confidence.HasValue)
            {
                result.Add("confidence", confidence.Value);
            }

            if (source.HasValue)
            {
                result.Add("source", source.Value.ToString());
            }

            return result;
        }
    }
}
