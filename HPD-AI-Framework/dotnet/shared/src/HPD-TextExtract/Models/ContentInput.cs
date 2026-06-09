using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HPD.TextExtract.Models
{
    public enum ContentInputKind
    {
        Path,
        Stream,
        Bytes,
        Url
    }

    public sealed class ContentInput
    {
        private readonly Stream? _stream;
        private readonly ReadOnlyMemory<byte> _bytes;

        public ContentInputKind Kind { get; }
        public string? Path { get; }
        public Uri? Url { get; }
        public string? FileName { get; }
        public string? MimeType { get; }
        public bool LeaveOpen { get; }

        private ContentInput(
            ContentInputKind kind,
            string? path = null,
            Stream? stream = null,
            ReadOnlyMemory<byte> bytes = default,
            Uri? url = null,
            string? fileName = null,
            string? mimeType = null,
            bool leaveOpen = false)
        {
            Kind = kind;
            Path = path;
            _stream = stream;
            _bytes = bytes;
            Url = url;
            FileName = fileName;
            MimeType = mimeType;
            LeaveOpen = leaveOpen;
        }

        public static ContentInput FromPath(string path, string? mimeType = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            return new ContentInput(
                ContentInputKind.Path,
                path: path,
                fileName: System.IO.Path.GetFileName(path),
                mimeType: mimeType);
        }

        public static ContentInput FromStream(
            Stream stream,
            string? fileName = null,
            string? mimeType = null,
            bool leaveOpen = false)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return new ContentInput(
                ContentInputKind.Stream,
                stream: stream,
                fileName: fileName,
                mimeType: mimeType,
                leaveOpen: leaveOpen);
        }

        public static ContentInput FromBytes(
            ReadOnlyMemory<byte> bytes,
            string? fileName = null,
            string? mimeType = null)
        {
            return new ContentInput(
                ContentInputKind.Bytes,
                bytes: bytes,
                fileName: fileName,
                mimeType: mimeType);
        }

        public static ContentInput FromUrl(Uri url, string? mimeType = null)
        {
            ArgumentNullException.ThrowIfNull(url);
            return new ContentInput(
                ContentInputKind.Url,
                url: url,
                fileName: System.IO.Path.GetFileName(url.LocalPath),
                mimeType: mimeType);
        }

        public async ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Kind switch
            {
                ContentInputKind.Path => new FileStream(
                    Path!,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true),
                ContentInputKind.Stream => LeaveOpen
                    ? new NonClosingStream(_stream!)
                    : _stream!,
                ContentInputKind.Bytes => new MemoryStream(_bytes.ToArray(), writable: false),
                ContentInputKind.Url => throw new InvalidOperationException("URL inputs must be fetched by a web decoder."),
                _ => throw new InvalidOperationException($"Unsupported input kind: {Kind}")
            };
        }

        private sealed class NonClosingStream : Stream
        {
            private readonly Stream _inner;

            public NonClosingStream(Stream inner) => _inner = inner;

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }
            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                _inner.ReadAsync(buffer, cancellationToken);
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
                _inner.WriteAsync(buffer, cancellationToken);
            protected override void Dispose(bool disposing) { }
            public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
