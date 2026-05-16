using System.Text;
using Ude;

public partial class CodingHarness
{
    private const int BinarySniffBytes = 8192;

    private static async Task<byte[]> ReadByteSampleAsync(string fullPath)
    {
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BinarySniffBytes, useAsync: true);
        var buffer = new byte[Math.Min(BinarySniffBytes, Math.Max(0, (int)Math.Min(stream.Length, BinarySniffBytes)))];
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead)).ConfigureAwait(false);
            if (read == 0)
                break;
            totalRead += read;
        }

        if (totalRead == buffer.Length)
            return buffer;

        Array.Resize(ref buffer, totalRead);
        return buffer;
    }

    private static bool LooksBinary(byte[] sample, bool hasTextBom)
    {
        if (sample.Length == 0 || hasTextBom)
            return false;

        var controlCount = 0;
        foreach (var value in sample)
        {
            if (value == 0)
                return true;

            var printable = value is 0x09 or 0x0A or 0x0D ||
                            value is >= 0x20 and <= 0x7E ||
                            value >= 0x80;

            if (!printable)
                controlCount++;
        }

        return controlCount > sample.Length * 0.10;
    }

    private static Encoding? DetectBomEncoding(byte[] sample)
    {
        if (sample.Length >= 4)
        {
            if (sample[0] == 0xFF && sample[1] == 0xFE && sample[2] == 0x00 && sample[3] == 0x00)
                return new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true);
            if (sample[0] == 0x00 && sample[1] == 0x00 && sample[2] == 0xFE && sample[3] == 0xFF)
                return new UTF32Encoding(bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: true);
        }

        if (sample.Length >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

        if (sample.Length >= 2)
        {
            if (sample[0] == 0xFF && sample[1] == 0xFE)
                return new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true);
            if (sample[0] == 0xFE && sample[1] == 0xFF)
                return new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true);
        }

        return null;
    }

    private static Encoding DetectTextEncoding(string fullPath, byte[] sample, Encoding? bomEncoding)
    {
        if (bomEncoding != null)
            return bomEncoding;

        try
        {
            var detector = new CharsetDetector();
            using var sampleStream = new MemoryStream(sample, writable: false);
            detector.Feed(sampleStream);
            detector.DataEnd();

            if (!string.IsNullOrWhiteSpace(detector.Charset))
                return GetEncodingByCharset(detector.Charset);
        }
        catch
        {
            // Fall through to strict UTF-8.
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    }

    private static Encoding GetEncodingByCharset(string charset)
    {
        if (charset.Equals("iso-8859-1", StringComparison.OrdinalIgnoreCase) ||
            charset.Equals("latin1", StringComparison.OrdinalIgnoreCase) ||
            charset.Equals("windows-1252", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.Latin1;
        }

        return Encoding.GetEncoding(charset, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}
