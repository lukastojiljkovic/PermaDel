using System.Diagnostics;

namespace PermaDel.Core.Tests;

public sealed class ShredderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("PermaDelTests-");

    public void Dispose()
    {
        // Verbatim path, so leftovers with names that Win32 would normalize can be removed as well.
        var root = new DirectoryInfo(Verbatim(_root.FullName));
        if (!root.Exists)
            return;

        // Top level only: recursive enumeration follows junctions, which would lead outside the test folder.
        foreach (var file in root.EnumerateFiles("*", new EnumerationOptions { AttributesToSkip = 0 }))
            file.Attributes = FileAttributes.Normal;
        root.Delete(recursive: true);
    }

    public static TheoryData<string> ProtectedLocations => new()
    {
        Path.GetPathRoot(Environment.SystemDirectory)!,
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Path.Combine(Environment.SystemDirectory, "kernel32.dll"),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\",
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!,
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Verbatim(Environment.GetFolderPath(Environment.SpecialFolder.Windows)),
        @"\\.\PhysicalDrive0",
        AppContext.BaseDirectory,
    };

    [Theory]
    [InlineData(Shredder.MinPasses - 1)]
    [InlineData(Shredder.MaxPasses + 1)]
    public void Constructor_RejectsPassCountOutOfRange(int passes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Shredder(passes));

    [Theory]
    [MemberData(nameof(ProtectedLocations))]
    public void IsProtected_BlocksCriticalLocations(string path) => Assert.True(Shredder.IsProtected(path));

    [Fact]
    public void IsProtected_AllowsUserData() => Assert.False(Shredder.IsProtected(_root.FullName));

    [Fact]
    public void IsProtected_ResolvesJunctionsInThePath()
    {
        var link = Path.Combine(_root.FullName, "windows-link");
        Mklink("/J", link, Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        Assert.False(Shredder.IsProtected(link));
        Assert.True(Shredder.IsProtected(Path.Combine(link, "System32")));

        // A recursive delete of the test folder tries DeleteVolumeMountPoint on junctions, which requires administrator rights.
        Directory.Delete(link);
    }

    [Fact]
    public void Overwrite_ReplacesEveryBlockWithRandomData()
    {
        const int length = 3 * 1024 * 1024 + 17;
        var file = CreateFile("zeros.bin", new byte[length]);

        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Write))
            new Shredder(2).Overwrite(stream, null, CancellationToken.None);

        var data = File.ReadAllBytes(file);
        Assert.Equal(length, data.Length);
        Assert.All(data.Chunk(512), block => Assert.Contains(block, value => value != 0));
    }

    [Fact]
    public void Shred_DeletesFileWithoutLeftovers()
    {
        var file = CreateFile("secret.txt", "top secret"u8.ToArray());

        var result = new Shredder(3).Shred([file]);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.FilesShredded);
        Assert.Empty(_root.EnumerateFileSystemInfos());
    }

    [Fact]
    public void Shred_RemovesDirectoryTreeIncludingReadOnlyAndHiddenItems()
    {
        var readOnly = CreateFile(@"tree\a.txt", new byte[100]);
        var hidden = CreateFile(@"tree\nested\b.bin", new byte[5000]);
        CreateFile(@"tree\nested\deeper\empty.dat", []);
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(Path.GetDirectoryName(hidden)!, FileAttributes.Directory | FileAttributes.ReadOnly);

        var result = new Shredder(1).Shred([Path.Combine(_root.FullName, "tree")]);

        Assert.True(result.Succeeded, Describe(result));
        Assert.Equal(3, result.FilesShredded);
        Assert.Equal(3, result.DirectoriesRemoved);
        Assert.Empty(_root.EnumerateFileSystemInfos());
    }

    [Fact]
    public void Shred_DestroysAlternateDataStreams()
    {
        var file = CreateFile("ads.txt", new byte[10]);
        File.WriteAllText(file + ":hidden", "classified");
        Assert.Contains(DataStreams.Enumerate(file), stream => stream is { Name: ":hidden:$DATA", Length: 10 });

        var result = new Shredder(1).Shred([file]);

        Assert.True(result.Succeeded);
        Assert.Empty(_root.EnumerateFileSystemInfos());
    }

    [Fact]
    public void Shred_HandlesPathsLongerThanMaxPath()
    {
        var relative = Path.Combine(string.Join('\\', Enumerable.Repeat(new string('d', 60), 5)), "deep.bin");
        var file = CreateFile(relative, new byte[4096]);
        Assert.True(file.Length > 260);

        var result = new Shredder(1).Shred([Path.Combine(_root.FullName, relative.Split('\\')[0])]);

        Assert.True(result.Succeeded, Describe(result));
        Assert.Empty(_root.EnumerateFileSystemInfos());
    }

    [Theory]
    [InlineData(".")]
    [InlineData(" ")]
    public void Shred_NameEndingInPeriodOrSpaceNeverResolvesToLookalike(string suffix)
    {
        var lookalike = CreateFile("report", "keep"u8.ToArray());
        var target = lookalike + suffix;
        File.WriteAllText(Verbatim(target), "destroy");

        var result = new Shredder(1).Shred([target]);

        Assert.True(result.Succeeded, Describe(result));
        Assert.Equal("keep", File.ReadAllText(lookalike));
        Assert.False(File.Exists(Verbatim(target)));
    }

    [Theory]
    [InlineData("...")]
    [InlineData(".. ")]
    public void Shred_NeverLeavesSelectionThroughNamesThatLookLikeRelativeSegments(string name)
    {
        var outside = CreateFile("outside.txt", "keep"u8.ToArray());
        var selection = Directory.CreateDirectory(Path.Combine(_root.FullName, "selection")).FullName;
        Directory.CreateDirectory(Verbatim(Path.Combine(selection, name)));
        File.WriteAllText(Verbatim(Path.Combine(selection, name, "inner.txt")), "destroy");

        var result = new Shredder(1).Shred([selection]);

        Assert.True(result.Succeeded, Describe(result));
        Assert.Equal("keep", File.ReadAllText(outside));
        Assert.False(Directory.Exists(selection));
    }

    [Fact]
    public void Shred_RemovesJunctionWithoutTouchingItsTarget()
    {
        var outside = CreateFile(@"outside\keep.txt", "keep me"u8.ToArray());
        var selection = Directory.CreateDirectory(Path.Combine(_root.FullName, "selection")).FullName;
        Mklink("/J", Path.Combine(selection, "link"), Path.GetDirectoryName(outside)!);

        var result = new Shredder(1).Shred([selection]);

        Assert.True(result.Succeeded);
        Assert.False(Directory.Exists(selection));
        Assert.Equal("keep me", File.ReadAllText(outside));
    }

    [Fact]
    public void Shred_RefusesFileWithOtherHardLinks()
    {
        var original = CreateFile("original.txt", "shared data"u8.ToArray());
        var link = Path.Combine(_root.FullName, "link.txt");
        Mklink("/H", link, original);

        var result = new Shredder(1).Shred([link]);

        Assert.Single(result.Failures);
        Assert.True(File.Exists(link));
        Assert.Equal("shared data", File.ReadAllText(original));
    }

    [Fact]
    public void Shred_RefusesFileWhoseDataIsNotOnThisPc()
    {
        var file = CreateFile("cloud.txt", "remote"u8.ToArray());
        File.SetAttributes(file, FileAttributes.Offline);

        var result = new Shredder(1).Shred([file]);

        Assert.Single(result.Failures);
        Assert.Equal("remote", File.ReadAllText(file));
    }

    [Fact]
    public void Shred_IgnoresItemsNestedInsideAnotherSelection()
    {
        var file = CreateFile(@"folder\inner.txt", new byte[100]);

        var result = new Shredder(1).Shred([file, Path.GetDirectoryName(file)!]);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.FilesShredded);
    }

    [Fact]
    public void Shred_ReportsLockedFileAndLeavesItsFolderIntact()
    {
        var file = CreateFile(@"busy\locked.txt", new byte[100]);

        using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = new Shredder(1).Shred([Path.GetDirectoryName(file)!]);
            Assert.Equal(2, result.Failures.Count);
        }

        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Shred_LeavesFileThatCannotBeOpenedExactlyAsItWas()
    {
        const FileAttributes attributes = FileAttributes.ReadOnly | FileAttributes.Hidden;
        var file = CreateFile("locked.txt", "intact"u8.ToArray());
        File.SetAttributes(file, attributes);

        using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Single(new Shredder(1).Shred([file]).Failures);

        Assert.Equal(attributes, File.GetAttributes(file));
        Assert.Equal("intact", File.ReadAllText(file));
    }

    [Fact]
    public void Shred_ReportsMissingPath()
    {
        var result = new Shredder(1).Shred([Path.Combine(_root.FullName, "missing.txt")]);

        Assert.Single(result.Failures);
    }

    [Fact]
    public void Shred_ThrowsWhenCancelledAndKeepsFiles()
    {
        var file = CreateFile("keep.txt", new byte[10]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => new Shredder(1).Shred([file], cancellationToken: cancellation.Token));
        Assert.True(File.Exists(file));
    }

    private string CreateFile(string relativePath, byte[] content)
    {
        var path = Path.Combine(_root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static string Verbatim(string path) => @"\\?\" + path;

    private static string Describe(ShredResult result) => string.Join(Environment.NewLine, result.Failures);

    private static void Mklink(string kind, string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink {kind} \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
