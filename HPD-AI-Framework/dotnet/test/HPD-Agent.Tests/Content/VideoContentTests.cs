// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.Content;

public class VideoContentTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_DefaultsToMp4()
    {
        // Arrange
        var videoBytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        // Act
        var content = new VideoContent(videoBytes);

        // Assert
        Assert.Equal("video/mp4", content.MediaType);
    }

    [Fact]
    public void Constructor_AcceptsCustomMediaType()
    {
        // Arrange
        var videoBytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        // Act
        var content = new VideoContent(videoBytes, "video/webm");

        // Assert
        Assert.Equal("video/webm", content.MediaType);
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void Mp4_CreatesWithMp4MediaType()
    {
        // Arrange
        var videoBytes = new byte[] { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70 }; // ftyp box

        // Act
        var content = VideoContent.Mp4(videoBytes);

        // Assert
        Assert.Equal("video/mp4", content.MediaType);
        Assert.Equal(videoBytes.Length, content.Data.Length);
    }

    [Fact]
    public void WebM_CreatesWithWebMMediaType()
    {
        // Arrange
        var videoBytes = new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }; // EBML header

        // Act
        var content = VideoContent.WebM(videoBytes);

        // Assert
        Assert.Equal("video/webm", content.MediaType);
        Assert.Equal(videoBytes.Length, content.Data.Length);
    }

    [Fact]
    public void Mov_CreatesWithMovMediaType()
    {
        // Arrange
        var videoBytes = new byte[] { 0x00, 0x00, 0x00, 0x14, 0x66, 0x74, 0x79, 0x70, 0x71, 0x74, 0x20, 0x20 }; // QuickTime

        // Act
        var content = VideoContent.Mov(videoBytes);

        // Assert
        Assert.Equal("video/quicktime", content.MediaType);
        Assert.Equal(videoBytes.Length, content.Data.Length);
    }

    [Fact]
    public void Avi_CreatesWithAviMediaType()
    {
        // Arrange
        var videoBytes = new byte[] { 0x52, 0x49, 0x46, 0x46 }; // RIFF header

        // Act
        var content = VideoContent.Avi(videoBytes);

        // Assert
        Assert.Equal("video/x-msvideo", content.MediaType);
        Assert.Equal(videoBytes.Length, content.Data.Length);
    }

    #endregion

    #region FromFileAsync Tests

    [Fact]
    public async Task FromFileAsync_LoadsMp4File()
    {
        // Arrange: Create temp MP4 file
        var tempFile = Path.GetTempFileName();
        var mp4Path = Path.ChangeExtension(tempFile, ".mp4");
        var mp4Bytes = new byte[] { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70 };
        await File.WriteAllBytesAsync(mp4Path, mp4Bytes);

        try
        {
            // Act
            var content = await VideoContent.FromFileAsync(mp4Path);

            // Assert
            Assert.Equal("video/mp4", content.MediaType);
            Assert.Equal(Path.GetFileName(mp4Path), content.Name);
            Assert.Equal(mp4Bytes.Length, content.Data.Length);
        }
        finally
        {
            File.Delete(mp4Path);
        }
    }

    [Fact]
    public async Task FromFileAsync_LoadsWebMFile()
    {
        // Arrange: Create temp WebM file
        var tempFile = Path.GetTempFileName();
        var webmPath = Path.ChangeExtension(tempFile, ".webm");
        var webmBytes = new byte[] { 0x1A, 0x45, 0xDF, 0xA3 };
        await File.WriteAllBytesAsync(webmPath, webmBytes);

        try
        {
            // Act
            var content = await VideoContent.FromFileAsync(webmPath);

            // Assert
            Assert.Equal("video/webm", content.MediaType);
            Assert.Equal(Path.GetFileName(webmPath), content.Name);
        }
        finally
        {
            File.Delete(webmPath);
        }
    }

    [Fact]
    public async Task FromFileAsync_LoadsMovFile()
    {
        // Arrange: Create temp MOV file
        var tempFile = Path.GetTempFileName();
        var movPath = Path.ChangeExtension(tempFile, ".mov");
        var movBytes = new byte[] { 0x00, 0x00, 0x00, 0x14, 0x66, 0x74, 0x79, 0x70 };
        await File.WriteAllBytesAsync(movPath, movBytes);

        try
        {
            // Act
            var content = await VideoContent.FromFileAsync(movPath);

            // Assert
            Assert.Equal("video/quicktime", content.MediaType);
        }
        finally
        {
            File.Delete(movPath);
        }
    }

    [Fact]
    public async Task FromFileAsync_LoadsAviFile()
    {
        // Arrange: Create temp AVI file
        var tempFile = Path.GetTempFileName();
        var aviPath = Path.ChangeExtension(tempFile, ".avi");
        var aviBytes = new byte[] { 0x52, 0x49, 0x46, 0x46 };
        await File.WriteAllBytesAsync(aviPath, aviBytes);

        try
        {
            // Act
            var content = await VideoContent.FromFileAsync(aviPath);

            // Assert
            Assert.Equal("video/x-msvideo", content.MediaType);
        }
        finally
        {
            File.Delete(aviPath);
        }
    }

    [Fact]
    public async Task FromFileAsync_LoadsMkvFile()
    {
        // Arrange: Create temp MKV file
        var tempFile = Path.GetTempFileName();
        var mkvPath = Path.ChangeExtension(tempFile, ".mkv");
        var mkvBytes = new byte[] { 0x1A, 0x45, 0xDF, 0xA3 };
        await File.WriteAllBytesAsync(mkvPath, mkvBytes);

        try
        {
            // Act
            var content = await VideoContent.FromFileAsync(mkvPath);

            // Assert
            Assert.Equal("video/x-matroska", content.MediaType);
        }
        finally
        {
            File.Delete(mkvPath);
        }
    }

    [Fact]
    public async Task FromFileAsync_DefaultsToMp4_ForUnknownExtension()
    {
        // Arrange: Create temp file with unknown extension
        var tempFile = Path.GetTempFileName();
        var unknownPath = Path.ChangeExtension(tempFile, ".unknown");
        var videoBytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        await File.WriteAllBytesAsync(unknownPath, videoBytes);

        try
        {
            // Act
            var content = await VideoContent.FromFileAsync(unknownPath);

            // Assert
            Assert.Equal("video/mp4", content.MediaType); // Default
        }
        finally
        {
            File.Delete(unknownPath);
        }
    }

    [Fact]
    public async Task FromFileAsync_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "nonexistent.mp4");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await VideoContent.FromFileAsync(nonExistentPath));
    }

    #endregion

    #region DataUri Constructor Tests

    [Fact]
    public void Constructor_AcceptsVideoDataUri()
    {
        // Arrange: Base64-encoded video
        var videoBytes = new byte[] { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70 };
        var base64 = Convert.ToBase64String(videoBytes);
        var dataUri = $"data:video/mp4;base64,{base64}";

        // Act
        var content = new VideoContent(dataUri);

        // Assert
        Assert.Equal("video/mp4", content.MediaType);
        Assert.Equal(videoBytes.Length, content.Data.Length);
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_ForNonVideoDataUri()
    {
        // Arrange: Data URI with non-video MIME type
        var dataUri = "data:audio/mpeg;base64,//uQxAAA";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new VideoContent(dataUri));
    }

    #endregion
}
