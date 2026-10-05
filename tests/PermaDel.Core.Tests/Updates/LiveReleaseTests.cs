using PermaDel.Core.Updates;

namespace PermaDel.Core.Tests.Updates;

/// <summary>
/// A manual check of the release contract against a payload captured from the
/// live repository. It fetches nothing itself (this sandbox cannot do TLS) and
/// returns immediately unless <c>PERMADEL_RELEASE_JSON</c> points at a
/// captured payload, so CI stays offline.
///
/// Capture it with:
/// <c>curl.exe -H "User-Agent: PermaDel/1.1.0" https://api.github.com/repos/lukastojiljkovic/PermaDel/releases/latest -o payload.json</c>
/// then run
/// <c>dotnet test --project tests\PermaDel.Core.Tests -c Release --filter LiveRelease</c>
/// with the variable set.
/// </summary>
public sealed class LiveReleaseTests
{
    [Fact]
    public async Task The_live_release_matches_the_contract()
    {
        var path = Environment.GetEnvironmentVariable("PERMADEL_RELEASE_JSON");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        var json = await File.ReadAllTextAsync(path, CancellationToken.None);
        Assert.True(ReleaseReader.TryParse(json, out var release, out var failure), failure);
        Assert.NotNull(release!.InstallerName);
        Console.WriteLine($"tag: {release.Tag}");
        Console.WriteLine($"installer: {release.InstallerName} ({release.InstallerUrl})");
        Console.WriteLine(release.SidecarName is null ? "sidecar: missing" : $"sidecar: {release.SidecarName}");

        // Forcing an old version is what the DEBUG override does in the app.
        var forcedOld = new Version(0, 1, 0);
        Console.WriteLine(release.Version > forcedOld
            ? $"a user on {forcedOld} would be told {release.Version} is available"
            : $"a user on {forcedOld} is up to date");
        Assert.True(release.Version > forcedOld);

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var service = new UpdateService(
            http,
            forcedOld,
            TimeProvider.System,
            new FakePreferences(),
            new FakeLauncher(),
            Path.GetTempPath());
        var download = await service.DownloadAsync(release, CancellationToken.None);
        Console.WriteLine($"installer download refused: success={download.Success}, error={download.Error}");
        Console.WriteLine($"release page offered: {download.ReleasePageUrl}");

        Assert.False(download.Success);
        Assert.Contains("no checksum file", download.Error);
        Assert.Equal(release.PageUrl, download.ReleasePageUrl);
    }
}
