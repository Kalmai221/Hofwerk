using Avalonia;

namespace Hofwerk.Installer;

sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Build step: Setup --web <packed app> <download url> <version> <output>  writes the online installer: this program
        // with only the download address, size and SHA-256 of the packed app attached.
        if (args.Length == 5 && args[0] == "--web")
        {
            using var packed = File.OpenRead(args[1]);
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packed));
            Engine.WebPayload.Attach(Environment.ProcessPath!, new Engine.WebManifest(args[2], new FileInfo(args[1]).Length, hash, args[3]), args[4]);
            return 0;
        }

        // Build step: Setup --attach <app folder> <output> [<packed app output>]  writes a copy of this program with the app
        // packed inside it, and optionally keeps the packed app as a file of its own (what the online installer downloads).
        if ((args.Length == 3 || args.Length == 4) && args[0] == "--attach")
        {
            string pack = Path.GetTempFileName();
            try
            {
                // HOFWERK_FAST_PACK=1 trades size for speed, for quick test builds. Releases use the smallest setting.
                var level = Environment.GetEnvironmentVariable("HOFWERK_FAST_PACK") == "1"
                    ? System.IO.Compression.CompressionLevel.Fastest
                    : System.IO.Compression.CompressionLevel.SmallestSize;
                using (var file = File.Create(pack))
                    Engine.PayloadPack.Write(Engine.PayloadPack.FromFolder(args[1]), file, level);
                Engine.PayloadFile.Attach(Environment.ProcessPath!, pack, args[2]);
                if (args.Length == 4) File.Copy(pack, args[3], overwrite: true);
            }
            finally { File.Delete(pack); }
            return 0;
        }

        // --silent runs without a window, for scripts and package managers.
        if (args.Contains("--silent", StringComparer.OrdinalIgnoreCase))
            return Silent.Run(args);

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        // The window is simple, so software drawing is plenty and the installer does not have to carry the ANGLE graphics layer (5 MB).
        .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
        .WithInterFont()
        .LogToTrace();
}
