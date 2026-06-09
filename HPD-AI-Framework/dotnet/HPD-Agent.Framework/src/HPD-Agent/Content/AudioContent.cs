// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using NLayer;

namespace HPD.Agent;

/// <summary>
/// Represents audio content for speech-to-text, realtime audio, and finite input-media runtimes.
/// Extends DataContent with audio-specific conveniences.
/// </summary>
/// <remarks>
/// <para>
/// AudioContent can flow through HPD Audio runtime integration as finite input media.
/// New audio runtime integrations preserve audio identity before transcription. Legacy
/// transcription middleware may still choose to transcribe audio for compatibility.
/// </para>
/// <para>
/// <b>Supported Formats:</b> MP3, WAV, OGG, FLAC, WebM, M4A
/// </para>
/// <para>
/// <b>Middleware Behavior:</b>
/// <list type="bullet">
/// <item>ContentUploadMiddleware: Uploads to IContentStore with kind=upload metadata</item>
/// <item>Audio runtime integration: Detects input audio before upload and preserves input-content identity</item>
/// </list>
/// </para>
/// </remarks>
public class AudioContent : DataContent
{
    public const string PcmMediaType = "audio/pcm";
    public const string PcmuMediaType = "audio/pcmu";
    public const string PcmaMediaType = "audio/pcma";
    public const int DefaultRealtimeInputSampleRate = 24000;
    public const int DefaultRealtimeInputChannelCount = 1;

    /// <summary>
    /// Creates audio content from bytes.
    /// </summary>
    /// <param name="data">Audio bytes.</param>
    /// <param name="mediaType">MIME type.</param>
    public AudioContent(ReadOnlyMemory<byte> data, string mediaType)
        : base(data, RequireMediaType(mediaType))
    {
    }

    /// <summary>
    /// Creates audio content from a data URI.
    /// </summary>
    /// <param name="uri">Data URI containing audio data.</param>
    [JsonConstructor]
    public AudioContent(string uri)
        : base(uri)
    {
        if (!HasTopLevelMediaType("audio"))
            throw new ArgumentException("Data URI must contain audio content.", nameof(uri));
    }

    /// <summary>
    /// Creates audio content from a file path.
    /// </summary>
    /// <param name="filePath">Path to audio file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>AudioContent with file data.</returns>
    public static async Task<AudioContent> FromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var mediaType = GetMediaTypeFromExtension(filePath);
        return new AudioContent(bytes, mediaType) { Name = Path.GetFileName(filePath) };
    }

    /// <summary>
    /// Creates audio content for WAV format.
    /// </summary>
    /// <param name="data">Audio bytes in WAV format.</param>
    /// <returns>AudioContent with WAV MIME type.</returns>
    public static AudioContent Wav(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.AudioWav);

    /// <summary>
    /// Creates audio content for MP3 format.
    /// </summary>
    /// <param name="data">Audio bytes in MP3 format.</param>
    /// <returns>AudioContent with MP3 MIME type.</returns>
    public static AudioContent Mp3(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.AudioMpeg);

    /// <summary>
    /// Creates audio content for OGG format.
    /// </summary>
    /// <param name="data">Audio bytes in OGG format.</param>
    /// <returns>AudioContent with OGG MIME type.</returns>
    public static AudioContent Ogg(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.AudioOgg);

    /// <summary>
    /// Creates audio content for FLAC format.
    /// </summary>
    /// <param name="data">Audio bytes in FLAC format.</param>
    /// <returns>AudioContent with FLAC MIME type.</returns>
    public static AudioContent Flac(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.AudioFlac);

    /// <summary>
    /// Creates audio content for WebM format.
    /// </summary>
    /// <param name="data">Audio bytes in WebM format.</param>
    /// <returns>AudioContent with WebM MIME type.</returns>
    public static AudioContent WebM(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.AudioWebM);

    /// <summary>
    /// Creates audio content for M4A format.
    /// </summary>
    /// <param name="data">Audio bytes in M4A format.</param>
    /// <returns>AudioContent with M4A MIME type.</returns>
    public static AudioContent M4a(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.AudioMp4);

    /// <summary>
    /// Creates raw PCM audio content.
    /// </summary>
    /// <param name="data">PCM bytes.</param>
    /// <param name="sampleRate">PCM sample rate.</param>
    /// <returns>AudioContent with PCM media type and sample-rate parameter.</returns>
    public static AudioContent Pcm(ReadOnlyMemory<byte> data, int sampleRate = DefaultRealtimeInputSampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return new AudioContent(data, $"{PcmMediaType};rate={sampleRate}");
    }

    /// <summary>
    /// Wraps generic audio data as <see cref="AudioContent"/> while preserving bytes and metadata.
    /// </summary>
    /// <param name="content">Data content with an audio media type.</param>
    /// <returns>AudioContent with the same data, media type, and name.</returns>
    public static AudioContent FromDataContent(DataContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!IsAudioMediaType(content.MediaType))
        {
            throw new ArgumentException("DataContent must contain audio content.", nameof(content));
        }

        return new AudioContent(content.Data, content.MediaType)
        {
            Name = content.Name
        };
    }

    /// <summary>
    /// Converts this audio into the finite input-audio format expected by native realtime transports.
    /// </summary>
    /// <param name="sampleRate">Target PCM sample rate when decoding encoded audio.</param>
    /// <returns>A realtime-compatible audio copy.</returns>
    public AudioContent ToRealtimeInputAudio(int sampleRate = DefaultRealtimeInputSampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        if (IsRealtimeInputCompatibleMediaType(MediaType))
        {
            return new AudioContent(Data, MediaType)
            {
                Name = Name
            };
        }

        var sourceMediaType = GetBaseMediaType(MediaType);
        var pcm = sourceMediaType switch
        {
            MimeTypeRegistry.AudioMpeg or "audio/mp3" => DecodeMp3(Data),
            MimeTypeRegistry.AudioWav or "audio/x-wav" => DecodeWav(Data),
            _ => throw UnsupportedRealtimeInput(MediaType)
        };

        var prepared = ConvertToPcm16(pcm, sampleRate, DefaultRealtimeInputChannelCount);
        return new AudioContent(prepared, $"{PcmMediaType};rate={sampleRate}")
        {
            Name = ReplaceExtension(Name, ".pcm")
        };
    }

    public static bool IsAudioMediaType(string? mediaType)
        => !string.IsNullOrWhiteSpace(mediaType) &&
            mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);

    public static string? GetBaseMediaType(string? mediaType)
        => string.IsNullOrWhiteSpace(mediaType)
            ? null
            : mediaType.Split(';', 2)[0].Trim().ToLowerInvariant();

    public static bool IsRealtimeInputCompatibleMediaType(string? mediaType)
        => GetRealtimeInputAudioFormatMediaType(mediaType) is not null;

    public static string? GetRealtimeInputAudioFormatMediaType(string? mediaType)
        => GetBaseMediaType(mediaType) switch
        {
            PcmMediaType => PcmMediaType,
            PcmuMediaType => PcmuMediaType,
            PcmaMediaType => PcmaMediaType,
            _ => null
        };

    public static int? GetSampleRate(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return null;
        }

        var parts = mediaType.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts[1..])
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();
            if ((string.Equals(key, "rate", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "sample-rate", StringComparison.OrdinalIgnoreCase)) &&
                int.TryParse(value, out var parsed) &&
                parsed > 0)
            {
                return parsed;
            }
        }

        return null;
    }

    private static string GetMediaTypeFromExtension(string filePath)
    {
        return MimeTypeRegistry.GetMimeTypeFromPath(filePath)
            ?? throw new NotSupportedException($"Audio file extension '{Path.GetExtension(filePath)}' is not registered.");
    }

    private static string RequireMediaType(string mediaType)
        => string.IsNullOrWhiteSpace(mediaType)
            ? throw new ArgumentException("Audio media type is required.", nameof(mediaType))
            : mediaType;

    private static DecodedPcm DecodeMp3(ReadOnlyMemory<byte> data)
    {
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        using var file = new MpegFile(stream);
        var samples = new List<float>();
        var buffer = new float[Math.Max(file.SampleRate * Math.Max(file.Channels, 1), 4096)];

        while (true)
        {
            var read = file.ReadSamples(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }

            for (var i = 0; i < read; i++)
            {
                samples.Add(buffer[i]);
            }
        }

        return new DecodedPcm(samples.ToArray(), file.SampleRate, file.Channels);
    }

    private static DecodedPcm DecodeWav(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < 44 ||
            !AsciiEquals(span[0..4], "RIFF") ||
            !AsciiEquals(span[8..12], "WAVE"))
        {
            throw UnsupportedRealtimeInput(MimeTypeRegistry.AudioWav);
        }

        ushort? formatTag = null;
        ushort? channels = null;
        int? sampleRate = null;
        ushort? bitsPerSample = null;
        ReadOnlySpan<byte> pcmData = default;

        var offset = 12;
        while (offset + 8 <= span.Length)
        {
            var chunkId = span.Slice(offset, 4);
            var chunkSize = (int)ReadUInt32LittleEndian(span.Slice(offset + 4, 4));
            offset += 8;
            if (chunkSize < 0 || offset + chunkSize > span.Length)
            {
                break;
            }

            var chunk = span.Slice(offset, chunkSize);
            if (AsciiEquals(chunkId, "fmt ") && chunk.Length >= 16)
            {
                formatTag = ReadUInt16LittleEndian(chunk[0..2]);
                channels = ReadUInt16LittleEndian(chunk[2..4]);
                sampleRate = (int)ReadUInt32LittleEndian(chunk[4..8]);
                bitsPerSample = ReadUInt16LittleEndian(chunk[14..16]);
            }
            else if (AsciiEquals(chunkId, "data"))
            {
                pcmData = chunk;
            }

            offset += chunkSize + (chunkSize % 2);
        }

        if (formatTag != 1 ||
            channels is null or 0 ||
            sampleRate is null or <= 0 ||
            bitsPerSample != 16 ||
            pcmData.IsEmpty)
        {
            throw UnsupportedRealtimeInput(MimeTypeRegistry.AudioWav);
        }

        var sampleCount = pcmData.Length / sizeof(short);
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            samples[i] = ReadInt16LittleEndian(pcmData.Slice(i * sizeof(short), sizeof(short))) / 32768f;
        }

        return new DecodedPcm(samples, sampleRate.Value, channels.Value);
    }

    private static byte[] ConvertToPcm16(DecodedPcm source, int targetSampleRate, int targetChannelCount)
    {
        if (source.Samples.Length == 0)
        {
            return [];
        }

        var sourceFrameCount = source.Samples.Length / source.ChannelCount;
        if (sourceFrameCount <= 0)
        {
            return [];
        }

        var targetFrameCount = Math.Max(1, (int)Math.Round(sourceFrameCount * (double)targetSampleRate / source.SampleRate));
        var output = new byte[targetFrameCount * sizeof(short) * targetChannelCount];

        for (var targetFrame = 0; targetFrame < targetFrameCount; targetFrame++)
        {
            var sourcePosition = targetFrame * (double)source.SampleRate / targetSampleRate;
            var sourceFrame = Math.Min((int)Math.Floor(sourcePosition), sourceFrameCount - 1);
            var nextFrame = Math.Min(sourceFrame + 1, sourceFrameCount - 1);
            var fraction = sourcePosition - sourceFrame;
            var mixed = Lerp(MixFrame(source, sourceFrame), MixFrame(source, nextFrame), fraction);
            var pcm = FloatToPcm16(mixed);
            output[targetFrame * 2] = (byte)(pcm & 0xFF);
            output[targetFrame * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
        }

        return output;
    }

    private static float MixFrame(DecodedPcm source, int frame)
    {
        var start = frame * source.ChannelCount;
        var sum = 0f;
        for (var channel = 0; channel < source.ChannelCount; channel++)
        {
            sum += source.Samples[start + channel];
        }

        return sum / source.ChannelCount;
    }

    private static float Lerp(float left, float right, double fraction)
        => left + (right - left) * (float)fraction;

    private static short FloatToPcm16(float sample)
    {
        var clamped = Math.Clamp(sample, -1f, 1f);
        return (short)Math.Round(clamped * (clamped < 0 ? 32768f : 32767f));
    }

    private static NotSupportedException UnsupportedRealtimeInput(string? mediaType)
        => new(
            $"Native realtime input currently supports input audio/mpeg, audio/wav, audio/pcm, audio/pcmu, and audio/pcma. " +
            $"Received '{mediaType ?? "<unknown>"}'. Add a decoder/transcoder before using this format with realtime transport.");

    private static string? ReplaceExtension(string? name, string extension)
        => string.IsNullOrWhiteSpace(name)
            ? name
            : Path.ChangeExtension(name, extension);

    private static bool AsciiEquals(ReadOnlySpan<byte> bytes, string value)
    {
        if (bytes.Length != value.Length)
        {
            return false;
        }

        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != value[i])
            {
                return false;
            }
        }

        return true;
    }

    private static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> value)
        => (ushort)(value[0] | (value[1] << 8));

    private static short ReadInt16LittleEndian(ReadOnlySpan<byte> value)
        => (short)ReadUInt16LittleEndian(value);

    private static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> value)
        => (uint)(value[0] | (value[1] << 8) | (value[2] << 16) | (value[3] << 24));

    private sealed record DecodedPcm(float[] Samples, int SampleRate, int ChannelCount);
}
