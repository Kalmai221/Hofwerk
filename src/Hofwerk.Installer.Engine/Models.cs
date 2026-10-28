using System.Reflection;

namespace Hofwerk.Installer.Engine;

public static class AppInfo
{
    public const string Name = "Hofwerk";
    public const string Publisher = "Hofwerk contributors";
    public const string Url = "https://kalmai221.github.io/Hofwerk/";   // the website, shown as the "about" link in Installed apps

    /// <summary>The version the installer was built for (from the release tag), without build metadata.</summary>
    public static string Version
    {
        get
        {
            string? v = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(v)) return "0.0.0";
            int plus = v.IndexOf('+');
            return plus < 0 ? v : v[..plus];
        }
    }
}

/// <summary>What the user picked on the options page.</summary>
/// <param name="Version">The version being installed when it is not the installer's own (an in-app update applies a newer app with the installed setup program).</param>
public sealed record InstallRequest(string Directory, bool DesktopShortcut, bool FileAssociation, string? OmsiFolder = null, string? Version = null, string? Language = null);

/// <summary>The stages an install goes through, in order. The online installer starts with Download; the offline one has it already.</summary>
public enum InstallStep { None, Download, Extract, Move, Cleanup, Close }

public sealed record InstallProgress(double Fraction, string Message, InstallStep Step = InstallStep.None);

/// <summary>An expected problem (bad folder, locked file) with a message that can be shown to the user as is.</summary>
public sealed class InstallerException(string message, Exception? inner = null) : Exception(message, inner);
