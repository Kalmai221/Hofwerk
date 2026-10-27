using System.Buffers;
using System.IO.Compression;
using System.Text;

namespace Hofwerk.Installer.Engine;

public sealed record PackEntry(string Path, long Length);

/// <summary>A file to put in a pack: its path inside the pack, its size, and a way to read it.</summary>
public sealed record PackSource(string Path, long Length, Func<Stream> Open);

/// <summary>
/// The app as one solid Brotli stream: a header listing every file, then all file contents back to back.
/// Compressing everything together beats zip (which compresses each file on its own) by a good margin for DLL-heavy apps.
/// Files are grouped by type so similar ones sit next to each other, and the compressor looks back 16 MB (the largest window
/// every Brotli decoder supports) so it can reuse data across the big DLLs. Unpacking needs nothing special.
/// </summary>
public static class PayloadPack
{
    static readonly byte[] Magic = "HWPACK1\n"u8.ToArray();
    const int MaxFiles = 1_000_000;
    const int MaxPathBytes = 4096;

    public static IReadOnlyList<PackSource> FromFolder(string folder)
    {
        string root = Path.GetFullPath(folder);
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(f => Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f, StringComparer.Ordinal)
            .Select(file => new PackSource(
                Path.GetRelativePath(root, file).Replace('\\', '/'),
                new FileInfo(file).Length,
                () => File.OpenRead(file)))
            .ToList();
    }

    public static void Write(IReadOnlyList<PackSource> files, Stream output, CompressionLevel level = CompressionLevel.SmallestSize)
    {
        using var brotli = new BrotliWindowStream(output, level == CompressionLevel.SmallestSize ? 11 : 1, level == CompressionLevel.SmallestSize ? 24 : 22);
        using (var header = new BinaryWriter(brotli, Encoding.UTF8, leaveOpen: true))
        {
            header.Write(Magic);
            header.Write(files.Count);
            foreach (var file in files)
            {
                byte[] path = Encoding.UTF8.GetBytes(file.Path);
                header.Write(path.Length);
                header.Write(path);
                header.Write(file.Length);
            }
        }

        foreach (var file in files)
        {
            using var content = file.Open();
            long copied = CopyExactly(content, brotli, file.Length);
            if (copied != file.Length) throw new IOException($"{file.Path} changed size while it was being packed.");
        }
    }

    /// <summary>
    /// Reads a pack from start to end. <paramref name="onHeader"/> gets the full file list first (for progress),
    /// then <paramref name="onFile"/> gets each file's content as a forward-only stream of exactly that file's length.
    /// </summary>
    public static void Read(Stream payload, Action<IReadOnlyList<PackEntry>> onHeader, Action<PackEntry, Stream> onFile)
    {
        using var brotli = new BrotliStream(payload, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new BinaryReader(brotli, Encoding.UTF8, leaveOpen: true);

        if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
            throw new InvalidDataException("The installer package is not in a format this installer understands.");

        int count = reader.ReadInt32();
        if (count < 0 || count > MaxFiles) throw new InvalidDataException("The installer package is damaged.");

        var entries = new List<PackEntry>(count);
        for (int i = 0; i < count; i++)
        {
            int pathLength = reader.ReadInt32();
            if (pathLength <= 0 || pathLength > MaxPathBytes) throw new InvalidDataException("The installer package is damaged.");
            string path = Encoding.UTF8.GetString(reader.ReadBytes(pathLength));
            long length = reader.ReadInt64();
            if (length < 0) throw new InvalidDataException("The installer package is damaged.");
            entries.Add(new PackEntry(path, length));
        }

        onHeader(entries);

        foreach (var entry in entries)
        {
            using var content = new BoundedStream(brotli, entry.Length);
            onFile(entry, content);
            content.Drain(); // whatever the callback did not read still has to be skipped to reach the next file
        }
    }

    /// <summary>Writes Brotli with a chosen quality and window size (<see cref="BrotliStream"/> always uses a 4 MB window).</summary>
    public sealed class BrotliWindowStream(Stream output, int quality, int window) : Stream
    {
        BrotliEncoder _encoder = new(quality, window);
        readonly byte[] _buffer = new byte[64 * 1024];
        bool _finished;

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> data)
        {
            while (data.Length > 0)
            {
                var status = _encoder.Compress(data, _buffer, out int consumed, out int written, isFinalBlock: false);
                if (status == OperationStatus.InvalidData) throw new IOException("Compressing the app failed.");
                if (written > 0) output.Write(_buffer, 0, written);
                data = data[consumed..];
            }
        }

        void Finish()
        {
            if (_finished) return;
            _finished = true;
            OperationStatus status;
            do
            {
                status = _encoder.Compress([], _buffer, out _, out int written, isFinalBlock: true);
                if (status == OperationStatus.InvalidData) throw new IOException("Compressing the app failed.");
                if (written > 0) output.Write(_buffer, 0, written);
            } while (status != OperationStatus.Done);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Finish();
                _encoder.Dispose();
            }
            base.Dispose(disposing);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    static long CopyExactly(Stream from, Stream to, long length)
    {
        var buffer = new byte[256 * 1024];
        long total = 0;
        int read;
        while (total < length && (read = from.Read(buffer, 0, (int)Math.Min(buffer.Length, length - total))) > 0)
        {
            to.Write(buffer, 0, read);
            total += read;
        }
        return total;
    }

    /// <summary>The next <c>length</c> bytes of another stream, as a stream of their own.</summary>
    sealed class BoundedStream : Stream
    {
        readonly Stream _inner;
        readonly long _length;
        long _left;

        public BoundedStream(Stream inner, long length)
        {
            _inner = inner;
            _length = length;
            _left = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _length - _left; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_left <= 0) return 0;
            int read = _inner.Read(buffer, offset, (int)Math.Min(count, _left));
            if (read == 0) throw new EndOfStreamException("The installer package ended early.");
            _left -= read;
            return read;
        }

        public void Drain()
        {
            var buffer = new byte[64 * 1024];
            while (_left > 0)
            {
                int read = _inner.Read(buffer, 0, (int)Math.Min(buffer.Length, _left));
                if (read == 0) throw new EndOfStreamException("The installer package ended early.");
                _left -= read;
            }
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
