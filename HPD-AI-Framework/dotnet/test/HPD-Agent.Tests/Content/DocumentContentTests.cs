// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.Content;

public class DocumentContentTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_RequiresMediaType()
    {
        // Arrange
        var docBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }; // PDF magic bytes

        // Act
        var content = new DocumentContent(docBytes, "application/pdf");

        // Assert
        Assert.Equal("application/pdf", content.MediaType);
        Assert.Equal(docBytes.Length, content.Data.Length);
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void Pdf_CreatesWithPdfMediaType()
    {
        // Arrange
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }; // %PDF-

        // Act
        var content = DocumentContent.Pdf(pdfBytes);

        // Assert
        Assert.Equal("application/pdf", content.MediaType);
        Assert.Equal(pdfBytes.Length, content.Data.Length);
    }

    [Fact]
    public void Word_CreatesWithWordMediaType()
    {
        // Arrange
        var docxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 }; // ZIP header (docx is a ZIP)

        // Act
        var content = DocumentContent.Word(docxBytes);

        // Assert
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", content.MediaType);
        Assert.Equal(docxBytes.Length, content.Data.Length);
    }

    [Fact]
    public void Excel_CreatesWithExcelMediaType()
    {
        // Arrange
        var xlsxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 };

        // Act
        var content = DocumentContent.Excel(xlsxBytes);

        // Assert
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", content.MediaType);
    }

    [Fact]
    public void PowerPoint_CreatesWithPowerPointMediaType()
    {
        // Arrange
        var pptxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 };

        // Act
        var content = DocumentContent.PowerPoint(pptxBytes);

        // Assert
        Assert.Equal("application/vnd.openxmlformats-officedocument.presentationml.presentation", content.MediaType);
    }

    [Fact]
    public void Html_CreatesWithHtmlMediaType()
    {
        // Arrange
        var htmlBytes = System.Text.Encoding.UTF8.GetBytes("<html><body>Hello</body></html>");

        // Act
        var content = DocumentContent.Html(htmlBytes);

        // Assert
        Assert.Equal("text/html", content.MediaType);
    }

    [Fact]
    public void Markdown_CreatesWithMarkdownMediaType()
    {
        // Arrange
        var mdBytes = System.Text.Encoding.UTF8.GetBytes("# Hello World");

        // Act
        var content = DocumentContent.Markdown(mdBytes);

        // Assert
        Assert.Equal("text/markdown", content.MediaType);
    }

    #endregion

    #region HasValidFormat Tests

    [Fact]
    public void HasValidFormat_ReturnsTrue_ForValidPdf()
    {
        // Arrange: PDF magic bytes
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 }; // %PDF-1.7
        var content = DocumentContent.Pdf(pdfBytes);

        // Act
        var isValid = content.HasValidFormat();

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void HasValidFormat_ReturnsFalse_ForInvalidPdf()
    {
        // Arrange: Not PDF bytes
        var invalidBytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        var content = new DocumentContent(invalidBytes, "application/pdf");

        // Act
        var isValid = content.HasValidFormat();

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void HasValidFormat_ReturnsTrue_ForValidDocx()
    {
        // Arrange: ZIP magic bytes (docx is a ZIP file)
        var zipBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        var content = DocumentContent.Word(zipBytes);

        // Act
        var isValid = content.HasValidFormat();

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void HasValidFormat_ReturnsTrue_ForValidXlsx()
    {
        // Arrange: ZIP magic bytes
        var zipBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        var content = DocumentContent.Excel(zipBytes);

        // Act
        var isValid = content.HasValidFormat();

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void HasValidFormat_ReturnsTrue_ForTextFormats()
    {
        // Arrange: Text formats don't have magic bytes, so always valid
        var textBytes = System.Text.Encoding.UTF8.GetBytes("Hello World");
        var content = DocumentContent.Html(textBytes);

        // Act
        var isValid = content.HasValidFormat();

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void HasValidFormat_ReturnsFalse_ForEmptyData()
    {
        // Arrange
        var content = new DocumentContent(ReadOnlyMemory<byte>.Empty, "application/pdf");

        // Act
        var isValid = content.HasValidFormat();

        // Assert
        Assert.False(isValid);
    }

    #endregion

    #region DetectFormat Tests

    [Fact]
    public void DetectFormat_ReturnsPdf_ForPdfBytes()
    {
        // Arrange
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }; // %PDF-
        var content = DocumentContent.Pdf(pdfBytes);

        // Act
        var detected = content.DetectFormat();

        // Assert
        Assert.Equal("application/pdf", detected);
    }

    [Fact]
    public void DetectFormat_ReturnsZip_ForDocxBytes()
    {
        // Arrange: docx is a ZIP, so it detects as ZIP
        var zipBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        var content = DocumentContent.Word(zipBytes);

        // Act
        var detected = content.DetectFormat();

        // Assert
        Assert.Equal("application/zip", detected);
    }

    [Fact]
    public void DetectFormat_ReturnsNull_ForTextFormats()
    {
        // Arrange: Text formats don't have magic bytes
        var textBytes = System.Text.Encoding.UTF8.GetBytes("Hello World");
        var content = DocumentContent.Html(textBytes);

        // Act
        var detected = content.DetectFormat();

        // Assert
        Assert.Null(detected);
    }

    [Fact]
    public void DetectFormat_ReturnsNull_ForEmptyData()
    {
        // Arrange
        var content = new DocumentContent(ReadOnlyMemory<byte>.Empty, "application/pdf");

        // Act
        var detected = content.DetectFormat();

        // Assert
        Assert.Null(detected);
    }

    #endregion

    #region FromFileAsync Tests

    [Fact]
    public async Task FromFileAsync_LoadsPdfFile()
    {
        // Arrange: Create temp PDF file
        var tempFile = Path.GetTempFileName();
        var pdfPath = Path.ChangeExtension(tempFile, ".pdf");
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };
        await File.WriteAllBytesAsync(pdfPath, pdfBytes);

        try
        {
            // Act
            var content = await DocumentContent.FromFileAsync(pdfPath);

            // Assert
            Assert.Equal("application/pdf", content.MediaType);
            Assert.Equal(Path.GetFileName(pdfPath), content.Name);
            Assert.Equal(pdfBytes.Length, content.Data.Length);
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task FromFileAsync_LoadsDocxFile()
    {
        // Arrange: Create temp DOCX file
        var tempFile = Path.GetTempFileName();
        var docxPath = Path.ChangeExtension(tempFile, ".docx");
        var docxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        await File.WriteAllBytesAsync(docxPath, docxBytes);

        try
        {
            // Act
            var content = await DocumentContent.FromFileAsync(docxPath);

            // Assert
            Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", content.MediaType);
        }
        finally
        {
            File.Delete(docxPath);
        }
    }

    [Fact]
    public async Task FromFileAsync_LoadsHtmlFile()
    {
        // Arrange: Create temp HTML file
        var tempFile = Path.GetTempFileName();
        var htmlPath = Path.ChangeExtension(tempFile, ".html");
        var htmlBytes = System.Text.Encoding.UTF8.GetBytes("<html><body>Test</body></html>");
        await File.WriteAllBytesAsync(htmlPath, htmlBytes);

        try
        {
            // Act
            var content = await DocumentContent.FromFileAsync(htmlPath);

            // Assert
            Assert.Equal("text/html", content.MediaType);
        }
        finally
        {
            File.Delete(htmlPath);
        }
    }

    [Fact]
    public async Task FromFileAsync_LoadsMarkdownFile()
    {
        // Arrange: Create temp Markdown file
        var tempFile = Path.GetTempFileName();
        var mdPath = Path.ChangeExtension(tempFile, ".md");
        var mdBytes = System.Text.Encoding.UTF8.GetBytes("# Hello World");
        await File.WriteAllBytesAsync(mdPath, mdBytes);

        try
        {
            // Act
            var content = await DocumentContent.FromFileAsync(mdPath);

            // Assert
            Assert.Equal("text/markdown", content.MediaType);
        }
        finally
        {
            File.Delete(mdPath);
        }
    }

    [Fact]
    public async Task FromFileAsync_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "nonexistent.pdf");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await DocumentContent.FromFileAsync(nonExistentPath));
    }

    #endregion

    #region FromUrlAsync Tests

    [Fact]
    public async Task FromUrlAsync_DownloadsDocument_WithCustomHttpClient()
    {
        // Arrange: Mock HttpClient that returns PDF bytes
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var mockHandler = new MockHttpMessageHandler(pdfBytes, "application/pdf");
        var httpClient = new HttpClient(mockHandler);
        var url = "https://example.com/document.pdf";

        // Act
        var content = await DocumentContent.FromUrlAsync(url, httpClient);

        // Assert
        Assert.Equal("application/pdf", content.MediaType);
        Assert.Equal("document.pdf", content.Name);
        Assert.Equal(pdfBytes.Length, content.Data.Length);
    }

    [Fact]
    public async Task FromUrlAsync_FallsBackToExtension_WhenNoContentType()
    {
        // Arrange: Mock HttpClient with no Content-Type header
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var mockHandler = new MockHttpMessageHandler(pdfBytes, null);
        var httpClient = new HttpClient(mockHandler);
        var url = "https://example.com/document.pdf";

        // Act
        var content = await DocumentContent.FromUrlAsync(url, httpClient);

        // Assert
        Assert.Equal("application/pdf", content.MediaType); // From extension
    }

    #endregion

    #region DataUri Constructor Tests

    [Fact]
    public void Constructor_AcceptsDocumentDataUri()
    {
        // Arrange: Base64-encoded PDF
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var base64 = Convert.ToBase64String(pdfBytes);
        var dataUri = $"data:application/pdf;base64,{base64}";

        // Act
        var content = new DocumentContent(dataUri);

        // Assert
        Assert.Equal("application/pdf", content.MediaType);
        Assert.Equal(pdfBytes.Length, content.Data.Length);
    }

    #endregion

    // Helper class for mocking HttpClient
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _responseBytes;
        private readonly string? _contentType;

        public MockHttpMessageHandler(byte[] responseBytes, string? contentType)
        {
            _responseBytes = responseBytes;
            _contentType = contentType;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_responseBytes)
            };

            if (_contentType != null)
            {
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_contentType);
            }

            return Task.FromResult(response);
        }
    }
}
