// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using HPD.Agent;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Content;

public class AudioContentTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_RequiresMediaType()
    {
        // Arrange
        var audioBytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        var exception = Assert.Throws<ArgumentException>(() => new AudioContent(audioBytes, ""));

        Assert.Contains("media type", exception.Message);
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
    public async Task FromFileAsync_ThrowsNotSupportedException_ForUnknownExtension()
    {
        // Arrange: Create temp file with unknown extension
        var tempFile = Path.GetTempFileName();
        var unknownPath = Path.ChangeExtension(tempFile, ".unknown");
        var audioBytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        await File.WriteAllBytesAsync(unknownPath, audioBytes);

        try
        {
            var exception = await Assert.ThrowsAsync<NotSupportedException>(
                async () => await AudioContent.FromFileAsync(unknownPath));

            Assert.Contains(".unknown", exception.Message);
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

    #region Realtime Input Tests

    [Fact]
    public void Pcm_CreatesWithSampleRateMediaType()
    {
        var content = AudioContent.Pcm(new byte[] { 1, 2, 3, 4 }, 16000);

        Assert.Equal("audio/pcm;rate=16000", content.MediaType);
        Assert.Equal(16000, AudioContent.GetSampleRate(content.MediaType));
        Assert.Equal("audio/pcm", AudioContent.GetRealtimeInputAudioFormatMediaType(content.MediaType));
    }

    [Fact]
    public void FromDataContent_WrapsAudioAndPreservesName()
    {
        var data = new DataContent(new byte[] { 1, 2, 3 }, "audio/mpeg") { Name = "input.mp3" };

        var content = AudioContent.FromDataContent(data);

        Assert.Equal("audio/mpeg", content.MediaType);
        Assert.Equal("input.mp3", content.Name);
        Assert.Equal(data.Data.ToArray(), content.Data.ToArray());
    }

    [Fact]
    public void FromDataContent_RejectsNonAudio()
    {
        var data = new DataContent(new byte[] { 1, 2, 3 }, "text/plain");

        Assert.Throws<ArgumentException>(() => AudioContent.FromDataContent(data));
    }

    [Fact]
    public void ToRealtimeInputAudio_PreservesCompatiblePcm()
    {
        var content = AudioContent.Pcm(new byte[] { 1, 2, 3, 4 }, 16000);
        content.Name = "input.pcm";

        var realtime = content.ToRealtimeInputAudio();

        Assert.NotSame(content, realtime);
        Assert.Equal("audio/pcm;rate=16000", realtime.MediaType);
        Assert.Equal("input.pcm", realtime.Name);
        Assert.Equal(content.Data.ToArray(), realtime.Data.ToArray());
    }

    [Fact]
    public void ToRealtimeInputAudio_ConvertsWavToRealtimePcm()
    {
        var content = AudioContent.Wav(CreatePcm16Wav(
            sampleRate: 16000,
            channelCount: 1,
            samples: [0, 1200, -1200, 0]));
        content.Name = "input.wav";

        var realtime = content.ToRealtimeInputAudio();

        Assert.Equal("audio/pcm;rate=24000", realtime.MediaType);
        Assert.Equal("input.pcm", realtime.Name);
        Assert.True(realtime.Data.Length > 0);
        Assert.Equal(0, realtime.Data.Length % 2);
    }

    [Fact]
    public async Task ToRealtimeInputAudio_ConvertsMp3ToRealtimePcm()
    {
        var path = FindRepoFile(
            "test",
            "HPD-Agent.AudioCli",
            "freesound_community-how-are-you-doing-today-103598.mp3");
        var content = await AudioContent.FromFileAsync(path);

        var realtime = content.ToRealtimeInputAudio();

        Assert.Equal("audio/pcm;rate=24000", realtime.MediaType);
        Assert.Equal("freesound_community-how-are-you-doing-today-103598.pcm", realtime.Name);
        Assert.True(realtime.Data.Length > content.Data.Length);
    }

    [Fact]
    public void ToRealtimeInputAudio_RejectsUnsupportedEncodedAudio()
    {
        var content = AudioContent.Ogg(new byte[] { 1, 2, 3 });

        var error = Assert.Throws<NotSupportedException>(() => content.ToRealtimeInputAudio());

        Assert.Contains("supports input audio/mpeg", error.Message);
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

    private static byte[] CreatePcm16Wav(int sampleRate, int channelCount, short[] samples)
    {
        var dataLength = samples.Length * sizeof(short);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channelCount);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channelCount * sizeof(short));
        writer.Write((short)(channelCount * sizeof(short)));
        writer.Write((short)16);
        writer.Write("data".ToCharArray());
        writer.Write(dataLength);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        return stream.ToArray();
    }

    private static string FindRepoFile(params string[] relativeParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find repo file '{Path.Combine(relativeParts)}' from '{AppContext.BaseDirectory}'.");
    }
}
