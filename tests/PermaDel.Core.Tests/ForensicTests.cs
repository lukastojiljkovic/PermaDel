using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Xunit.Abstractions;

namespace PermaDel.Core.Tests;

/// <summary>
/// Shreds data on a freshly formatted NTFS virtual disk, detaches it and scans the raw disk image for the data, the way
/// a forensic recovery tool would. Creating the disk requires administrator rights, so these tests run separately:
/// <c>dotnet test tests/PermaDel.Core.Tests --filter Category=Forensics</c> from an elevated terminal.
/// </summary>
[Trait("Category", "Forensics")]
public sealed class ForensicTests : IDisposable
{
    private const int PageSize = 4096;

    private readonly ITestOutputHelper _output;
    private readonly string _image = Path.Combine(Path.GetTempPath(), $"PermaDelForensics-{Guid.NewGuid():N}.vhd");
    private readonly string _root;
    private bool _attached;

    public ForensicTests(ITestOutputHelper output)
    {
        _output = output;
        Assert.True(new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator), "Run the forensic tests from an elevated terminal.");

        var letter = Enumerable.Range('D', 'Z' - 'D' + 1).Select(code => (char)code).Reverse()
            .First(candidate => DriveInfo.GetDrives().All(drive => char.ToUpperInvariant(drive.Name[0]) != candidate));
        _root = $@"{letter}:\";

        _attached = true;
        RunDiskpart($"""
            create vdisk file="{_image}" maximum=64 type=fixed
            select vdisk file="{_image}"
            attach vdisk
            create partition primary
            format fs=ntfs quick label=PermaDel
            assign letter={letter}
            """);

        for (var attempt = 0; attempt < 50 && !Directory.Exists(_root); attempt++)
            Thread.Sleep(200);
    }

    public void Dispose()
    {
        if (_attached)
            Detach(throwOnError: false);
        File.Delete(_image);
    }

    [Fact]
    public void ShreddedDataIsGoneFromTheRawDisk()
    {
        string contents = CreateMarker(), smallContents = CreateMarker(), streamContents = CreateMarker(), name = CreateMarker(), control = CreateMarker();

        WriteFile("large.bin", Repeat(contents, 1024 * 1024));
        WriteFile("small.txt", Encoding.ASCII.GetBytes(smallContents));
        WriteFile("stream.txt", [0]);
        WriteFile("stream.txt:secret", Repeat(streamContents, 64 * 1024));
        WriteFile(name + ".txt", [0]);
        WriteFile("control.bin", Repeat(control, 64 * 1024));

        var result = new Shredder(1).Shred(new[] { "large.bin", "small.txt", "stream.txt", name + ".txt" }.Select(file => _root + file));
        File.Delete(_root + "control.bin");
        Detach(throwOnError: true);

        var image = File.ReadAllBytes(_image);
        var findings = new Dictionary<string, List<int>>
        {
            ["File contents"] = FindAll(image, contents),
            ["Small file contents (stored in the MFT record)"] = FindAll(image, smallContents),
            ["Alternate data stream contents"] = FindAll(image, streamContents),
            ["File name"] = FindAll(image, name),
            ["Control file removed by an ordinary delete"] = FindAll(image, control),
        };
        foreach (var (label, offsets) in findings)
            _output.WriteLine($"{label}: {(offsets.Count == 0 ? "not found" : $"{offsets.Count} copies, in pages starting with {string.Join(", ", offsets.Select(offset => PageSignature(image, offset)).Distinct())}")}");

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Failures));
        Assert.True(findings["Control file removed by an ordinary delete"].Count > 0, "The scan must find data removed by an ordinary delete, otherwise it proves nothing.");
        Assert.Empty(findings["File contents"]);
        Assert.Empty(findings["Alternate data stream contents"]);

        // NTFS records metadata changes in its circular transaction log ($LogFile, made of "RCRD" pages), which keeps them
        // until newer activity overwrites them. Everywhere else, including MFT records and directory indexes, they must be gone.
        Assert.All(findings["File name"], offset => Assert.Equal("RCRD", PageSignature(image, offset)));
        Assert.All(findings["Small file contents (stored in the MFT record)"], offset => Assert.Equal("RCRD", PageSignature(image, offset)));
    }

    private static string CreateMarker() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static byte[] Repeat(string marker, int length)
    {
        var pattern = Encoding.ASCII.GetBytes(marker);
        return Enumerable.Range(0, length).Select(index => pattern[index % pattern.Length]).ToArray();
    }

    /// <summary>Offsets of the marker as ASCII (file contents) and as UTF-16 (the encoding of NTFS names).</summary>
    private static List<int> FindAll(byte[] image, string marker)
    {
        var offsets = new List<int>();
        foreach (var pattern in new[] { Encoding.ASCII.GetBytes(marker), Encoding.Unicode.GetBytes(marker) })
        {
            int start = 0, index;
            while ((index = image.AsSpan(start).IndexOf(pattern)) >= 0)
            {
                offsets.Add(start + index);
                start += index + 1;
            }
        }
        return offsets;
    }

    /// <summary>The first four bytes of the 4 KB page holding the offset: "FILE" for MFT records, "INDX" for directory indexes, "RCRD" for the log.</summary>
    private static string PageSignature(byte[] image, int offset) =>
        new(Encoding.ASCII.GetString(image, offset - offset % PageSize, 4).Select(c => char.IsAsciiLetterOrDigit(c) ? c : '.').ToArray());

    /// <summary>Flushes to the disk, so the data really is on it before it's shredded.</summary>
    private void WriteFile(string name, byte[] content)
    {
        using var stream = new FileStream(_root + name, FileMode.Create, FileAccess.Write);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }

    private void Detach(bool throwOnError)
    {
        RunDiskpart($"""
            select vdisk file="{_image}"
            detach vdisk
            """, throwOnError);
        _attached = false;
    }

    private static void RunDiskpart(string script, bool throwOnError = true)
    {
        var scriptFile = Path.GetTempFileName();
        File.WriteAllText(scriptFile, script);
        try
        {
            using var process = Process.Start(new ProcessStartInfo("diskpart.exe", $"/s \"{scriptFile}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            })!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (throwOnError)
                Assert.True(process.ExitCode == 0, output);
        }
        finally
        {
            File.Delete(scriptFile);
        }
    }
}
