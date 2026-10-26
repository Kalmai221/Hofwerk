using System.IO.Compression;
using System.Text;

namespace Hofwerk.Installer.Engine;

public sealed record PackEntry(string Path, long Length);

/// <summary>A file to put in a pack: its path inside the pack, its size, and a way to read it.</summary>
public sealed record PackSource(string Path, long Length, Func<Stream> Open);

/// <summary>
/// The app as one solid Brotli stream: a header listing every file, then all file contents back to back.
/// Compressing everything together beats zip (which compresses each file on its own) by a good margin for DLL-heavy apps.
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
            .Order(StringComparer.Ordinal)
            .Select(file => new PackSource(
                Path.GetRelativePath(root, file).Replace('\\', '/'),
                new FileInfo(file).Length,
                () => File.OpenRead(file)))
            .ToList();
    }

    public static void Write(IReadOnlyList<PackSource> files, Stream output, CompressionLevel level = CompressionLevel.SmallestSize)
    {
        using var brotli = new BrotliStream(output, level, leaveOpen: true);
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
    sealed class BoundedStream(Stream inner, long length) : Stream
    {
        long _left = length;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => length - _left; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_left <= 0) return 0;
            int read = inner.Read(buffer, offset, (int)Math.Min(count, _left));
            if (read == 0) throw new EndOfStreamException("The installer package ended early.");
            _left -= read;
            return read;
        }

        public void Drain()
        {
            var buffer = new byte[64 * 1024];
            while (_left > 0)
            {
                int read = inner.Read(buffer, 0, (int)Math.Min(buffer.Length, _left));
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
