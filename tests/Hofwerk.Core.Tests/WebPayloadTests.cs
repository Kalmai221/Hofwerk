using System.Net;
using System.Security.Cryptography;
using Hofwerk.Installer.Engine;

namespace Hofwerk.Core.Tests;

public class WebPayloadTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "hofwerk-web-" + Guid.NewGuid().ToString("N"));

    public WebPayloadTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A small app folder packed the way the build does it. Returns the packed file.</summary>
    string MakePack(out string appFolder)
    {
        appFolder = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(appFolder, "sub"));
        File.WriteAllText(Path.Combine(appFolder, "Hofwerk.exe"), "pretend program");
        File.WriteAllBytes(Path.Combine(appFolder, "sub", "data.bin"), Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray());

        string pack = Path.Combine(_root, "app.hwpack");
        using (var file = File.Create(pack)) PayloadPack.Write(PayloadPack.FromFolder(appFolder), file);
        return pack;
    }

    static WebManifest ManifestFor(string packFile, string url) =>
        new(url, new FileInfo(packFile).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packFile))), "0.1.0");

    static string FileUrl(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    string MakeProgram(WebManifest? manifest)
    {
        string program = Path.Combine(_root, manifest is null ? "plain.exe" : "web.exe");
        string template = Path.Combine(_root, "template.exe");
        File.WriteAllBytes(template, Enumerable.Range(0, 3000).Select(i => (byte)(i * 7)).ToArray());
        if (manifest is null) File.Copy(template, program);
        else WebPayload.Attach(template, manifest, program);
        return program;
    }

    [Fact]
    public void A_manifest_attached_to_the_program_is_read_back_and_is_not_part_of_the_program()
    {
        var manifest = new WebManifest("https://github.com/x/y/releases/download/v1/Hofwerk-App-1-win-x64.hwpack", 1234, "ABCDEF", "1.0.0");
        string program = MakeProgram(manifest);

        var read = WebPayload.TryRead(program);

        Assert.Equal(manifest with { Sha256 = "abcdef" }, read);
        Assert.False(PayloadFile.HasPayload(program));
        Assert.Equal(3000, PayloadFile.ProgramLength(program));   // the uninstaller copy leaves the manifest out
        Assert.Null(WebPayload.TryRead(MakeProgram(null)));
    }

    [Fact]
    public async Task The_app_is_downloaded_checked_and_unpacks_identically()
    {
        string pack = MakePack(out string app);
        string program = MakeProgram(ManifestFor(pack, FileUrl(pack)));

        var progress = new List<double>();
        string leftover;
        await using (var payload = await WebPayload.OpenInstallPayloadAsync(program, new Progress<double>(progress.Add)))
        {
            Assert.NotNull(payload);
            var got = new Dictionary<string, byte[]>();
            PayloadPack.Read(payload, _ => { }, (entry, content) =>
            {
                using var copy = new MemoryStream();
                content.CopyTo(copy);
                got[entry.Path] = copy.ToArray();
            });
            Assert.Equal(2, got.Count);
            Assert.Equal(File.ReadAllBytes(Path.Combine(app, "sub", "data.bin")), got["sub/data.bin"]);
            Assert.Equal(File.ReadAllBytes(Path.Combine(app, "Hofwerk.exe")), got["Hofwerk.exe"]);
            leftover = ((FileStream)payload).Name;
        }

        Assert.False(File.Exists(leftover));    // the downloaded file is removed when the install is done
        await Task.Delay(50);
        Assert.Equal(1.0, progress.Max(), 3);
    }

    [Fact]
    public async Task A_download_that_does_not_match_is_refused_and_leaves_nothing_behind()
    {
        string pack = MakePack(out _);
        string wrongHash = Convert.ToHexString(SHA256.HashData([1, 2, 3]));
        var manifest = ManifestFor(pack, FileUrl(pack)) with { Sha256 = wrongHash };

        var ex = await Assert.ThrowsAsync<InstallerException>(() => WebPayload.DownloadAsync(manifest));
        Assert.Contains("did not match", ex.Message);

        var shortManifest = ManifestFor(pack, FileUrl(pack)) with { Size = new FileInfo(pack).Length + 10 };
        ex = await Assert.ThrowsAsync<InstallerException>(() => WebPayload.DownloadAsync(shortManifest));
        Assert.Contains("incomplete", ex.Message);

        var recent = DateTime.UtcNow.AddMinutes(-2);
        Assert.DoesNotContain(Directory.GetFiles(Path.GetTempPath(), "Hofwerk-setup-*.hwpack"), f => File.GetCreationTimeUtc(f) > recent);
    }

    [Theory]
    [InlineData("https://evil.example.com/Hofwerk-App.hwpack")]
    [InlineData("http://github.com/x/y/Hofwerk-App.hwpack")]
    [InlineData("ftp://github.com/x/y/Hofwerk-App.hwpack")]
    [InlineData("not a url")]
    public async Task Only_https_downloads_from_GitHub_are_used(string url)
    {
        var manifest = new WebManifest(url, 10, new string('0', 64), "1.0.0");
        await Assert.ThrowsAsync<InstallerException>(() => WebPayload.DownloadAsync(manifest));
    }

    sealed class Handler(byte[] body, bool offline = false) : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requested = request.RequestUri;
            if (offline) throw new HttpRequestException("no network");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    [Fact]
    public async Task Downloading_over_https_from_GitHub_works_and_being_offline_gives_a_friendly_error()
    {
        string pack = MakePack(out _);
        byte[] bytes = File.ReadAllBytes(pack);
        var manifest = ManifestFor(pack, "https://github.com/Kalmai221/Hofwerk/releases/download/v1/Hofwerk-App-1-win-x64.hwpack");

        var handler = new Handler(bytes);
        await using (var payload = await WebPayload.DownloadAsync(manifest, http: new HttpClient(handler)))
            Assert.Equal(bytes.Length, payload.Length);
        Assert.Equal(manifest.Url, handler.Requested!.ToString());

        var ex = await Assert.ThrowsAsync<InstallerException>(() => WebPayload.DownloadAsync(manifest, http: new HttpClient(new Handler([], offline: true))));
        Assert.Contains("internet connection", ex.Message);
    }
}
