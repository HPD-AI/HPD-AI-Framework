// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.Content;

public class AudioContentTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_DefaultsToMp3()
    {
        // Arrange
        var audioBytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        // Act
        var content = new AudioContent(audioBytes);

        // Assert
        Assert.Equal("audio/mpeg", content.MediaType);
    }

    [Fact]
    public void Constructor_AcceptsCustomMediaType()
    {
        // Arrange
        var audioBytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        // Act
        var content = new AudioContent(audioBytes, "audio/wav");

        // Assert
        Assert.Equal("audio/wav", content.MediaType);
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void Wav_CreatesWithWavMediaType()
    {
        // Arrange
        var audioBytes = new byte[] { 0x52, 0x49, 0x46, 0x46 }; // RIFF header

        // Act
        var content = AudioContent.Wav(audioBytes);

        // Assert
        Assert.Equal("audio/wav", content.MediaType);
        Assert.Equal(audioBytes.Length, content.Data.Length);
    }

    [Fact]
    public void Mp3_CreatesWithMp3MediaType()
    {
        // Arrange
        var audioBytes = new byte[] { 0xFF, 0xFB, 0x90, 0x00 }; // MP3 frame header

        // Act
        var content = AudioContent.Mp3(audioBytes);

        // Assert
        Assert.Equal("audio/mpeg", content.MediaType);
        Assert.Equal(audioBytes.Length, content.Data.Length);
    }

    [Fact]
    public void Ogg_CreatesWithOggMediaType()
    {
        // Arrange
        var audioBytes = new byte[] { 0x4F, 0x67, 0x67, 0x53 }; // OggS header

        // Act
        var content = AudioContent.Ogg(audioBytes);

        // Assert
        Assert.Equal("audio/ogg", content.MediaType);
        Assert.Equal(audioBytes.Length, content.Data.Length);
    }

    [Fact]
    public void Flac_CreatesWithFlacMediaType()
    {
        // Arrange
        var audioBytes = new byte[] { 0x66, 0x4C, 0x61, 0x43 }; // fLaC header

        // Act
        var content = AudioContent.Flac(audioBytes);

        // Assert
        Assert.Equal("audio/flac", content.MediaType);
        Assert.Equal(audioBytes.Length, content.Data.Length);
    }

    [Fact]
    public void WebM_CreatesWithWebMMediaType()
    {
        // Arrange
        var audioBytes = new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }; // EBML header

        // Act
        var content = AudioContent.WebM(audioBytes);

        // Assert
        Assert.Equal("audio/webm", content.MediaType);
        Assert.Equal(audioBytes.Length, content.Data.Length);
    }

    [Fact]
    public void M4a_CreatesWithM4aMediaType()
    {
        // Arrange
        var audioBytes = new byte[] { 0x00, 0x00, 0x00, 0x20 };

        // Act
        var content = AudioContent.M4a(audioBytes);

        // Assert
        Assert.Equal("audio/mp4", content.MediaType);
        Assert.Equal(audioBytes.Length, content.Data.Length);
    }

    #endregion

    #region FromFileAsync Tests

    [Fact]
    public async Task FromFileAsync_LoadsWavFile()
    {
        // Arrange: Create temp WAV file
        var tempFile = Path.GetTempFileName();
        var wavPath = Path.ChangeExtension(tempFile, ".wav");
        var wavBytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00 };
        await File.WriteAllBytesAsync(wavPath, wavBytes);

        try
        {
            // Act
            var content = await AudioContent.FromFileAsync(wavPath);

            // Assert
            Assert.Equal("audio/wav", content.MediaType);
            Assert.Equal(Path.GetFileName(wavPath), content.Name);
            Assert.Equal(wavBytes.Length, content.Data.Length);
        }
        finally
        {
            File.Delete(wavPath);
        }
    }

    [Fact]
    public async Task FromFileAsync_LoadsMp3File()
    {
        // Arrange: Create temp MP3 file
        var tempFile = Path.GetTempFileName();
        var mp3Path = Path.ChangeExtension(tempFile, ".mp3");
        var mp3Bytes = new byte[] { 0xFF, 0xFB, 0x90, 0x00 };
        await File.WriteAllBytesAsync(mp3Path, mp3Bytes);

        try
        {
            // Act
            var content = await AudioContent.FromFileAsync(mp3Path);

            // Assert
            Assert.Equal("audio/mpeg", content.MediaType);
            Assert.Equal(Path.GetFileName(mp3Path), content.Name);
        }
        finally
        {
            File.Delete(mp3Path);
        }
    }

    [Fact]
    public async Task FromFileAsync_LoadsOggFile()
    {
        // Arrange: Create temp OGG file
        var tempFile = Path.GetTempFileName();
        var oggPath = Path.ChangeExtension(tempFile, ".ogg");
        var oggBytes = new byte[] { 0x4F, 0x67, 0x67, 0x53 };
        await File.WriteAllBytesAsync(oggPath, oggBytes);

        try
        {
            // Act
            var content = await AudioContent.FromFileAsync(oggPath);

            // Assert
            Assert.Equal("audio/ogg", content.MediaType);
        }
        finally
        {
            File.Delete(oggPath);
        }
    }

    [Fact]
    public async Task FromFileAsync_LoadsFlacFile()
    {
        // Arrange: Create temp FLAC file
        var tempFile = Path.GetTempFileName();
        var flacPath = Path.ChangeExtension(tempFile, ".flac");
        var flacBytes = new byte[] { 0x66, 0x4C, 0x61, 0x43 };
        await File.WriteAllBytesAsync(flacPath, flacBytes);

        try
        {
            // Act
            var content = await AudioContent.FromFileAsync(flacPath);

            // Assert
            Assert.Equal("audio/flac", content.MediaType);
        }
        finally
        {
            File.Delete(flacPath);
        }
    }

    [Fact]
    public async Task FromFileAsync_LoadsM4aFile()
    {
        // Arrange: Create temp M4A file
        var tempFile = Path.GetTempFileName();
        var m4aPath = Path.ChangeExtension(tempFile, ".m4a");
        var m4aBytes = new byte[] { 0x00, 0x00, 0x00, 0x20 };
        await File.WriteAllBytesAsync(m4aPath, m4aBytes);

        try
        {
            // Act
            var content = await AudioContent.FromFileAsync(m4aPath);

            // Assert
            Assert.Equal("audio/mp4", content.MediaType);
        }
        finally
        {
            File.Delete(m4aPath);
        }
    }

    [Fact]
    public async Task FromFileAsync_LoadsAacFile()
    {
        // Arrange: Create temp AAC file
        var tempFile = Path.GetTempFileName();
        var aacPath = Path.ChangeExtension(tempFile, ".aac");
        var aacBytes = new byte[] { 0xFF, 0xF1, 0x50, 0x80 };
        await File.WriteAllBytesAsync(aacPath, aacBytes);

        try
        {
            // Act
            var content = await AudioContent.FromFileAsync(aacPath);

            // Assert
            Assert.Equal("audio/aac", content.MediaType);
        }
        finally
        {
            File.Delete(aacPath);
        }
    }

    [Fact]
    public async Task FromFileAsync_DefaultsToMp3_ForUnknownExtension()
    {
        // Arrange: Create temp file with unknown extension
        var tempFile = Path.GetTempFileName();
        var unknownPath = Path.ChangeExtension(tempFile, ".unknown");
        var audioBytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        await File.WriteAllBytesAsync(unknownPath, audioBytes);

        try
        {
            // Act
            var content = await AudioContent.FromFileAsync(unknownPath);

            // Assert
            Assert.Equal("audio/mpeg", content.MediaType); // Default
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
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "nonexistent.mp3");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await AudioContent.FromFileAsync(nonExistentPath));
    }

    #endregion

    #region DataUri Constructor Tests

    [Fact]
    public void Constructor_AcceptsAudioDataUri()
    {
        // Arrange: Base64-encoded audio
        var audioBytes = new byte[] { 0xFF, 0xFB, 0x90, 0x00 };
        var base64 = Convert.ToBase64String(audioBytes);
        var dataUri = $"data:audio/mpeg;base64,{base64}";

        // Act
        var content = new AudioContent(dataUri);

        // Assert
        Assert.Equal("audio/mpeg", content.MediaType);
        Assert.Equal(audioBytes.Length, content.Data.Length);
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_ForNonAudioDataUri()
    {
        // Arrange: Data URI with non-audio MIME type
        var dataUri = "data:image/png;base64,iVBORw0KGgo=";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new AudioContent(dataUri));
    }

    #endregion
}
