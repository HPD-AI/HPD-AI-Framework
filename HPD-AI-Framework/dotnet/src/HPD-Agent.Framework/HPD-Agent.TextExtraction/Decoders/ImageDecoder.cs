using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HPD.Agent.TextExtraction.Extensions;
using HPD.Agent.TextExtraction.Interfaces;
using HPD.Agent.TextExtraction.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Agent.TextExtraction.Decoders
{
    /// <summary>
    /// Image decoder with OCR support
    /// </summary>
    public sealed class ImageDecoder : IContentDecoder, IOcrDecoder
    {
        private readonly IOcrEngine? _ocrEngine;
        private readonly ILogger<ImageDecoder> _log;
        private string _ocrLanguage = "eng";
        private bool _ocrEnabled = true;

        public ImageDecoder(IOcrEngine? ocrEngine = null, ILoggerFactory? loggerFactory = null)
        {
            _ocrEngine = ocrEngine;
            _log = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<ImageDecoder>();
        }

        public string OcrLanguage
        {
            get => _ocrLanguage;
            set => _ocrLanguage = value ?? "eng";
        }

        public bool OcrEnabled
        {
            get => _ocrEnabled && _ocrEngine != null;
            set => _ocrEnabled = value;
        }

        public bool SupportsMimeType(string mimeType)
        {
            return mimeType != null && (
                mimeType.StartsWith(Models.MimeTypes.ImageJpeg, StringComparison.OrdinalIgnoreCase) ||
                mimeType.StartsWith(Models.MimeTypes.ImagePng, StringComparison.OrdinalIgnoreCase) ||
                mimeType.StartsWith(Models.MimeTypes.ImageTiff, StringComparison.OrdinalIgnoreCase) ||
                mimeType.StartsWith(Models.MimeTypes.ImageBmp, StringComparison.OrdinalIgnoreCase) ||
                mimeType.StartsWith(Models.MimeTypes.ImageGif, StringComparison.OrdinalIgnoreCase) ||
                mimeType.StartsWith(Models.MimeTypes.ImageWebP, StringComparison.OrdinalIgnoreCase)
            );
        }

        public async Task<FileContent> DecodeAsync(string filename, CancellationToken cancellationToken = default)
        {
            _log.LogDebug("Extracting text from image file '{0}'", filename);

            var result = new FileContent(MimeTypes.PlainText);

            if (!OcrEnabled)
            {
                _log.LogWarning("OCR is disabled or not available. Returning empty content for image file.");
                result.Sections.Add(new Chunk(string.Empty, 1, Chunk.Meta(sentencesAreComplete: true)));
                return result;
            }

            var content = await ImageToTextAsync(filename, cancellationToken).ConfigureAwait(false);
            result.Sections.Add(new Chunk(content.Trim(), 1, Chunk.Meta(sentencesAreComplete: true)));

            return result;
        }

        public async Task<FileContent> DecodeAsync(Stream data, CancellationToken cancellationToken = default)
        {
            _log.LogDebug("Extracting text from image stream");

            var result = new FileContent(MimeTypes.PlainText);

            if (!OcrEnabled)
            {
                _log.LogWarning("OCR is disabled or not available. Returning empty content for image stream.");
                result.Sections.Add(new Chunk(string.Empty, 1, Chunk.Meta(sentencesAreComplete: true)));
                return result;
            }

            var content = await ImageToTextAsync(data, cancellationToken).ConfigureAwait(false);
            result.Sections.Add(new Chunk(content.Trim(), 1, Chunk.Meta(sentencesAreComplete: true)));

            return result;
        }

        private async Task<string> ImageToTextAsync(string filename, CancellationToken cancellationToken = default)
        {
            using var content = File.OpenRead(filename);
            return await ImageToTextAsync(content, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> ImageToTextAsync(Stream data, CancellationToken cancellationToken = default)
        {
            if (_ocrEngine == null)
            {
                _log.LogWarning("OCR engine is not configured. Unable to extract text from image.");
                return string.Empty;
            }

            try
            {
                // Use the IOcrEngine to extract text
                // This is where Kernel Memory's OCR would be called
                // For now, returning a placeholder until OCR engine is properly configured
                return await _ocrEngine.ExtractTextFromImageAsync(data, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error extracting text from image using OCR");
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// Interface for OCR engines - allows swapping implementations
    /// </summary>
    public interface IOcrEngine
    {
        Task<string> ExtractTextFromImageAsync(Stream imageStream, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Placeholder OCR engine for when Kernel Memory's OCR is not available
    /// Can be replaced with Tesseract, Windows.Media.Ocr, IronOCR, etc.
    /// </summary>
    public class PlaceholderOcrEngine : IOcrEngine
    {
        public Task<string> ExtractTextFromImageAsync(Stream imageStream, CancellationToken cancellationToken = default)
        {
            // This is a placeholder - replace with actual OCR implementation
            // Options:
            // 1. Microsoft.KernelMemory.DataFormats.Image.IOcrEngine
            // 2. Tesseract
            // 3. Windows.Media.Ocr
            // 4. IronOCR
            // 5. Azure Computer Vision API
            return Task.FromResult("[OCR text extraction not configured]");
        }
    }
}