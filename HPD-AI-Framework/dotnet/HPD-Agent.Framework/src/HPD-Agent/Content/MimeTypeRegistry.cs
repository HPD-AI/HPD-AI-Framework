// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;

namespace HPD.Agent;

/// <summary>
/// Centralized registry for MIME types and file extensions.
/// Provides two-way mapping and supports legacy format variations.
/// </summary>
public static class MimeTypeRegistry
{
    #region Image MIME Types

    /// <summary>Wildcard MIME type for images when exact format is unknown.</summary>
    public const string ImageAny = "image/*";
    /// <summary>
    /// MIME type for PNG images.
    /// </summary>
    public const string ImagePng = "image/png";
    /// <summary>
    /// MIME type for JPEG images.
    /// </summary>
    public const string ImageJpeg = "image/jpeg";
    /// <summary>
    /// MIME type for GIF images.
    /// </summary>
    public const string ImageGif = "image/gif";
    /// <summary>
    /// MIME type for WebP images.
    /// </summary>
    public const string ImageWebP = "image/webp";
    /// <summary>
    /// MIME type for BMP images.
    /// </summary>
    public const string ImageBmp = "image/bmp";
    /// <summary>
    /// MIME type for TIFF images.
    /// </summary>
    public const string ImageTiff = "image/tiff";
    /// <summary>
    /// MIME type for HEIC images.
    /// </summary>
    public const string ImageHeic = "image/heic";
    /// <summary>
    /// MIME type for HEIF images.
    /// </summary>
    public const string ImageHeif = "image/heif";
    /// <summary>
    /// MIME type for AVIF images.
    /// </summary>
    public const string ImageAvif = "image/avif";
    /// <summary>
    /// MIME type for SVG images.
    /// </summary>
    public const string ImageSvg = "image/svg+xml";
    /// <summary>
    /// MIME type for icon images.
    /// </summary>
    public const string ImageIcon = "image/x-icon";

    #endregion

    #region Audio MIME Types

    /// <summary>
    /// MIME type for MPEG audio.
    /// </summary>
    public const string AudioMpeg = "audio/mpeg";
    /// <summary>
    /// MIME type for MP3 audio (alias for AudioMpeg).
    /// </summary>
    public const string AudioMp3 = "audio/mpeg"; // Alias for AudioMpeg
    /// <summary>
    /// MIME type for WAV audio.
    /// </summary>
    public const string AudioWav = "audio/wav";
    /// <summary>
    /// MIME type for WAVE audio (alternative for WAV).
    /// </summary>
    public const string AudioWave = "audio/wave"; // Alternative for WAV
    /// <summary>
    /// MIME type for OGG audio.
    /// </summary>
    public const string AudioOgg = "audio/ogg";
    /// <summary>
    /// MIME type for FLAC audio.
    /// </summary>
    public const string AudioFlac = "audio/flac";
    /// <summary>
    /// MIME type for WebM audio.
    /// </summary>
    public const string AudioWebM = "audio/webm";
    /// <summary>
    /// MIME type for MP4 audio.
    /// </summary>
    public const string AudioMp4 = "audio/mp4";
    /// <summary>
    /// MIME type for AAC audio.
    /// </summary>
    public const string AudioAac = "audio/aac";
    /// <summary>
    /// MIME type for Opus audio.
    /// </summary>
    public const string AudioOpus = "audio/opus";
    /// <summary>
    /// MIME type for Vorbis audio.
    /// </summary>
    public const string AudioVorbis = "audio/vorbis";

    #endregion

    #region Video MIME Types

    /// <summary>Wildcard MIME type for videos when exact format is unknown.</summary>
    public const string VideoAny = "video/*";
    /// <summary>
    /// MIME type for MP4 video.
    /// </summary>
    public const string VideoMp4 = "video/mp4";
    /// <summary>
    /// MIME type for WebM video.
    /// </summary>
    public const string VideoWebM = "video/webm";
    /// <summary>
    /// MIME type for QuickTime video.
    /// </summary>
    public const string VideoQuickTime = "video/quicktime";
    /// <summary>
    /// MIME type for AVI video.
    /// </summary>
    public const string VideoAvi = "video/x-msvideo";
    /// <summary>
    /// MIME type for Matroska video.
    /// </summary>
    public const string VideoMatroska = "video/x-matroska";
    /// <summary>
    /// MIME type for FLV video.
    /// </summary>
    public const string VideoFlv = "video/x-flv";
    /// <summary>
    /// MIME type for WMV video.
    /// </summary>
    public const string VideoWmv = "video/x-ms-wmv";
    /// <summary>
    /// MIME type for OGG video.
    /// </summary>
    public const string VideoOgg = "video/ogg";
    /// <summary>
    /// MIME type for 3GPP video.
    /// </summary>
    public const string Video3gpp = "video/3gpp";
    /// <summary>
    /// MIME type for 3GPP2 video.
    /// </summary>
    public const string Video3gpp2 = "video/3gpp2";
    /// <summary>
    /// MIME type for MPEG video.
    /// </summary>
    public const string VideoMpeg = "video/mpeg";

    #endregion

    #region Document MIME Types

    /// <summary>
    /// MIME type for PDF documents.
    /// </summary>
    public const string ApplicationPdf = "application/pdf";

    // Microsoft Office (Legacy)
    /// <summary>
    /// MIME type for Microsoft Word documents.
    /// </summary>
    public const string ApplicationMsWord = "application/msword";
    /// <summary>
    /// MIME type for Microsoft Excel documents.
    /// </summary>
    public const string ApplicationMsExcel = "application/vnd.ms-excel";
    /// <summary>
    /// MIME type for Microsoft PowerPoint documents.
    /// </summary>
    public const string ApplicationMsPowerPoint = "application/vnd.ms-powerpoint";

    // Microsoft Office (OpenXML)
    /// <summary>
    /// MIME type for Word OpenXML documents.
    /// </summary>
    public const string ApplicationWordOpenXml = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    /// <summary>
    /// MIME type for Excel OpenXML documents.
    /// </summary>
    public const string ApplicationExcelOpenXml = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    /// <summary>
    /// MIME type for PowerPoint OpenXML documents.
    /// </summary>
    public const string ApplicationPowerPointOpenXml = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    // OpenDocument
    /// <summary>
    /// MIME type for OpenDocument Text documents.
    /// </summary>
    public const string ApplicationOpenDocumentText = "application/vnd.oasis.opendocument.text";
    /// <summary>
    /// MIME type for OpenDocument Spreadsheet documents.
    /// </summary>
    public const string ApplicationOpenDocumentSpreadsheet = "application/vnd.oasis.opendocument.spreadsheet";
    /// <summary>
    /// MIME type for OpenDocument Presentation documents.
    /// </summary>
    public const string ApplicationOpenDocumentPresentation = "application/vnd.oasis.opendocument.presentation";

    // Archives
    /// <summary>
    /// MIME type for ZIP archives.
    /// </summary>
    public const string ApplicationZip = "application/zip";
    /// <summary>
    /// MIME type for GZIP archives.
    /// </summary>
    public const string ApplicationGzip = "application/gzip";
    /// <summary>
    /// MIME type for 7z archives.
    /// </summary>
    public const string Application7z = "application/x-7z-compressed";
    /// <summary>
    /// MIME type for RAR archives.
    /// </summary>
    public const string ApplicationRar = "application/vnd.rar";
    public const string ApplicationTar = "application/x-tar";
    public const string ApplicationBzip2 = "application/x-bzip2";
    public const string ApplicationXz = "application/x-xz";

    #endregion

    #region Text MIME Types

    public const string TextPlain = "text/plain";
    public const string TextHtml = "text/html";
    public const string TextMarkdown = "text/markdown";
    public const string TextMarkdownOld1 = "text/x-markdown"; // Legacy variant
    public const string TextMarkdownOld2 = "text/plain-markdown"; // Legacy variant
    public const string TextCsv = "text/csv";
    public const string TextXml = "text/xml";
    public const string TextCss = "text/css";
    public const string TextJavascript = "text/javascript";
    public const string TextTypescript = "text/typescript";
    public const string ApplicationJson = "application/json";
    public const string ApplicationXml = "application/xml";
    public const string ApplicationYaml = "application/yaml";
    public const string ApplicationRtf = "application/rtf";

    #endregion

    #region Generic

    public const string ApplicationOctetStream = "application/octet-stream";

    #endregion

    #region Extension to MIME Type Mapping

    private static readonly Dictionary<string, string> s_extensionToMimeType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            [".png"] = ImagePng,
            [".jpg"] = ImageJpeg,
            [".jpeg"] = ImageJpeg,
            [".gif"] = ImageGif,
            [".webp"] = ImageWebP,
            [".bmp"] = ImageBmp,
            [".tiff"] = ImageTiff,
            [".tif"] = ImageTiff,
            [".heic"] = ImageHeic,
            [".heif"] = ImageHeif,
            [".avif"] = ImageAvif,
            [".svg"] = ImageSvg,
            [".ico"] = ImageIcon,

            // Audio
            [".mp3"] = AudioMpeg,
            [".wav"] = AudioWav,
            [".wave"] = AudioWav,
            [".ogg"] = AudioOgg,
            [".oga"] = AudioOgg,
            [".flac"] = AudioFlac,
            [".weba"] = AudioWebM,  // Audio-only WebM
            [".m4a"] = AudioMp4,
            [".aac"] = AudioAac,
            [".opus"] = AudioOpus,

            // Video
            [".mp4"] = VideoMp4,
            [".m4v"] = VideoMp4,
            [".webm"] = VideoWebM,  // WebM is primarily video (can contain audio track)
            [".mov"] = VideoQuickTime,
            [".avi"] = VideoAvi,
            [".mkv"] = VideoMatroska,
            [".flv"] = VideoFlv,
            [".wmv"] = VideoWmv,
            [".ogv"] = VideoOgg,
            [".3gp"] = Video3gpp,
            [".3g2"] = Video3gpp2,
            [".mpg"] = VideoMpeg,
            [".mpeg"] = VideoMpeg,

            // Documents
            [".pdf"] = ApplicationPdf,
            [".doc"] = ApplicationMsWord,
            [".docx"] = ApplicationWordOpenXml,
            [".xls"] = ApplicationMsExcel,
            [".xlsx"] = ApplicationExcelOpenXml,
            [".ppt"] = ApplicationMsPowerPoint,
            [".pptx"] = ApplicationPowerPointOpenXml,
            [".odt"] = ApplicationOpenDocumentText,
            [".ods"] = ApplicationOpenDocumentSpreadsheet,
            [".odp"] = ApplicationOpenDocumentPresentation,

            // Text
            [".txt"] = TextPlain,
            [".text"] = TextPlain,
            [".html"] = TextHtml,
            [".htm"] = TextHtml,
            [".md"] = TextMarkdown,
            [".markdown"] = TextMarkdown,
            [".mdown"] = TextMarkdown,
            [".mkd"] = TextMarkdown,
            [".csv"] = TextCsv,
            [".xml"] = TextXml,
            [".css"] = TextCss,
            [".js"] = TextJavascript,
            [".mjs"] = TextJavascript,
            [".ts"] = TextTypescript,
            [".tsx"] = TextTypescript,
            [".jsx"] = TextJavascript,
            [".json"] = ApplicationJson,
            [".yaml"] = ApplicationYaml,
            [".yml"] = ApplicationYaml,
            [".rtf"] = ApplicationRtf,

            // Archives
            [".zip"] = ApplicationZip,
            [".gz"] = ApplicationGzip,
            [".gzip"] = ApplicationGzip,
            [".7z"] = Application7z,
            [".rar"] = ApplicationRar,
            [".tar"] = ApplicationTar,
            [".bz2"] = ApplicationBzip2,
            [".xz"] = ApplicationXz,

            // Generic
            [".bin"] = ApplicationOctetStream,
        };

    #endregion

    #region MIME Type to Extensions Mapping

    private static readonly Dictionary<string, string[]> s_mimeTypeToExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            [ImagePng] = new[] { ".png" },
            [ImageJpeg] = new[] { ".jpg", ".jpeg" },
            [ImageGif] = new[] { ".gif" },
            [ImageWebP] = new[] { ".webp" },
            [ImageBmp] = new[] { ".bmp" },
            [ImageTiff] = new[] { ".tiff", ".tif" },
            [ImageHeic] = new[] { ".heic" },
            [ImageHeif] = new[] { ".heif" },
            [ImageAvif] = new[] { ".avif" },
            [ImageSvg] = new[] { ".svg" },
            [ImageIcon] = new[] { ".ico" },

            // Audio
            [AudioMpeg] = new[] { ".mp3" },
            [AudioWav] = new[] { ".wav", ".wave" },
            [AudioOgg] = new[] { ".ogg", ".oga" },
            [AudioFlac] = new[] { ".flac" },
            [AudioWebM] = new[] { ".weba" },
            [AudioMp4] = new[] { ".m4a" },
            [AudioAac] = new[] { ".aac" },
            [AudioOpus] = new[] { ".opus" },

            // Video
            [VideoMp4] = new[] { ".mp4", ".m4v" },
            [VideoWebM] = new[] { ".webm" },
            [VideoQuickTime] = new[] { ".mov" },
            [VideoAvi] = new[] { ".avi" },
            [VideoMatroska] = new[] { ".mkv" },
            [VideoFlv] = new[] { ".flv" },
            [VideoWmv] = new[] { ".wmv" },
            [VideoOgg] = new[] { ".ogv" },
            [Video3gpp] = new[] { ".3gp" },
            [Video3gpp2] = new[] { ".3g2" },
            [VideoMpeg] = new[] { ".mpg", ".mpeg" },

            // Documents
            [ApplicationPdf] = new[] { ".pdf" },
            [ApplicationMsWord] = new[] { ".doc" },
            [ApplicationWordOpenXml] = new[] { ".docx" },
            [ApplicationMsExcel] = new[] { ".xls" },
            [ApplicationExcelOpenXml] = new[] { ".xlsx" },
            [ApplicationMsPowerPoint] = new[] { ".ppt" },
            [ApplicationPowerPointOpenXml] = new[] { ".pptx" },
            [ApplicationOpenDocumentText] = new[] { ".odt" },
            [ApplicationOpenDocumentSpreadsheet] = new[] { ".ods" },
            [ApplicationOpenDocumentPresentation] = new[] { ".odp" },

            // Text
            [TextPlain] = new[] { ".txt", ".text" },
            [TextHtml] = new[] { ".html", ".htm" },
            [TextMarkdown] = new[] { ".md", ".markdown", ".mdown", ".mkd" },
            [TextCsv] = new[] { ".csv" },
            [TextXml] = new[] { ".xml" },
            [TextCss] = new[] { ".css" },
            [TextJavascript] = new[] { ".js", ".mjs", ".jsx" },
            [TextTypescript] = new[] { ".ts", ".tsx" },
            [ApplicationJson] = new[] { ".json" },
            [ApplicationYaml] = new[] { ".yaml", ".yml" },
            [ApplicationRtf] = new[] { ".rtf" },

            // Archives
            [ApplicationZip] = new[] { ".zip" },
            [ApplicationGzip] = new[] { ".gz", ".gzip" },
            [Application7z] = new[] { ".7z" },
            [ApplicationRar] = new[] { ".rar" },
            [ApplicationTar] = new[] { ".tar" },
            [ApplicationBzip2] = new[] { ".bz2" },
            [ApplicationXz] = new[] { ".xz" },

            // Generic
            [ApplicationOctetStream] = new[] { ".bin" },
        };

    #endregion

    #region MIME Type Variants (for normalization)

    private static readonly Dictionary<string, string> s_mimeTypeVariants =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Markdown variants normalize to standard
            [TextMarkdownOld1] = TextMarkdown,
            [TextMarkdownOld2] = TextMarkdown,

            // Audio/Video WebM variants (some sources use audio/webm, others video/webm)
            ["audio/x-webm"] = AudioWebM,
            ["video/x-webm"] = VideoWebM,

            // WAV variants
            [AudioWave] = AudioWav,
            ["audio/x-wav"] = AudioWav,

            // Vorbis variants (Vorbis codec typically in Ogg container)
            [AudioVorbis] = AudioOgg,

            // XML variants (both text/xml and application/xml are valid)
            [ApplicationXml] = TextXml,

            // HEIC/HEIF variants
            ["image/heic-sequence"] = ImageHeic,
            ["image/heif-sequence"] = ImageHeif,
        };

    #endregion

    #region Public API

    /// <summary>
    /// Gets the MIME type for a file extension.
    /// </summary>
    /// <param name="extension">File extension (with or without leading dot).</param>
    /// <returns>MIME type, or null if extension is not recognized.</returns>
    public static string? GetMimeType(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        // Ensure extension starts with dot
        if (!extension.StartsWith("."))
            extension = "." + extension;

        return s_extensionToMimeType.TryGetValue(extension, out var mimeType)
            ? mimeType
            : null;
    }

    /// <summary>
    /// Gets the MIME type for a file path based on its extension.
    /// </summary>
    /// <param name="filePath">File path or name.</param>
    /// <returns>MIME type, or null if extension is not recognized.</returns>
    public static string? GetMimeTypeFromPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var extension = System.IO.Path.GetExtension(filePath);
        return GetMimeType(extension);
    }

    /// <summary>
    /// Tries to get the MIME type for a file extension.
    /// </summary>
    /// <param name="extension">File extension (with or without leading dot).</param>
    /// <param name="mimeType">Output MIME type if found.</param>
    /// <returns>True if extension is recognized, false otherwise.</returns>
    public static bool TryGetMimeType(string extension, out string mimeType)
    {
        var result = GetMimeType(extension);
        mimeType = result ?? string.Empty;
        return result != null;
    }

    /// <summary>
    /// Gets the primary file extension for a MIME type.
    /// </summary>
    /// <param name="mimeType">MIME type.</param>
    /// <returns>Primary extension (with leading dot), or null if MIME type is not recognized.</returns>
    public static string? GetPrimaryExtension(string mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return null;

        // Normalize variant first
        var normalized = NormalizeMimeType(mimeType);

        return s_mimeTypeToExtensions.TryGetValue(normalized, out var extensions) && extensions.Length > 0
            ? extensions[0]
            : null;
    }

    /// <summary>
    /// Gets all file extensions for a MIME type.
    /// </summary>
    /// <param name="mimeType">MIME type.</param>
    /// <returns>Array of extensions (with leading dots), or empty array if MIME type is not recognized.</returns>
    public static string[] GetExtensions(string mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return Array.Empty<string>();

        // Normalize variant first
        var normalized = NormalizeMimeType(mimeType);

        return s_mimeTypeToExtensions.TryGetValue(normalized, out var extensions)
            ? extensions
            : Array.Empty<string>();
    }

    /// <summary>
    /// Normalizes a MIME type to its canonical form.
    /// Handles legacy variants (e.g., text/x-markdown → text/markdown).
    /// </summary>
    /// <param name="mimeType">MIME type to normalize.</param>
    /// <returns>Normalized MIME type.</returns>
    public static string NormalizeMimeType(string mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return mimeType;

        return s_mimeTypeVariants.TryGetValue(mimeType, out var normalized)
            ? normalized
            : mimeType;
    }

    /// <summary>
    /// Checks if a MIME type is supported.
    /// </summary>
    /// <param name="mimeType">MIME type to check.</param>
    /// <returns>True if supported, false otherwise.</returns>
    public static bool IsSupported(string mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return false;

        var normalized = NormalizeMimeType(mimeType);
        return s_mimeTypeToExtensions.ContainsKey(normalized);
    }

    /// <summary>
    /// Checks if a file extension is supported.
    /// </summary>
    /// <param name="extension">File extension (with or without leading dot).</param>
    /// <returns>True if supported, false otherwise.</returns>
    public static bool IsExtensionSupported(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        // Ensure extension starts with dot
        if (!extension.StartsWith("."))
            extension = "." + extension;

        return s_extensionToMimeType.ContainsKey(extension);
    }

    /// <summary>
    /// Gets all supported MIME types.
    /// </summary>
    /// <returns>Array of all supported MIME types.</returns>
    public static string[] GetSupportedMimeTypes()
    {
        return s_mimeTypeToExtensions.Keys.ToArray();
    }

    /// <summary>
    /// Gets all supported file extensions.
    /// </summary>
    /// <returns>Array of all supported extensions (with leading dots).</returns>
    public static string[] GetSupportedExtensions()
    {
        return s_extensionToMimeType.Keys.ToArray();
    }

    /// <summary>
    /// Checks if a MIME type represents an image.
    /// </summary>
    public static bool IsImage(string mimeType) =>
        !string.IsNullOrWhiteSpace(mimeType) &&
        mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if a MIME type represents audio.
    /// </summary>
    public static bool IsAudio(string mimeType) =>
        !string.IsNullOrWhiteSpace(mimeType) &&
        mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if a MIME type represents video.
    /// </summary>
    public static bool IsVideo(string mimeType) =>
        !string.IsNullOrWhiteSpace(mimeType) &&
        mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if a MIME type represents a document.
    /// </summary>
    public static bool IsDocument(string mimeType) =>
        !string.IsNullOrWhiteSpace(mimeType) &&
        (mimeType.StartsWith("application/", StringComparison.OrdinalIgnoreCase) ||
         mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase));

    #endregion
}
