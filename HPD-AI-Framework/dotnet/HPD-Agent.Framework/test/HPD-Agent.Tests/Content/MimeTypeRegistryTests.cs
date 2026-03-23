// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using System;
using System.Linq;
using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.Content;

public class MimeTypeRegistryTests
{
    #region GetMimeType Tests

    [Theory]
    [InlineData(".png", MimeTypeRegistry.ImagePng)]
    [InlineData(".jpg", MimeTypeRegistry.ImageJpeg)]
    [InlineData(".jpeg", MimeTypeRegistry.ImageJpeg)]
    [InlineData(".gif", MimeTypeRegistry.ImageGif)]
    [InlineData(".webp", MimeTypeRegistry.ImageWebP)]
    [InlineData(".svg", MimeTypeRegistry.ImageSvg)]
    [InlineData(".heic", MimeTypeRegistry.ImageHeic)]
    [InlineData(".avif", MimeTypeRegistry.ImageAvif)]
    public void GetMimeType_ReturnsCorrectType_ForImageExtensions(string extension, string expectedMimeType)
    {
        // Act
        var mimeType = MimeTypeRegistry.GetMimeType(extension);

        // Assert
        Assert.Equal(expectedMimeType, mimeType);
    }

    [Theory]
    [InlineData(".mp3", MimeTypeRegistry.AudioMpeg)]
    [InlineData(".wav", MimeTypeRegistry.AudioWav)]
    [InlineData(".ogg", MimeTypeRegistry.AudioOgg)]
    [InlineData(".flac", MimeTypeRegistry.AudioFlac)]
    [InlineData(".m4a", MimeTypeRegistry.AudioMp4)]
    [InlineData(".aac", MimeTypeRegistry.AudioAac)]
    public void GetMimeType_ReturnsCorrectType_ForAudioExtensions(string extension, string expectedMimeType)
    {
        // Act
        var mimeType = MimeTypeRegistry.GetMimeType(extension);

        // Assert
        Assert.Equal(expectedMimeType, mimeType);
    }

    [Theory]
    [InlineData(".mp4", MimeTypeRegistry.VideoMp4)]
    [InlineData(".webm", MimeTypeRegistry.VideoWebM)]
    [InlineData(".mov", MimeTypeRegistry.VideoQuickTime)]
    [InlineData(".avi", MimeTypeRegistry.VideoAvi)]
    [InlineData(".mkv", MimeTypeRegistry.VideoMatroska)]
    public void GetMimeType_ReturnsCorrectType_ForVideoExtensions(string extension, string expectedMimeType)
    {
        // Act
        var mimeType = MimeTypeRegistry.GetMimeType(extension);

        // Assert
        Assert.Equal(expectedMimeType, mimeType);
    }

    [Theory]
    [InlineData(".pdf", MimeTypeRegistry.ApplicationPdf)]
    [InlineData(".docx", MimeTypeRegistry.ApplicationWordOpenXml)]
    [InlineData(".xlsx", MimeTypeRegistry.ApplicationExcelOpenXml)]
    [InlineData(".pptx", MimeTypeRegistry.ApplicationPowerPointOpenXml)]
    [InlineData(".html", MimeTypeRegistry.TextHtml)]
    [InlineData(".md", MimeTypeRegistry.TextMarkdown)]
    public void GetMimeType_ReturnsCorrectType_ForDocumentExtensions(string extension, string expectedMimeType)
    {
        // Act
        var mimeType = MimeTypeRegistry.GetMimeType(extension);

        // Assert
        Assert.Equal(expectedMimeType, mimeType);
    }

    [Fact]
    public void GetMimeType_IsCaseInsensitive()
    {
        // Arrange
        var extensions = new[] { ".PNG", ".JpG", ".GIF", ".PDF", ".DOCX" };

        // Act & Assert
        foreach (var ext in extensions)
        {
            var mimeType = MimeTypeRegistry.GetMimeType(ext);
            Assert.NotNull(mimeType);
        }
    }

    [Fact]
    public void GetMimeType_WorksWithoutLeadingDot()
    {
        // Act
        var mimeType = MimeTypeRegistry.GetMimeType("png");

        // Assert
        Assert.Equal(MimeTypeRegistry.ImagePng, mimeType);
    }

    [Fact]
    public void GetMimeType_ReturnsNull_ForUnknownExtension()
    {
        // Act
        var mimeType = MimeTypeRegistry.GetMimeType(".unknown");

        // Assert
        Assert.Null(mimeType);
    }

    [Fact]
    public void GetMimeType_ReturnsNull_ForNullOrEmpty()
    {
        // Act
        var mimeType1 = MimeTypeRegistry.GetMimeType(null!);
        var mimeType2 = MimeTypeRegistry.GetMimeType(string.Empty);
        var mimeType3 = MimeTypeRegistry.GetMimeType("   ");

        // Assert
        Assert.Null(mimeType1);
        Assert.Null(mimeType2);
        Assert.Null(mimeType3);
    }

    #endregion

    #region GetMimeTypeFromPath Tests

    [Theory]
    [InlineData("image.png", MimeTypeRegistry.ImagePng)]
    [InlineData("/path/to/document.pdf", MimeTypeRegistry.ApplicationPdf)]
    [InlineData("C:\\Users\\file.docx", MimeTypeRegistry.ApplicationWordOpenXml)]
    [InlineData("audio.mp3", MimeTypeRegistry.AudioMpeg)]
    public void GetMimeTypeFromPath_ExtractsExtension_AndReturnsMimeType(string filePath, string expectedMimeType)
    {
        // Act
        var mimeType = MimeTypeRegistry.GetMimeTypeFromPath(filePath);

        // Assert
        Assert.Equal(expectedMimeType, mimeType);
    }

    [Fact]
    public void GetMimeTypeFromPath_ReturnsNull_ForPathWithoutExtension()
    {
        // Act
        var mimeType = MimeTypeRegistry.GetMimeTypeFromPath("README");

        // Assert
        Assert.Null(mimeType);
    }

    #endregion

    #region TryGetMimeType Tests

    [Fact]
    public void TryGetMimeType_ReturnsTrue_ForKnownExtension()
    {
        // Act
        var result = MimeTypeRegistry.TryGetMimeType(".png", out var mimeType);

        // Assert
        Assert.True(result);
        Assert.Equal(MimeTypeRegistry.ImagePng, mimeType);
    }

    [Fact]
    public void TryGetMimeType_ReturnsFalse_ForUnknownExtension()
    {
        // Act
        var result = MimeTypeRegistry.TryGetMimeType(".unknown", out var mimeType);

        // Assert
        Assert.False(result);
        Assert.Empty(mimeType);
    }

    #endregion

    #region GetPrimaryExtension Tests

    [Theory]
    [InlineData(MimeTypeRegistry.ImagePng, ".png")]
    [InlineData(MimeTypeRegistry.ImageJpeg, ".jpg")]
    [InlineData(MimeTypeRegistry.AudioMpeg, ".mp3")]
    [InlineData(MimeTypeRegistry.VideoMp4, ".mp4")]
    [InlineData(MimeTypeRegistry.ApplicationPdf, ".pdf")]
    public void GetPrimaryExtension_ReturnsCorrectExtension_ForMimeType(string mimeType, string expectedExtension)
    {
        // Act
        var extension = MimeTypeRegistry.GetPrimaryExtension(mimeType);

        // Assert
        Assert.Equal(expectedExtension, extension);
    }

    [Fact]
    public void GetPrimaryExtension_ReturnsNull_ForUnknownMimeType()
    {
        // Act
        var extension = MimeTypeRegistry.GetPrimaryExtension("application/x-unknown");

        // Assert
        Assert.Null(extension);
    }

    #endregion

    #region GetExtensions Tests

    [Fact]
    public void GetExtensions_ReturnsMultiple_ForJpeg()
    {
        // Act
        var extensions = MimeTypeRegistry.GetExtensions(MimeTypeRegistry.ImageJpeg);

        // Assert
        Assert.Contains(".jpg", extensions);
        Assert.Contains(".jpeg", extensions);
    }

    [Fact]
    public void GetExtensions_ReturnsMultiple_ForTiff()
    {
        // Act
        var extensions = MimeTypeRegistry.GetExtensions(MimeTypeRegistry.ImageTiff);

        // Assert
        Assert.Contains(".tiff", extensions);
        Assert.Contains(".tif", extensions);
    }

    [Fact]
    public void GetExtensions_ReturnsEmpty_ForUnknownMimeType()
    {
        // Act
        var extensions = MimeTypeRegistry.GetExtensions("application/x-unknown");

        // Assert
        Assert.Empty(extensions);
    }

    #endregion

    #region NormalizeMimeType Tests

    [Theory]
    [InlineData("text/x-markdown", MimeTypeRegistry.TextMarkdown)]
    [InlineData("text/plain-markdown", MimeTypeRegistry.TextMarkdown)]
    [InlineData("audio/x-wav", MimeTypeRegistry.AudioWav)]
    public void NormalizeMimeType_NormalizesVariants(string variant, string expectedNormalized)
    {
        // Act
        var normalized = MimeTypeRegistry.NormalizeMimeType(variant);

        // Assert
        Assert.Equal(expectedNormalized, normalized);
    }

    [Fact]
    public void NormalizeMimeType_ReturnsOriginal_ForStandardType()
    {
        // Act
        var normalized = MimeTypeRegistry.NormalizeMimeType(MimeTypeRegistry.ImagePng);

        // Assert
        Assert.Equal(MimeTypeRegistry.ImagePng, normalized);
    }

    [Fact]
    public void NormalizeMimeType_IsCaseInsensitive()
    {
        // Act
        var normalized = MimeTypeRegistry.NormalizeMimeType("TEXT/X-MARKDOWN");

        // Assert
        Assert.Equal(MimeTypeRegistry.TextMarkdown, normalized);
    }

    #endregion

    #region IsSupported Tests

    [Theory]
    [InlineData(MimeTypeRegistry.ImagePng)]
    [InlineData(MimeTypeRegistry.AudioMpeg)]
    [InlineData(MimeTypeRegistry.VideoMp4)]
    [InlineData(MimeTypeRegistry.ApplicationPdf)]
    public void IsSupported_ReturnsTrue_ForSupportedMimeTypes(string mimeType)
    {
        // Act
        var isSupported = MimeTypeRegistry.IsSupported(mimeType);

        // Assert
        Assert.True(isSupported);
    }

    [Fact]
    public void IsSupported_ReturnsTrue_ForVariants()
    {
        // Act
        var isSupported = MimeTypeRegistry.IsSupported("text/x-markdown");

        // Assert
        Assert.True(isSupported);
    }

    [Fact]
    public void IsSupported_ReturnsFalse_ForUnsupportedMimeType()
    {
        // Act
        var isSupported = MimeTypeRegistry.IsSupported("application/x-unknown");

        // Assert
        Assert.False(isSupported);
    }

    #endregion

    #region IsExtensionSupported Tests

    [Theory]
    [InlineData(".png")]
    [InlineData(".mp3")]
    [InlineData(".mp4")]
    [InlineData(".pdf")]
    public void IsExtensionSupported_ReturnsTrue_ForSupportedExtensions(string extension)
    {
        // Act
        var isSupported = MimeTypeRegistry.IsExtensionSupported(extension);

        // Assert
        Assert.True(isSupported);
    }

    [Fact]
    public void IsExtensionSupported_WorksWithoutLeadingDot()
    {
        // Act
        var isSupported = MimeTypeRegistry.IsExtensionSupported("png");

        // Assert
        Assert.True(isSupported);
    }

    [Fact]
    public void IsExtensionSupported_ReturnsFalse_ForUnsupportedExtension()
    {
        // Act
        var isSupported = MimeTypeRegistry.IsExtensionSupported(".unknown");

        // Assert
        Assert.False(isSupported);
    }

    #endregion

    #region Category Helper Tests

    [Theory]
    [InlineData(MimeTypeRegistry.ImagePng)]
    [InlineData(MimeTypeRegistry.ImageJpeg)]
    [InlineData(MimeTypeRegistry.ImageSvg)]
    public void IsImage_ReturnsTrue_ForImageMimeTypes(string mimeType)
    {
        // Act
        var isImage = MimeTypeRegistry.IsImage(mimeType);

        // Assert
        Assert.True(isImage);
    }

    [Theory]
    [InlineData(MimeTypeRegistry.AudioMpeg)]
    [InlineData(MimeTypeRegistry.AudioWav)]
    public void IsAudio_ReturnsTrue_ForAudioMimeTypes(string mimeType)
    {
        // Act
        var isAudio = MimeTypeRegistry.IsAudio(mimeType);

        // Assert
        Assert.True(isAudio);
    }

    [Theory]
    [InlineData(MimeTypeRegistry.VideoMp4)]
    [InlineData(MimeTypeRegistry.VideoWebM)]
    public void IsVideo_ReturnsTrue_ForVideoMimeTypes(string mimeType)
    {
        // Act
        var isVideo = MimeTypeRegistry.IsVideo(mimeType);

        // Assert
        Assert.True(isVideo);
    }

    [Theory]
    [InlineData(MimeTypeRegistry.ApplicationPdf)]
    [InlineData(MimeTypeRegistry.TextHtml)]
    [InlineData(MimeTypeRegistry.ApplicationJson)]
    public void IsDocument_ReturnsTrue_ForDocumentMimeTypes(string mimeType)
    {
        // Act
        var isDocument = MimeTypeRegistry.IsDocument(mimeType);

        // Assert
        Assert.True(isDocument);
    }

    [Fact]
    public void IsImage_ReturnsFalse_ForNonImageType()
    {
        // Act
        var isImage = MimeTypeRegistry.IsImage(MimeTypeRegistry.AudioMpeg);

        // Assert
        Assert.False(isImage);
    }

    #endregion

    #region GetSupportedMimeTypes Tests

    [Fact]
    public void GetSupportedMimeTypes_ReturnsNonEmptyArray()
    {
        // Act
        var mimeTypes = MimeTypeRegistry.GetSupportedMimeTypes();

        // Assert
        Assert.NotEmpty(mimeTypes);
        Assert.Contains(MimeTypeRegistry.ImagePng, mimeTypes);
        Assert.Contains(MimeTypeRegistry.AudioMpeg, mimeTypes);
        Assert.Contains(MimeTypeRegistry.VideoMp4, mimeTypes);
        Assert.Contains(MimeTypeRegistry.ApplicationPdf, mimeTypes);
    }

    [Fact]
    public void GetSupportedMimeTypes_ContainsExpectedCount()
    {
        // Act
        var mimeTypes = MimeTypeRegistry.GetSupportedMimeTypes();

        // Assert: Should have 50+ MIME types
        Assert.True(mimeTypes.Length >= 50, $"Expected at least 50 MIME types, got {mimeTypes.Length}");
    }

    #endregion

    #region GetSupportedExtensions Tests

    [Fact]
    public void GetSupportedExtensions_ReturnsNonEmptyArray()
    {
        // Act
        var extensions = MimeTypeRegistry.GetSupportedExtensions();

        // Assert
        Assert.NotEmpty(extensions);
        Assert.Contains(".png", extensions);
        Assert.Contains(".mp3", extensions);
        Assert.Contains(".mp4", extensions);
        Assert.Contains(".pdf", extensions);
    }

    [Fact]
    public void GetSupportedExtensions_ContainsExpectedCount()
    {
        // Act
        var extensions = MimeTypeRegistry.GetSupportedExtensions();

        // Assert: Should have 70+ extensions (more than MIME types due to aliases)
        Assert.True(extensions.Length >= 70, $"Expected at least 70 extensions, got {extensions.Length}");
    }

    #endregion

    #region Consistency Tests

    [Fact]
    public void Registry_HasBidirectionalMapping()
    {
        // Get all extensions
        var extensions = MimeTypeRegistry.GetSupportedExtensions();

        foreach (var ext in extensions)
        {
            // Extension → MIME type
            var mimeType = MimeTypeRegistry.GetMimeType(ext);
            Assert.NotNull(mimeType);

            // MIME type → Extensions (should contain original)
            var mappedExtensions = MimeTypeRegistry.GetExtensions(mimeType!);
            Assert.Contains(ext, mappedExtensions);
        }
    }

    [Fact]
    public void Registry_AllConstantsAreSupported()
    {
        // Get all public const fields
        var constFields = typeof(MimeTypeRegistry)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && !f.Name.Contains("Old"));

        foreach (var field in constFields)
        {
            var mimeType = (string)field.GetValue(null)!;

            // Skip wildcard MIME types (e.g., image/*, video/*)
            if (mimeType.EndsWith("/*"))
                continue;

            // Each constant should be supported or be a variant
            var isSupported = MimeTypeRegistry.IsSupported(mimeType);
            Assert.True(isSupported, $"MIME type constant {field.Name} ({mimeType}) is not supported");
        }
    }

    #endregion
}
