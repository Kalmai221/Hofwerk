using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Hofwerk.Installer.Engine;

/// <summary>Where the app can be downloaded from, and how to tell it arrived intact.</summary>
public sealed record WebManifest(string Url, long Size, string Sha256, string Version);

/// <summary>
/// The online installer is the setup program with no app inside it, only a small manifest at its end:
/// <c>[program][manifest text][manifest length: int64][8-byte magic]</c>. It downloads the app (the same packed
/// stream the offline installer carries inside itself), checks its size and SHA-256 against the manifest, and installs it.
/// The text is plain lines so the trimmed installer needs no serializer.
/// </summary>
public static class WebPayload
{
    static readonly byte[] Magic = "HWWEBMF1"u8.ToArray();
    const int TrailerSize = 8 + 8;
    const int MaxManifestBytes = 8192;

    public static void Attach(string programPath, WebManifest manifest, string outputPath)
    {
        if (string.Equals(Path.GetFullPath(programPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The output must be a different file from the program.");

        byte[] text = Encoding.UTF8.GetBytes(string.Join('\n', manifest.Url, manifest.Size, manifest.Sha256.ToLowerInvariant(), manifest.Version));
        long programLength = PayloadFile.ProgramLength(programPath);

        using var output = File.Create(outputPath);
        PayloadFile.CopyPrefix(programPath, output, programLength);
        output.Write(text);

        Span<byte> trailer = stackalloc byte[TrailerSize];
        BinaryPrimitives.WriteInt64LittleEndian(trailer, text.Length);
        Magic.CopyTo(trailer[8..]);
        output.Write(trailer);
    }

    /// <summary>The manifest attached to this program, or null when it has none (offline installer, uninstaller).</summary>
    public static WebManifest? TryRead(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (ManifestLength(fs) is not { } length) return null;

            fs.Seek(-(TrailerSize + length), SeekOrigin.End);
            byte[] text = new byte[length];
            fs.ReadExactly(text);

            string[] lines = Encoding.UTF8.GetString(text).Split('\n');
            if (lines.Length != 4 || !long.TryParse(lines[1], out long size) || size <= 0) return null;
            return new WebManifest(lines[0], size, lines[2].ToLowerInvariant(), lines[3]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Length of the manifest at the end of <paramref name="fs"/>, or null if there is none.</summary>
    internal static long? ManifestLength(FileStream fs)
    {
        if (fs.Length < TrailerSize) return null;

        Span<byte> trailer = stackalloc byte[TrailerSize];
        fs.Seek(-TrailerSize, SeekOrigin.End);
        fs.ReadExactly(trailer);
        fs.Seek(0, SeekOrigin.Begin);

        if (!trailer[8..].SequenceEqual(Magic)) return null;
        long length = BinaryPrimitives.ReadInt64LittleEndian(trailer);
        return length is > 0 and <= MaxManifestBytes && length <= fs.Length - TrailerSize ? length : null;
    }

    internal static long TrailerBytes(long manifestLength) => manifestLength + TrailerSize;

    // Only GitHub is trusted as a source (file: is allowed so the installer can be tested without a release).
    static bool IsAllowed(Uri uri) =>
        uri.IsFile
        || (uri.Scheme == Uri.UriSchemeHttps
            && (uri.Host == "github.com" || uri.Host.EndsWith(".github.com", StringComparison.Ordinal)
                || uri.Host.EndsWith(".githubusercontent.com", StringComparison.Ordinal)));

    /// <summary>
    /// Downloads the app into a temporary file, checks size and SHA-256, and returns a stream over it that deletes the file
    /// when it is closed. <paramref name="progress"/> gets 0..1.
    /// </summary>
    public static async Task<Stream> DownloadAsync(WebManifest manifest, IProgress<double>? progress = null, HttpClient? http = null, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out var uri) || !IsAllowed(uri))
            throw new InstallerException("This installer points to a download that is not on GitHub, so it was not used.");

        string temp = Path.Combine(Path.GetTempPath(), "Hofwerk-setup-" + Guid.NewGuid().ToString("N") + ".hwpack");
        bool keep = false;
        try
        {
            using var sha = SHA256.Create();
            await using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await using Stream source = uri.IsFile
                    ? File.OpenRead(uri.LocalPath)
                    : await OpenHttpAsync(uri, http, ct).ConfigureAwait(false);

                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    done += read;
                    progress?.Report(Math.Min(1.0, (double)done / manifest.Size));
                }
                sha.TransformFinalBlock([], 0, 0);

                if (done != manifest.Size)
                    throw new InstallerException($"The download is incomplete ({done:N0} of {manifest.Size:N0} bytes). Check your connection and try again.");
            }

            if (!string.Equals(Convert.ToHexString(sha.Hash!), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InstallerException("The downloaded file did not match what was expected, so it was not installed. Try again, or use the offline installer.");

            keep = true;
            return new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.DeleteOnClose);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InstallerException("Could not download Hofwerk. Check your internet connection and try again, or use the offline installer.", ex);
        }
        finally
        {
            if (!keep) try { File.Delete(temp); } catch (IOException) { }
        }
    }

    static async Task<Stream> OpenHttpAsync(Uri uri, HttpClient? http, CancellationToken ct)
    {
        http ??= new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The app to install: the one attached to this program, or the one downloaded as the manifest says. Null if neither.</summary>
    public static async Task<Stream?> OpenInstallPayloadAsync(string program, IProgress<double>? downloadProgress = null, HttpClient? http = null, CancellationToken ct = default)
    {
        if (PayloadFile.OpenPayload(program) is { } attached) return attached;
        return TryRead(program) is { } manifest ? await DownloadAsync(manifest, downloadProgress, http, ct).ConfigureAwait(false) : null;
    }
}
