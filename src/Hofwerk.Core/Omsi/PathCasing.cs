using System.Text;

namespace Hofwerk.Core.Omsi;

/// <summary>
/// Gives a path the capital letters its folders really have. Steam and the Windows registry store paths in lowercase
/// (<c>c:\program files (x86)\steam</c>), which works but looks wrong when it is shown to the user.
/// </summary>
public static class PathCasing
{
    /// <summary>
    /// The path with the drive letter in capitals and each folder written as it is on disk. Anything that cannot be looked up is left
    /// as it was written. On Linux, where letter case matters, a path is already exact and is returned unchanged.
    /// </summary>
    public static string Normalize(string path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path)) return path;

        try
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full) ?? "";
            if (root.Length == 0) return path;

            // "c:\" becomes "C:\"; a network share (\server\share\) keeps what it has.
            var result = new StringBuilder(root.StartsWith(@"\\", StringComparison.Ordinal) ? root : char.ToUpperInvariant(root[0]) + root[1..]);
            string current = root;

            foreach (string part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                string actual = part;
                try
                {
                    // Looking a name up in its folder returns the entry as it is spelled on disk.
                    var entry = new DirectoryInfo(current).EnumerateFileSystemInfos(part).FirstOrDefault();
                    if (entry is not null) actual = entry.Name;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }

                if (result[^1] != Path.DirectorySeparatorChar) result.Append(Path.DirectorySeparatorChar);
                result.Append(actual);
                current = Path.Combine(current, actual);
            }
            return result.ToString();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            return path;
        }
    }
}
