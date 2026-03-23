using System;
using System.Collections.Generic;
using System.IO;

namespace HPD.Agent.TextExtraction.Models
{
    public static class MimeTypes
    {
        public const string PlainText = "text/plain";

        // Multiple values have been used over the years.
        public const string MarkDown = "text/markdown";
        public const string MarkDownOld1 = "text/x-markdown";
        public const string MarkDownOld2 = "text/plain-markdown";

        public const string Html = "text/html";
        public const string XHTML = "application/xhtml+xml";
        public const string XML = "application/xml";
        public const string XML2 = "text/xml";
        public const string Json = "application/json";

        public const string ImageBmp = "image/bmp";
        public const string ImageGif = "image/gif";
        public const string ImageJpeg = "image/jpeg";
        public const string ImagePng = "image/png";
        public const string ImageTiff = "image/tiff";
        public const string ImageWebP = "image/webp";

        public const string WebPageUrl = "text/x-uri";

        public const string Pdf = "application/pdf";

        public const string MsWord = "application/msword";
        public const string MsWordX = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        public const string MsPowerPoint = "application/vnd.ms-powerpoint";
        public const string MsPowerPointX = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

        public const string MsExcel = "application/vnd.ms-excel";
        public const string MsExcelX = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    }

    public static class FileExtensions
    {
        public const string PlainText = ".txt";
        public const string MarkDown = ".md";

        public const string Htm = ".htm";
        public const string Html = ".html";
        public const string XHTML = ".xhtml";
        public const string XML = ".xml";
        public const string Json = ".json";

        public const string ImageBmp = ".bmp";
        public const string ImageGif = ".gif";
        public const string ImageJpeg = ".jpeg";
        public const string ImageJpg = ".jpg";
        public const string ImagePng = ".png";
        public const string ImageTiff = ".tiff";
        public const string ImageTiff2 = ".tif";
        public const string ImageWebP = ".webp";

        public const string WebPageUrl = ".url";

        public const string Pdf = ".pdf";

        public const string MsWord = ".doc";
        public const string MsWordX = ".docx";
        public const string MsPowerPoint = ".ppt";
        public const string MsPowerPointX = ".pptx";
        public const string MsExcel = ".xls";
        public const string MsExcelX = ".xlsx";
    }

    public interface IMimeTypeDetection
    {
        string GetFileType(string filename);
        bool TryGetFileType(string filename, out string? mimeType);
    }

    public class MimeTypesDetection : IMimeTypeDetection
    {
        private static readonly Dictionary<string, string> s_extensionTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { FileExtensions.PlainText, MimeTypes.PlainText },
                { FileExtensions.MarkDown, MimeTypes.MarkDown },
                { FileExtensions.Htm, MimeTypes.Html },
                { FileExtensions.Html, MimeTypes.Html },
                { FileExtensions.XHTML, MimeTypes.XHTML },
                { FileExtensions.XML, MimeTypes.XML },
                { FileExtensions.Json, MimeTypes.Json },
                { FileExtensions.ImageBmp, MimeTypes.ImageBmp },
                { FileExtensions.ImageGif, MimeTypes.ImageGif },
                { FileExtensions.ImageJpeg, MimeTypes.ImageJpeg },
                { FileExtensions.ImageJpg, MimeTypes.ImageJpeg },
                { FileExtensions.ImagePng, MimeTypes.ImagePng },
                { FileExtensions.ImageTiff, MimeTypes.ImageTiff },
                { FileExtensions.ImageTiff2, MimeTypes.ImageTiff },
                { FileExtensions.ImageWebP, MimeTypes.ImageWebP },
                { FileExtensions.WebPageUrl, MimeTypes.WebPageUrl },
                { FileExtensions.Pdf, MimeTypes.Pdf },
                { FileExtensions.MsWord, MimeTypes.MsWord },
                { FileExtensions.MsWordX, MimeTypes.MsWordX },
                { FileExtensions.MsPowerPoint, MimeTypes.MsPowerPoint },
                { FileExtensions.MsPowerPointX, MimeTypes.MsPowerPointX },
                { FileExtensions.MsExcel, MimeTypes.MsExcel },
                { FileExtensions.MsExcelX, MimeTypes.MsExcelX },
            };

        public string GetFileType(string filename)
        {
            string extension = Path.GetExtension(filename);

            if (s_extensionTypes.TryGetValue(extension, out var mimeType))
            {
                return mimeType;
            }

            throw new NotSupportedException($"File type not supported: {filename}");
        }

        public bool TryGetFileType(string filename, out string? mimeType)
        {
            return s_extensionTypes.TryGetValue(Path.GetExtension(filename), out mimeType);
        }
    }
}