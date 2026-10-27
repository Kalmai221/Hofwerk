using System.Security.Cryptography;
using Hofwerk.Installer.Engine;

namespace Hofwerk.Core.Tests;

public class PayloadPackTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "hofwerk-pack-" + Guid.NewGuid().ToString("N"));

    public PayloadPackTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Every_file_comes_back_identical_including_data_repeated_far_apart()
    {
        // A 6 MB block appears in two files: only a window bigger than the old 4 MB default can notice that.
        var random = new Random(42);
        byte[] block = new byte[6 * 1024 * 1024];
        random.NextBytes(block);

        string app = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(app, "sub"));
        File.WriteAllBytes(Path.Combine(app, "a.dll"), block);
        File.WriteAllBytes(Path.Combine(app, "b.dll"), block);
        File.WriteAllBytes(Path.Combine(app, "sub", "c.txt"), "hello hofwerk"u8.ToArray());
        File.WriteAllBytes(Path.Combine(app, "empty.bin"), []);
        File.WriteAllBytes(Path.Combine(app, "Z.EXE"), [0, 1, 2, 3, 255]);

        using var pack = new MemoryStream();
        PayloadPack.Write(PayloadPack.FromFolder(app), pack, System.IO.Compression.CompressionLevel.SmallestSize);

        // the repeat is found: the pack is not much bigger than one copy of the block
        Assert.True(pack.Length < block.Length * 1.2, $"pack is {pack.Length:N0} bytes");

        pack.Position = 0;
        var got = new Dictionary<string, byte[]>();
        PayloadPack.Read(pack, entries => Assert.Equal(5, entries.Count), (entry, content) =>
        {
            using var copy = new MemoryStream();
            content.CopyTo(copy);
            got[entry.Path] = copy.ToArray();
        });

        Assert.Equal(5, got.Count);
        foreach (var file in Directory.EnumerateFiles(app, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetRelativePath(app, file).Replace(Path.DirectorySeparatorChar, '/');
            Assert.True(SHA256.HashData(File.ReadAllBytes(file)).AsSpan().SequenceEqual(SHA256.HashData(got[name])), name);
        }
    }

    [Fact]
    public void Files_are_grouped_by_type()
    {
        string app = Path.Combine(_root, "app2");
        Directory.CreateDirectory(app);
        foreach (var name in new[] { "b.dll", "a.txt", "c.dll", "a.dll", "z.exe" }) File.WriteAllText(Path.Combine(app, name), name);

        Assert.Equal(["a.dll", "b.dll", "c.dll", "z.exe", "a.txt"], PayloadPack.FromFolder(app).Select(f => f.Path));
    }
}
