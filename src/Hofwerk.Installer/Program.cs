using Avalonia;

namespace Hofwerk.Installer;

sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Build step: Setup --attach <app folder> <output>  writes a copy of this program with the app packed inside it.
        if (args.Length == 3 && args[0] == "--attach")
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
        .WithInterFont()
        .LogToTrace();
}
