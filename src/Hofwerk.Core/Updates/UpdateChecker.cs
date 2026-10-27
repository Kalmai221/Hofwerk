using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hofwerk.Core.Updates;

/// <summary>A newer release that can be installed.</summary>
public sealed record UpdateInfo(AppVersion Version, string Tag, bool IsPreRelease, string AssetName, string DownloadUrl, long Size, string PageUrl, string Notes);

/// <summary>The few fields of GitHub's releases API that are used. Generated at build time so the app can be trimmed.</summary>
internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = [];
}

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
}

/// <summary>Looks for a newer release on GitHub, downloads its installer and starts it.</summary>
public sealed class UpdateChecker
{
    public const string DefaultRepository = "Kalmai221/Hofwerk";

    readonly HttpClient _http;
    readonly string _repository;
    readonly string _runtimeId;

    /// <param name="runtimeId">"win-x64" or "linux-x64": picks which installer of a release belongs to this machine.</param>
    public UpdateChecker(HttpClient? http = null, string repository = DefaultRepository, string? runtimeId = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _repository = repository;
        _runtimeId = runtimeId ?? (OperatingSystem.IsWindows() ? "win-x64" : "linux-x64");
    }

    /// <summary>
    /// The newest release that is newer than <paramref name="current"/> and has an installer for this machine, or null.
    /// Pre-releases are only offered when asked for, or when the running version is itself a pre-release.
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(AppVersion current, bool includePreReleases, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_repository}/releases?per_page=30");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Hofwerk", current.Text));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return Pick(JsonSerializer.Deserialize(json, UpdateJson.Default.ListGitHubRelease) ?? [], current, includePreReleases);
    }

    internal UpdateInfo? Pick(List<GitHubRelease> releases, AppVersion current, bool includePreReleases)
    {
        includePreReleases |= current.IsPreRelease;
        UpdateInfo? best = null;

        foreach (var release in releases)
        {
            if (release.Draft) continue;
            var version = AppVersion.TryParse(release.TagName);
            if (version is null) continue;
            bool pre = release.Prerelease || version.IsPreRelease;
            if (pre && !includePreReleases) continue;
            if (!(version > current)) continue;
            if (best is not null && !(version > best.Version)) continue;

            var asset = release.Assets.FirstOrDefault(IsInstaller);
            if (asset is null || !IsTrustedUrl(asset.BrowserDownloadUrl)) continue;

            best = new UpdateInfo(version, release.TagName, pre, asset.Name, asset.BrowserDownloadUrl, asset.Size, release.HtmlUrl, release.Body ?? "");
        }
        return best;
    }

    // Hofwerk-Setup-<version>-win-x64.exe, or Hofwerk-Setup-<version>-linux-x64.tar.gz
    bool IsInstaller(GitHubAsset asset) =>
        asset.Name.StartsWith("Hofwerk-Setup-", StringComparison.OrdinalIgnoreCase)
        && asset.Name.Contains($"-{_runtimeId}", StringComparison.OrdinalIgnoreCase)
        && (asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || asset.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase));

    // Only files from GitHub itself are downloaded and run.
    internal static bool IsTrustedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host == "github.com" || uri.Host.EndsWith(".github.com", StringComparison.Ordinal)
            || uri.Host.EndsWith(".githubusercontent.com", StringComparison.Ordinal));

    /// <summary>Downloads the installer into <paramref name="folder"/> and returns the program to start.</summary>
    public async Task<string> DownloadAsync(UpdateInfo update, string folder, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!IsTrustedUrl(update.DownloadUrl)) throw new IOException("The update is not hosted on GitHub, so it was not downloaded.");
        Directory.CreateDirectory(folder);

        // The name comes from the release; only its file name part is used.
        string file = Path.Combine(folder, Path.GetFileName(update.AssetName));
        using (var response = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? update.Size;
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var target = File.Create(file);

            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
            if (update.Size > 0 && done != update.Size)
                throw new IOException($"The download is incomplete ({done:N0} of {update.Size:N0} bytes). Try again.");
        }

        return file.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? ExtractLinuxInstaller(file, folder) : file;
    }

    // The Linux release is a tarball so the executable bit survives the download; the installer inside is what gets run.
    static string ExtractLinuxInstaller(string archive, string folder)
    {
        string extracted = Path.Combine(folder, "extracted");
        if (Directory.Exists(extracted)) Directory.Delete(extracted, true);
        Directory.CreateDirectory(extracted);

        using (var gzip = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress))
            TarFile.ExtractToDirectory(gzip, extracted, overwriteFiles: true);

        string installer = Directory.EnumerateFiles(extracted, "Hofwerk-Setup-*", SearchOption.AllDirectories).FirstOrDefault()
                           ?? throw new IOException("The downloaded archive does not contain an installer.");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(installer, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return installer;
    }

    /// <summary>Starts the downloaded installer. It finds the existing install and updates it in place.</summary>
    public static void Launch(string installer) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installer) { UseShellExecute = true })?.Dispose();
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<GitHubRelease>))]
internal sealed partial class UpdateJson : JsonSerializerContext;
