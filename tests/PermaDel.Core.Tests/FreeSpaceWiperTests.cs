namespace PermaDel.Core.Tests;

public sealed class FreeSpaceWiperTests : IDisposable
{
    private const int Cluster = 4096;

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("PermaDelWipeTests-");

    public void Dispose()
    {
        foreach (var file in _root.EnumerateFiles("*", new EnumerationOptions { AttributesToSkip = 0 }))
            file.Attributes = FileAttributes.Normal;
        _root.Delete(recursive: true);
    }

    [Fact]
    public void Wipe_FillsEveryFreeByteAndShrinksToTheClusterSize()
    {
        var capacity = 3 * 1024 * 1024 + 5 * Cluster;
        var volume = new FakeVolume(Sub("full"), Cluster, WipeFileSystem.Ntfs, capacity);

        var result = new FreeSpaceWiper().Wipe(volume, cleanFileTable: false, progress: null, CancellationToken.None);

        Assert.Equal(capacity, result.BytesWritten);
        Assert.Equal(capacity, volume.BytesWritten);
        Assert.Equal(Cluster, volume.Sizes.Where(size => size > 0).Min());
        Assert.Empty(volume.EnumerateRoot());
    }

    [Fact]
    public void Wipe_FlushesEveryFileItWroteBeforeRemovingIt()
    {
        var volume = new FakeVolume(Sub("flush"), Cluster, WipeFileSystem.Ntfs, 3 * 1024 * 1024 + 5 * Cluster);

        new FreeSpaceWiper().Wipe(volume, cleanFileTable: false, progress: null, CancellationToken.None);

        // Including the files the volume filled up in the middle of.
        Assert.Equal(0, volume.UnflushedFiles);
    }

    [Fact]
    public void Wipe_CleansTheFileTableOnlyOnNtfs()
    {
        var ntfs = new FakeVolume(Sub("ntfs"), Cluster, WipeFileSystem.Ntfs, capacity: 1024 * 1024, fileTableCapacity: 4);
        var ntfsResult = new FreeSpaceWiper().Wipe(ntfs, cleanFileTable: true, progress: null, CancellationToken.None);
        Assert.True(ntfsResult.FileTableCleaned);
        Assert.Equal(4, ntfsResult.FileTableEntries);

        var exfat = new FakeVolume(Sub("exfat"), Cluster, WipeFileSystem.ExFat, capacity: 1024 * 1024, fileTableCapacity: 4);
        var exfatResult = new FreeSpaceWiper().Wipe(exfat, cleanFileTable: true, progress: null, CancellationToken.None);
        Assert.False(exfatResult.FileTableCleaned);
        Assert.Equal(0, exfatResult.FileTableEntries);
        Assert.Equal(0, exfat.SmallFilesCreated);
    }

    [Fact]
    public void Wipe_StopsTheFileTablePhaseAtTheCap()
    {
        var volume = new FakeVolume(Sub("cap"), Cluster, WipeFileSystem.Ntfs, capacity: 1024 * 1024, fileTableCapacity: int.MaxValue);

        var result = new FreeSpaceWiper(maxFileTableEntries: 5).Wipe(volume, cleanFileTable: true, progress: null, CancellationToken.None);

        Assert.Equal(5, result.FileTableEntries);
        Assert.Equal(5, volume.SmallFilesCreated);
    }

    [Fact]
    public void Wipe_RemovesTheFolderWhenCancelledWhileWriting()
    {
        var volume = new FakeVolume(Sub("cancel-data"), Cluster, WipeFileSystem.Ntfs, capacity: 8 * 1024 * 1024);
        using var cancellation = new CancellationTokenSource();
        var progress = new CancellingProgress(update =>
        {
            if (update.Phase == WipePhase.WritingFreeSpace)
                cancellation.Cancel();
        });

        Assert.Throws<OperationCanceledException>(() => new FreeSpaceWiper().Wipe(volume, cleanFileTable: false, progress, cancellation.Token));
        Assert.Empty(volume.EnumerateRoot());
    }

    [Fact]
    public void Wipe_RemovesTheFolderWhenCancelledWhileCleaningTheFileTable()
    {
        var volume = new FakeVolume(Sub("cancel-table"), Cluster, WipeFileSystem.Ntfs, capacity: 1024 * 1024, fileTableCapacity: int.MaxValue);
        using var cancellation = new CancellationTokenSource();
        var progress = new CancellingProgress(update =>
        {
            if (update.Phase == WipePhase.CleaningFileTable)
                cancellation.Cancel();
        });

        Assert.Throws<OperationCanceledException>(() => new FreeSpaceWiper().Wipe(volume, cleanFileTable: true, progress, cancellation.Token));
        Assert.Empty(volume.EnumerateRoot());
    }

    [Fact]
    public void Wipe_RemovesTheFolderWhenAWriteThrows()
    {
        var volume = new FakeVolume(Sub("throws"), Cluster, WipeFileSystem.Ntfs, capacity: 8 * 1024 * 1024) { FailAfter = 0 };

        // Not a VolumeFullException, so it must reach the caller rather than shrink the write size.
        Assert.Throws<IOException>(() => new FreeSpaceWiper().Wipe(volume, cleanFileTable: false, progress: null, CancellationToken.None));
        Assert.Empty(volume.EnumerateRoot());
    }

    [Fact]
    public void RemoveLeftovers_RemovesOnlyWipeFolders()
    {
        var volume = new FakeVolume(Sub("leftovers"), Cluster, WipeFileSystem.Ntfs, capacity: 0);
        var wipe = Directory.CreateDirectory(Path.Combine(volume.Root, "PermaDel free space " + Guid.NewGuid().ToString("D"))).FullName;
        var plain = Directory.CreateDirectory(Path.Combine(volume.Root, "PermaDel free space")).FullName;
        var notAGuid = Directory.CreateDirectory(Path.Combine(volume.Root, "PermaDel free space notaguid")).FullName;
        var file = Path.Combine(volume.Root, "PermaDel free space " + Guid.NewGuid().ToString("D"));
        File.WriteAllText(file, "keep");

        var removed = FreeSpaceWiper.RemoveLeftovers(volume);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(wipe));
        Assert.True(Directory.Exists(plain));
        Assert.True(Directory.Exists(notAGuid));
        Assert.True(File.Exists(file));
    }

    [Theory]
    [InlineData(DriveType.Fixed, WipeFileSystem.Ntfs, false, true)]
    [InlineData(DriveType.Fixed, WipeFileSystem.ExFat, false, true)]
    [InlineData(DriveType.Fixed, WipeFileSystem.Fat32, false, true)]
    [InlineData(DriveType.Removable, WipeFileSystem.ExFat, false, true)]
    [InlineData(DriveType.Fixed, WipeFileSystem.Other, false, false)]
    [InlineData(DriveType.Fixed, WipeFileSystem.Ntfs, true, false)]
    [InlineData(DriveType.Network, WipeFileSystem.Ntfs, false, false)]
    [InlineData(DriveType.CDRom, WipeFileSystem.Fat32, false, false)]
    [InlineData(DriveType.NoRootDirectory, WipeFileSystem.Ntfs, false, false)]
    public void Supports_OnlyAcceptsWritableFixedAndRemovableDrivesWithAKnownFileSystem(DriveType type, WipeFileSystem fileSystem, bool readOnly, bool expected) =>
        Assert.Equal(expected, FreeSpaceWiper.Supports(type, fileSystem, readOnly));

    [Theory]
    [InlineData("NTFS", WipeFileSystem.Ntfs)]
    [InlineData("exfat", WipeFileSystem.ExFat)]
    [InlineData("FAT32", WipeFileSystem.Fat32)]
    [InlineData("FAT", WipeFileSystem.Other)]
    [InlineData("", WipeFileSystem.Other)]
    [InlineData(null, WipeFileSystem.Other)]
    public void FromFileSystemName_MapsTheKnownFileSystems(string? name, WipeFileSystem expected) =>
        Assert.Equal(expected, FreeSpaceWiper.FromFileSystemName(name));

    private string Sub(string name) => Path.Combine(_root.FullName, name);

    /// <summary>Reports synchronously, so a test can cancel from inside a progress update.</summary>
    private sealed class CancellingProgress(Action<WipeProgress> onReport) : IProgress<WipeProgress>
    {
        public void Report(WipeProgress value) => onReport(value);
    }

    /// <summary>
    /// A volume rooted at a real temporary directory that stops accepting bulk writes after a chosen number of
    /// bytes and small file table entries after a chosen count.
    /// </summary>
    private sealed class FakeVolume : IWipeVolume
    {
        private readonly long _capacity;
        private readonly int _fileTableCapacity;
        private readonly List<long> _sizes = [];

        public FakeVolume(string root, long clusterSize, WipeFileSystem fileSystem, long capacity, int fileTableCapacity = 0)
        {
            Root = root;
            Directory.CreateDirectory(root);
            ClusterSize = clusterSize;
            FileSystem = fileSystem;
            _capacity = capacity;
            _fileTableCapacity = fileTableCapacity;
        }

        public string Root { get; }

        public long FreeBytes => _capacity - BytesWritten;

        public long ClusterSize { get; }

        public WipeFileSystem FileSystem { get; }

        public long BytesWritten { get; private set; }

        public int SmallFilesCreated { get; private set; }

        /// <summary>Files closed with data written after their last flush.</summary>
        public int UnflushedFiles { get; private set; }

        public long FailAfter { get; init; } = long.MaxValue;

        public IReadOnlyList<long> Sizes => _sizes;

        public IReadOnlyList<WipeEntry> EnumerateRoot() =>
            new DirectoryInfo(Root).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 })
                .Select(info => new WipeEntry(info.FullName, info.Name, info is DirectoryInfo))
                .ToList();

        public string CreateFolder(string name)
        {
            var path = Path.Combine(Root, name);
            var info = Directory.CreateDirectory(path);
            info.Attributes = FileAttributes.Hidden | FileAttributes.NotContentIndexed;
            return path;
        }

        public IWipeFile CreateFile(string folder, string name) => new FakeFile(this, Path.Combine(folder, name));

        public bool TryCreateSmallFile(string folder, string name, ReadOnlySpan<byte> contents)
        {
            if (SmallFilesCreated >= _fileTableCapacity)
                return false;

            SmallFilesCreated++;
            File.WriteAllBytes(Path.Combine(folder, name), contents.ToArray());
            return true;
        }

        public void DeleteFolder(string folder) => Directory.Delete(folder, recursive: true);

        private sealed class FakeFile : IWipeFile
        {
            private readonly FakeVolume _volume;
            private readonly FileStream _stream;
            private long _length;
            private bool _flushed = true;

            public FakeFile(FakeVolume volume, string path)
            {
                _volume = volume;
                _stream = File.Create(path);
            }

            public void Write(ReadOnlySpan<byte> data)
            {
                if (_volume.BytesWritten + data.Length > _volume.FailAfter)
                    throw new IOException("The device reported an error.");
                if (_volume.BytesWritten + data.Length > _volume._capacity)
                    throw new VolumeFullException("The drive ran out of free space.");

                _stream.Write(data);
                _volume.BytesWritten += data.Length;
                _length += data.Length;
                _flushed = false;
            }

            public void Flush()
            {
                _stream.Flush();
                _flushed = true;
            }

            public void Dispose()
            {
                _stream.Dispose();
                _volume._sizes.Add(_length);
                if (!_flushed)
                    _volume.UnflushedFiles++;
            }
        }
    }
}
