using System.Security.Cryptography;
using PermaDel.Core.Metadata;

namespace PermaDel.Core.Tests.Metadata;

public sealed class MetadataRemoverTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("PermaDelMetadata-");

    public void Dispose()
    {
        foreach (var file in _root.EnumerateFiles("*", new EnumerationOptions { AttributesToSkip = 0 }))
            file.Attributes = FileAttributes.Normal;
        _root.Delete(recursive: true);
    }

    [Fact]
    public void Remove_SavesCopiesWithIncreasingNames()
    {
        var path = Write("photo.jpg", MetadataFixtures.Jpeg().Original);
        var remover = new MetadataRemover();

        var first = remover.Remove([path], replaceOriginals: false);
        var second = remover.Remove([path], replaceOriginals: false);
        var third = remover.Remove([path], replaceOriginals: false);

        Assert.Equal(1, first.FilesRemoved);
        Assert.Equal(Path.Combine(_root.FullName, "photo (clean).jpg"), Assert.Single(first.Copies));
        Assert.Equal(Path.Combine(_root.FullName, "photo (clean 2).jpg"), Assert.Single(second.Copies));
        Assert.Equal(Path.Combine(_root.FullName, "photo (clean 3).jpg"), Assert.Single(third.Copies));
        Assert.Equal(MetadataFixtures.Jpeg().Cleaned, File.ReadAllBytes(first.Copies[0]));
    }

    [Fact]
    public void Remove_LeavesTheOriginalUntouchedWhenSavingACopy()
    {
        var path = Write("photo.jpg", MetadataFixtures.Jpeg().Original);
        var before = Hash(path);

        var result = new MetadataRemover().Remove([path], replaceOriginals: false);

        Assert.True(result.Succeeded, Describe(result));
        Assert.Equal(before, Hash(path));
        Assert.True(File.Exists(result.Copies[0]));
    }

    [Fact]
    public void Remove_ReplacesTheOriginalWhenAsked()
    {
        var path = Write("photo.jpg", MetadataFixtures.Jpeg().Original);
        var created = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetCreationTimeUtc(path, created);

        var result = new MetadataRemover().Remove([path], replaceOriginals: true);

        Assert.True(result.Succeeded, Describe(result));
        Assert.Equal(1, result.FilesRemoved);
        Assert.Empty(result.Copies);
        Assert.Equal(MetadataFixtures.Jpeg().Cleaned, File.ReadAllBytes(path));
        Assert.Equal(created, File.GetCreationTimeUtc(path));
        Assert.Empty(_root.EnumerateFiles("*(clean)*"));
    }

    [Fact]
    public void Remove_ReportsReadOnlyFiles()
    {
        var path = Write("photo.jpg", MetadataFixtures.Jpeg().Original);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var result = new MetadataRemover().Remove([path], replaceOriginals: false);

            Assert.Equal(0, result.FilesRemoved);
            Assert.Equal("This file is read-only.", Assert.Single(result.Failures).Reason);
            Assert.Single(_root.EnumerateFiles());
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Remove_WritesNoCopyWhenTheFileCannotBeCleanedSafely()
    {
        var truncated = MetadataFixtures.Jpeg().Original[..^5];
        var path = Write("photo.jpg", truncated);

        var result = new MetadataRemover().Remove([path], replaceOriginals: false);

        Assert.Equal(0, result.FilesRemoved);
        Assert.Equal(path, Assert.Single(result.Failures).Path);
        Assert.Single(_root.EnumerateFiles());
        Assert.Equal(truncated, File.ReadAllBytes(path));
    }

    [Fact]
    public void Remove_ReplacesNothingWhenTheFileCannotBeCleanedSafely()
    {
        var truncated = MetadataFixtures.Jpeg().Original[..^5];
        var path = Write("photo.jpg", truncated);

        var result = new MetadataRemover().Remove([path], replaceOriginals: true);

        Assert.Equal(0, result.FilesRemoved);
        Assert.Single(result.Failures);
        Assert.Equal(truncated, File.ReadAllBytes(path));
    }

    [Fact]
    public void Remove_SkipsFilesThatAreUnsupportedOrAlreadyClean()
    {
        var text = Write("notes.txt", "hello"u8.ToArray());
        var clean = Write("plain.jpg", MetadataFixtures.CleanJpeg());

        var result = new MetadataRemover().Remove([text, clean], replaceOriginals: false);

        Assert.Equal(0, result.FilesRemoved);
        Assert.Empty(result.Failures);
        Assert.Empty(result.Copies);
        Assert.Equal(2, _root.EnumerateFiles().Count());
    }

    [Fact]
    public void Remove_ReportsAFileThatNoLongerExists()
    {
        var result = new MetadataRemover().Remove([Path.Combine(_root.FullName, "gone.jpg")], replaceOriginals: false);

        Assert.Equal(0, result.FilesRemoved);
        Assert.Equal(Path.Combine(_root.FullName, "gone.jpg"), Assert.Single(result.Failures).Path);
    }

    [Fact]
    public void Remove_CleansTheFilesItCanAfterOneFails()
    {
        var broken = Write("broken.png", MetadataFixtures.Png().Original[..^6]);
        var good = Write("photo.jpg", MetadataFixtures.Jpeg().Original);

        var result = new MetadataRemover().Remove([broken, good], replaceOriginals: true);

        Assert.Equal(1, result.FilesRemoved);
        Assert.Single(result.Failures);
        Assert.Equal(MetadataFixtures.Jpeg().Cleaned, File.ReadAllBytes(good));
    }

    [Fact]
    public void NextCopyPath_StepsPastTheNamesAlreadyTaken()
    {
        var path = Write("photo.jpg", []);
        File.WriteAllBytes(Path.Combine(_root.FullName, "photo (clean).jpg"), []);
        File.WriteAllBytes(Path.Combine(_root.FullName, "photo (clean 2).jpg"), []);

        var next = MetadataRemover.NextCopyPath(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(Path.Combine(_root.FullName, "photo (clean 3).jpg"), next);
    }

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(_root.FullName, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string Describe(MetadataRemovalResult result) =>
        string.Join(Environment.NewLine, result.Failures.Select(failure => $"{failure.Path}: {failure.Reason}"));
}
