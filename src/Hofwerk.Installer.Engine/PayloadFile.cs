using System.Buffers.Binary;

namespace Hofwerk.Installer.Engine;

/// <summary>
/// The installer is one executable with the app's zip attached to its end:
/// <c>[program][payload.zip][payload length: int64][8-byte magic]</c>.
/// The uninstaller is the same program without the payload.
/// </summary>
public static class PayloadFile
{
    static readonly byte[] Magic = "HWPAYLD1"u8.ToArray();
    const int TrailerSize = 8 + 8;

    public static bool HasPayload(string path)
    {
        using var fs = File.OpenRead(path);
        return PayloadLength(fs) is not null;
    }

    /// <summary>Size of the program itself, i.e. the file without the attached payload.</summary>
    public static long ProgramLength(string path)
    {
        using var fs = File.OpenRead(path);
        return fs.Length - (PayloadLength(fs) is { } len ? len + TrailerSize : 0);
    }

    /// <summary>Opens the attached zip, or returns null when the file has none. Dispose the stream when done.</summary>
    public static Stream? OpenPayload(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (PayloadLength(fs) is not { } len)
        {
            fs.Dispose();
            return null;
        }
        return new WindowStream(fs, fs.Length - TrailerSize - len, len);
    }

    /// <summary>Writes <paramref name="programPath"/> followed by <paramref name="zipPath"/> to <paramref name="outputPath"/>.</summary>
    public static void Attach(string programPath, string zipPath, string outputPath)
    {
        if (string.Equals(Path.GetFullPath(programPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The output must be a different file from the program.");

        long programLength = ProgramLength(programPath);
        using var output = File.Create(outputPath);
        CopyPrefix(programPath, output, programLength);

        long zipLength;
        using (var zip = File.OpenRead(zipPath))
        {
            zipLength = zip.Length;
            zip.CopyTo(output);
        }

        Span<byte> trailer = stackalloc byte[TrailerSize];
        BinaryPrimitives.WriteInt64LittleEndian(trailer, zipLength);
        Magic.CopyTo(trailer[8..]);
        output.Write(trailer);
    }

    /// <summary>Copies the program without its payload (used to make the uninstaller).</summary>
    public static void CopyProgramOnly(string programPath, string destination)
    {
        long length = ProgramLength(programPath);
        using var output = File.Create(destination);
        CopyPrefix(programPath, output, length);
    }

    static long? PayloadLength(FileStream fs)
    {
        if (fs.Length < TrailerSize) return null;

        Span<byte> trailer = stackalloc byte[TrailerSize];
        fs.Seek(-TrailerSize, SeekOrigin.End);
        fs.ReadExactly(trailer);
        fs.Seek(0, SeekOrigin.Begin);

        if (!trailer[8..].SequenceEqual(Magic)) return null;
        long length = BinaryPrimitives.ReadInt64LittleEndian(trailer);
        return length >= 0 && length <= fs.Length - TrailerSize ? length : null;
    }

    static void CopyPrefix(string path, Stream destination, long length)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[81920];
        long left = length;
        while (left > 0)
        {
            int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (read == 0) throw new EndOfStreamException();
            destination.Write(buffer, 0, read);
            left -= read;
        }
    }

    /// <summary>A read-only, seekable view of part of another stream, so ZipArchive can read the attached zip.</summary>
    sealed class WindowStream(Stream inner, long start, long length) : Stream
    {
        long _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => _position = value; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long left = length - _position;
            if (left <= 0) return 0;
            inner.Seek(start + _position, SeekOrigin.Begin);
            int read = inner.Read(buffer, offset, (int)Math.Min(count, left));
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => length + offset,
            };
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
