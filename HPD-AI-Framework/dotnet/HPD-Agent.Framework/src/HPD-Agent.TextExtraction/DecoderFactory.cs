using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HPD.Agent.TextExtraction.Decoders;
using HPD.Agent.TextExtraction.Interfaces;
using HPD.Agent.TextExtraction.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.TextExtraction
{
    /// <summary>
    /// Factory for creating and managing content decoders
    /// </summary>
    public interface IDecoderFactory
    {
        IContentDecoder? GetDecoder(string mimeType);
        IContentDecoder? GetDecoderForFile(string filename);
        void RegisterDecoder(IContentDecoder decoder, params string[] mimeTypes);
        IEnumerable<IContentDecoder> GetAllDecoders();
    }

    /// <summary>
    /// Default implementation of decoder factory with built-in decoders
    /// </summary>
    public class DecoderFactory : IDecoderFactory
    {
        private readonly Dictionary<string, IContentDecoder> _decoders = new();
        private readonly IMimeTypeDetection _mimeTypeDetection;
        private readonly ILogger<DecoderFactory>? _logger;

        public DecoderFactory(
            IMimeTypeDetection? mimeTypeDetection = null,
            ILoggerFactory? loggerFactory = null)
        {
            _mimeTypeDetection = mimeTypeDetection ?? new MimeTypesDetection();
            _logger = loggerFactory?.CreateLogger<DecoderFactory>();

            // Register default decoders
            RegisterDefaultDecoders(loggerFactory);
        }

        /// <summary>
        /// Constructor for dependency injection with custom decoders
        /// </summary>
        public DecoderFactory(
            IEnumerable<IContentDecoder> decoders,
            IMimeTypeDetection? mimeTypeDetection = null,
            ILoggerFactory? loggerFactory = null)
        {
            _mimeTypeDetection = mimeTypeDetection ?? new MimeTypesDetection();
            _logger = loggerFactory?.CreateLogger<DecoderFactory>();

            // Register provided decoders
            foreach (var decoder in decoders)
            {
                RegisterDecoderByType(decoder);
            }

            // If no decoders provided, register defaults
            if (!_decoders.Any())
            {
                RegisterDefaultDecoders(loggerFactory);
            }
        }

        public IContentDecoder? GetDecoder(string mimeType)
        {
            if (string.IsNullOrEmpty(mimeType))
                return null;

            // Try exact match first
            if (_decoders.TryGetValue(mimeType.ToLowerInvariant(), out var decoder))
            {
                return decoder;
            }

            // Try to find a decoder that supports this mime type
            foreach (var kvp in _decoders)
            {
                if (kvp.Value.SupportsMimeType(mimeType))
                {
                    return kvp.Value;
                }
            }

            _logger?.LogWarning("No decoder found for MIME type: {MimeType}", mimeType);
            return null;
        }

        public IContentDecoder? GetDecoderForFile(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return null;

            // Try to get MIME type from file extension
            if (_mimeTypeDetection.TryGetFileType(filename, out var mimeType) && !string.IsNullOrEmpty(mimeType))
            {
                return GetDecoder(mimeType);
            }

            // If it's a URL, try web decoder
            if (Uri.TryCreate(filename, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return GetDecoder(MimeTypes.WebPageUrl);
            }

            _logger?.LogWarning("Could not determine decoder for file: {Filename}", filename);
            return null;
        }

        public void RegisterDecoder(IContentDecoder decoder, params string[] mimeTypes)
        {
            if (decoder == null)
                throw new ArgumentNullException(nameof(decoder));

            if (mimeTypes == null || mimeTypes.Length == 0)
            {
                // Try to auto-detect supported mime types
                RegisterDecoderByType(decoder);
                return;
            }

            foreach (var mimeType in mimeTypes)
            {
                if (!string.IsNullOrEmpty(mimeType))
                {
                    var key = mimeType.ToLowerInvariant();
                    _decoders[key] = decoder;
                    _logger?.LogDebug("Registered decoder {DecoderType} for MIME type: {MimeType}",
                        decoder.GetType().Name, mimeType);
                }
            }
        }

        public IEnumerable<IContentDecoder> GetAllDecoders()
        {
            return _decoders.Values.Distinct();
        }

        private void RegisterDefaultDecoders(ILoggerFactory? loggerFactory)
        {
            // PDF
            var pdfDecoder = new PdfDecoder(loggerFactory);
            RegisterDecoder(pdfDecoder, MimeTypes.Pdf);

            // Microsoft Office
            var wordDecoder = new MsWordDecoder(loggerFactory);
            RegisterDecoder(wordDecoder, MimeTypes.MsWord, MimeTypes.MsWordX);

            var excelDecoder = new MsExcelDecoder(loggerFactory);
            RegisterDecoder(excelDecoder, MimeTypes.MsExcel, MimeTypes.MsExcelX);

            var powerPointDecoder = new MsPowerPointDecoder(loggerFactory);
            RegisterDecoder(powerPointDecoder, MimeTypes.MsPowerPoint, MimeTypes.MsPowerPointX);

            // Images with OCR
            var imageDecoder = new ImageDecoder(null, loggerFactory); // OCR engine can be injected later
            RegisterDecoder(imageDecoder,
                "image/jpeg", "image/png", "image/tiff",
                "image/bmp", "image/gif", "image/webp");

            // Web content
            var webDecoder = new WebDecoder(null, null, loggerFactory);
            RegisterDecoder(webDecoder, "text/html", "application/xhtml+xml", "text/x-uri");

            _logger?.LogInformation("Registered {Count} default decoders", _decoders.Count);
        }

        private void RegisterDecoderByType(IContentDecoder decoder)
        {
            // Auto-detect supported MIME types based on decoder type
            var decoderType = decoder.GetType();

            if (decoderType == typeof(PdfDecoder))
            {
                RegisterDecoder(decoder, MimeTypes.Pdf);
            }
            else if (decoderType == typeof(MsWordDecoder))
            {
                RegisterDecoder(decoder, MimeTypes.MsWord, MimeTypes.MsWordX);
            }
            else if (decoderType == typeof(MsExcelDecoder))
            {
                RegisterDecoder(decoder, MimeTypes.MsExcel, MimeTypes.MsExcelX);
            }
            else if (decoderType == typeof(MsPowerPointDecoder))
            {
                RegisterDecoder(decoder, MimeTypes.MsPowerPoint, MimeTypes.MsPowerPointX);
            }
            else if (decoderType == typeof(ImageDecoder))
            {
                RegisterDecoder(decoder,
                    Models.MimeTypes.ImageJpeg, Models.MimeTypes.ImagePng, Models.MimeTypes.ImageTiff,
                    Models.MimeTypes.ImageBmp, Models.MimeTypes.ImageGif, Models.MimeTypes.ImageWebP);
            }
            else if (decoderType == typeof(WebDecoder))
            {
                RegisterDecoder(decoder, Models.MimeTypes.Html, Models.MimeTypes.XHTML, Models.MimeTypes.WebPageUrl);
            }
            else
            {
                // For custom decoders, try common MIME types
                var commonMimeTypes = new[]
                {
                    Models.MimeTypes.PlainText, Models.MimeTypes.Json, Models.MimeTypes.XML,
                    Models.MimeTypes.MarkDown, Models.MimeTypes.Pdf, Models.MimeTypes.Html
                };

                foreach (var mimeType in commonMimeTypes)
                {
                    if (decoder.SupportsMimeType(mimeType))
                    {
                        RegisterDecoder(decoder, mimeType);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Service collection extensions for dependency injection
    /// </summary>
    public static class DecoderServiceExtensions
    {
        public static IServiceCollection AddTextExtraction(this IServiceCollection services)
        {
            // Register MIME type detection
            services.AddSingleton<IMimeTypeDetection, MimeTypesDetection>();

            // Register individual decoders
            services.AddSingleton<PdfDecoder>();
            services.AddSingleton<MsWordDecoder>();
            services.AddSingleton<MsExcelDecoder>();
            services.AddSingleton<MsPowerPointDecoder>();
            services.AddSingleton<ImageDecoder>();
            services.AddSingleton<WebDecoder>();

            // Register them as IContentDecoder
            services.AddSingleton<IContentDecoder>(sp => sp.GetRequiredService<PdfDecoder>());
            services.AddSingleton<IContentDecoder>(sp => sp.GetRequiredService<MsWordDecoder>());
            services.AddSingleton<IContentDecoder>(sp => sp.GetRequiredService<MsExcelDecoder>());
            services.AddSingleton<IContentDecoder>(sp => sp.GetRequiredService<MsPowerPointDecoder>());
            services.AddSingleton<IContentDecoder>(sp => sp.GetRequiredService<ImageDecoder>());
            services.AddSingleton<IContentDecoder>(sp => sp.GetRequiredService<WebDecoder>());

            // Register factory
            services.AddSingleton<IDecoderFactory, DecoderFactory>();

            return services;
        }

        public static IServiceCollection AddTextExtractionWithOcr<TOcrEngine>(
            this IServiceCollection services)
            where TOcrEngine : class, IOcrEngine
        {
            // Add base text extraction
            services.AddTextExtraction();

            // Add OCR engine
            services.AddSingleton<IOcrEngine, TOcrEngine>();

            return services;
        }
    }
}