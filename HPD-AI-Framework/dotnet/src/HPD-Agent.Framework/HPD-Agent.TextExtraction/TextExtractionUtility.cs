using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using HPD.Agent.TextExtraction.Interfaces;
using HPD.Agent.TextExtraction.Models;
using HPD.Agent.TextExtraction.Extensions;

namespace HPD.Agent.TextExtraction
{
    /// <summary>
    /// Legacy interface for backward compatibility
    /// </summary>
    public interface ITextDecoder : IAsyncDisposable, IDisposable
    {
        Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default);
        string Description { get; }
    }

    /// <summary>
    /// Adapter to convert IContentDecoder to ITextDecoder for backward compatibility
    /// </summary>
    public class ContentDecoderAdapter : ITextDecoder
    {
        private readonly IContentDecoder _decoder;

        public ContentDecoderAdapter(IContentDecoder decoder)
        {
            _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        }

        public string Description => $"Adapter for {_decoder.GetType().Name}";

        public async Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var fileContent = await _decoder.DecodeAsync(filePath, cancellationToken);
            return ConvertFileContentToString(fileContent);
        }

        private static string ConvertFileContentToString(FileContent fileContent)
        {
            if (fileContent.Sections.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var section in fileContent.Sections)
            {
                var sectionContent = section.Content.Trim();
                if (string.IsNullOrEmpty(sectionContent)) continue;

                sb.Append(sectionContent);
                if (section.SentencesAreComplete)
                {
                    sb.AppendLineNix();
                    sb.AppendLineNix();
                }
                else
                {
                    sb.AppendLineNix();
                }
            }
            return sb.ToString().Trim();
        }

        public void Dispose() => (_decoder as IDisposable)?.Dispose();
        public ValueTask DisposeAsync() => (_decoder as IAsyncDisposable)?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    /// <summary>
    /// Text extraction result containing extracted text and metadata
    /// </summary>
    public sealed class TextExtractionResult
    {
        public bool IsSuccess { get; init; }
        public string ExtractedText { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public string FilePath { get; init; } = string.Empty;
        public string? ErrorMessage { get; init; }
        public TimeSpan ProcessingTime { get; init; }
        public long FileSizeBytes { get; init; }
        public string MimeType { get; init; } = string.Empty;
        public FileContent? OriginalFileContent { get; init; }

        public static TextExtractionResult Success(string extractedText, string fileName, string filePath,
            TimeSpan processingTime, long fileSizeBytes, string mimeType, FileContent originalContent) =>
            new()
            {
                IsSuccess = true,
                ExtractedText = extractedText,
                FileName = fileName,
                FilePath = filePath,
                ProcessingTime = processingTime,
                FileSizeBytes = fileSizeBytes,
                MimeType = mimeType,
                OriginalFileContent = originalContent
            };

        public static TextExtractionResult Failure(string fileName, string filePath, string errorMessage) =>
            new()
            {
                IsSuccess = false,
                FileName = fileName,
                FilePath = filePath,
                ErrorMessage = errorMessage
            };
    }

    /// <summary>
    /// Factory interface for creating decoders (legacy compatibility)
    /// </summary>
    public interface IDecoderRegistry
    {
        TDecoder CreateDecoder<TDecoder>() where TDecoder : ITextDecoder, new();
    }

    /// <summary>
    /// Legacy decoder registry implementation
    /// </summary>
    public sealed class DecoderRegistry : IDecoderRegistry
    {
        public TDecoder CreateDecoder<TDecoder>() where TDecoder : ITextDecoder, new() { return new TDecoder(); }
    }

    /// <summary>
    /// Main text extraction utility that uses the new decoder architecture
    /// </summary>
    public sealed class TextExtractionUtility : IDisposable
    {
        private readonly IDecoderFactory _decoderFactory;
        private readonly IMimeTypeDetection _mimeTypeDetection;
        private readonly ILogger<TextExtractionUtility> _log;

        public TextExtractionUtility(
            IDecoderFactory? decoderFactory = null,
            IMimeTypeDetection? mimeTypeDetection = null,
            ILoggerFactory? loggerFactory = null)
        {
            _decoderFactory = decoderFactory ?? new DecoderFactory(mimeTypeDetection, loggerFactory);
            _mimeTypeDetection = mimeTypeDetection ?? new MimeTypesDetection();
            _log = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<TextExtractionUtility>();
        }

        /// <summary>
        /// Extract text from a file or URL
        /// </summary>
        public async Task<TextExtractionResult> ExtractTextAsync(string urlOrFilePath, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(urlOrFilePath);

            var startTime = DateTime.UtcNow;
            bool isUrl = IsUrl(urlOrFilePath);
            string fileName;
            string? mimeType;
            long fileSize = 0;

            if (isUrl)
            {
                var uri = new Uri(urlOrFilePath);
                fileName = Path.GetFileName(uri.LocalPath) ?? uri.Host;
                mimeType = Models.MimeTypes.WebPageUrl;
                _log.LogDebug("Processing URL: {Url}", urlOrFilePath);
            }
            else
            {
                var fi = new FileInfo(urlOrFilePath);
                if (!fi.Exists)
                {
                    _log.LogWarning("File not found: {FilePath}", urlOrFilePath);
                    return TextExtractionResult.Failure(fi.Name, urlOrFilePath, $"File not found: {urlOrFilePath}");
                }

                fileName = fi.Name;
                fileSize = fi.Length;

                if (!_mimeTypeDetection.TryGetFileType(fileName, out mimeType))
                {
                    mimeType = Models.MimeTypes.PlainText; // Default fallback
                }

                _log.LogDebug("Processing file: {FilePath} ({FileSize} bytes), MIME type: {MimeType}",
                    urlOrFilePath, fileSize, mimeType);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Get appropriate decoder
            var decoder = isUrl
                ? _decoderFactory.GetDecoder(Models.MimeTypes.WebPageUrl)
                : _decoderFactory.GetDecoderForFile(urlOrFilePath);

            if (decoder == null)
            {
                var errorMsg = $"No decoder found for {(isUrl ? "URL" : "file type")} '{urlOrFilePath}'";
                _log.LogError(errorMsg);
                return TextExtractionResult.Failure(fileName, urlOrFilePath, errorMsg);
            }

            try
            {
                _log.LogDebug("Using decoder: {DecoderType}", decoder.GetType().Name);

                FileContent fileContent;
                if (isUrl && decoder is IWebDecoder webDecoder)
                {
                    fileContent = await webDecoder.DecodeFromUrlAsync(urlOrFilePath, cancellationToken);
                }
                else
                {
                    fileContent = await decoder.DecodeAsync(urlOrFilePath, cancellationToken);
                }

                var extractedText = ConvertFileContentToString(fileContent);
                var processingTime = DateTime.UtcNow - startTime;

                _log.LogInformation("Successfully extracted {CharCount} characters from '{FileName}' in {ProcessingTime}ms",
                    extractedText.Length, fileName, processingTime.TotalMilliseconds);

                return TextExtractionResult.Success(extractedText, fileName, urlOrFilePath,
                    processingTime, fileSize, mimeType ?? Models.MimeTypes.PlainText, fileContent);
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _log.LogError(ex, "Failed to extract text from '{FilePath}' after {ProcessingTime}ms",
                    urlOrFilePath, processingTime.TotalMilliseconds);

                return TextExtractionResult.Failure(fileName, urlOrFilePath,
                    $"Text extraction failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract text from binary data (e.g., from DataContent in Microsoft.Extensions.AI)
        /// </summary>
        /// <param name="data">Binary data to extract text from</param>
        /// <param name="mimeType">MIME type of the data</param>
        /// <param name="fileName">Optional file name (used for logging and result)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Text extraction result</returns>
        public async Task<TextExtractionResult> ExtractTextAsync(
            ReadOnlyMemory<byte> data,
            string? mimeType = null,
            string? fileName = null,
            CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            fileName ??= "binary-data";
            mimeType ??= Models.MimeTypes.PlainText;
            long fileSize = data.Length;

            _log.LogDebug("Processing binary data ({FileSize} bytes), MIME type: {MimeType}",
                fileSize, mimeType);

            cancellationToken.ThrowIfCancellationRequested();

            // Get appropriate decoder
            var decoder = _decoderFactory.GetDecoder(mimeType);

            if (decoder == null)
            {
                var errorMsg = $"No decoder found for MIME type '{mimeType}'";
                _log.LogError(errorMsg);
                return TextExtractionResult.Failure(fileName, "binary-data", errorMsg);
            }

            try
            {
                _log.LogDebug("Using decoder: {DecoderType}", decoder.GetType().Name);

                // Create a MemoryStream from the binary data
                using var stream = new MemoryStream(data.ToArray(), writable: false);
                var fileContent = await decoder.DecodeAsync(stream, cancellationToken);

                var extractedText = ConvertFileContentToString(fileContent);
                var processingTime = DateTime.UtcNow - startTime;

                _log.LogInformation("Successfully extracted {CharCount} characters from binary data in {ProcessingTime}ms",
                    extractedText.Length, processingTime.TotalMilliseconds);

                return TextExtractionResult.Success(extractedText, fileName, "binary-data",
                    processingTime, fileSize, mimeType, fileContent);
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _log.LogError(ex, "Failed to extract text from binary data after {ProcessingTime}ms",
                    processingTime.TotalMilliseconds);

                return TextExtractionResult.Failure(fileName, "binary-data",
                    $"Text extraction failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Register a custom decoder
        /// </summary>
        public void RegisterDecoder(IContentDecoder decoder, params string[] mimeTypes)
        {
            _decoderFactory.RegisterDecoder(decoder, mimeTypes);
        }

        /// <summary>
        /// Get all available decoders
        /// </summary>
        public IEnumerable<IContentDecoder> GetAvailableDecoders()
        {
            return _decoderFactory.GetAllDecoders();
        }

        /// <summary>
        /// Legacy method: Create a decoder adapter for backward compatibility
        /// </summary>
        public ITextDecoder? CreateLegacyDecoder(string mimeType)
        {
            var decoder = _decoderFactory.GetDecoder(mimeType);
            return decoder != null ? new ContentDecoderAdapter(decoder) : null;
        }

        private static string ConvertFileContentToString(FileContent fileContent)
        {
            if (fileContent.Sections.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var section in fileContent.Sections)
            {
                var sectionContent = section.Content.Trim();
                if (string.IsNullOrEmpty(sectionContent)) continue;

                sb.Append(sectionContent);
                if (section.SentencesAreComplete)
                {
                    sb.AppendLineNix();
                    sb.AppendLineNix();
                }
                else
                {
                    sb.AppendLineNix();
                }
            }
            return sb.ToString().Trim() ?? string.Empty;
        }

        public static bool IsUrl(string input)
        {
            return Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        public void Dispose()
        {
            // Dispose any resources if needed
        }
    }

    /// <summary>
    /// Extension methods for easier usage
    /// </summary>
    public static class TextExtractionUtilityExtensions
    {
        public static async Task<string> ExtractTextStringAsync(this TextExtractionUtility utility,
            string urlOrFilePath, CancellationToken cancellationToken = default)
        {
            var result = await utility.ExtractTextAsync(urlOrFilePath, cancellationToken);
            return result.IsSuccess ? result.ExtractedText : throw new InvalidOperationException(result.ErrorMessage);
        }
    }
}