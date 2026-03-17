using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HPD.Agent.TextExtraction.Models;

namespace HPD.Agent.TextExtraction.Interfaces
{
    /// <summary>
    /// Interface for content decoders that extract text from various file formats
    /// </summary>
    public interface IContentDecoder
    {
        /// <summary>
        /// Check if the decoder supports the given MIME type
        /// </summary>
        bool SupportsMimeType(string mimeType);

        /// <summary>
        /// Decode content from a file
        /// </summary>
        Task<FileContent> DecodeAsync(string filename, CancellationToken cancellationToken = default);

        /// <summary>
        /// Decode content from a stream
        /// </summary>
        Task<FileContent> DecodeAsync(Stream data, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Extended interface for decoders that need configuration
    /// </summary>
    public interface IConfigurableDecoder : IContentDecoder
    {
        /// <summary>
        /// Configure the decoder with implementation-specific options
        /// </summary>
        void Configure(object options);
    }

    /// <summary>
    /// Interface for OCR-capable decoders
    /// </summary>
    public interface IOcrDecoder : IContentDecoder
    {
        /// <summary>
        /// Gets or sets the OCR language
        /// </summary>
        string OcrLanguage { get; set; }

        /// <summary>
        /// Gets whether OCR is enabled
        /// </summary>
        bool OcrEnabled { get; set; }
    }

    /// <summary>
    /// Interface for web content decoders
    /// </summary>
    public interface IWebDecoder : IContentDecoder
    {
        /// <summary>
        /// Decode content from a URL
        /// </summary>
        Task<FileContent> DecodeFromUrlAsync(string url, CancellationToken cancellationToken = default);
    }
}