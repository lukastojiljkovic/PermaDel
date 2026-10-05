using System.Net;
using System.Security.Cryptography;
using System.Text;
using PermaDel.Core.Updates;

namespace PermaDel.Core.Tests.Updates;

public sealed class UpdateVersionTests
{
    [Theory]
    [InlineData("v0.1.0", 0, 1, 0)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("v12.34.56", 12, 34, 56)]
    public void Accepts_three_part_tags(string tag, int major, int minor, int patch)
    {
        Assert.True(UpdateVersion.TryParseTag(tag, out var version));
        Assert.Equal(new Version(major, minor, patch), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2.3")]
    [InlineData("v1.2")]
    [InlineData("v1.2.3.4")]
    [InlineData("v1.2.3-rc.1")]
    [InlineData("v1.2.x")]
    [InlineData("v")]
    [InlineData("v1..3")]
    public void Rejects_anything_else(string? tag) => Assert.False(UpdateVersion.TryParseTag(tag, out _));
}

public sealed class ReleaseReaderTests
{
    [Fact]
    public void Matches_the_installer_and_its_sidecar_by_exact_name()
    {
        Assert.True(ReleaseReader.TryParse(Releases.Json("v1.2.0"), out var release, out var failure));

        Assert.Null(failure);
        Assert.Equal(new Version(1, 2, 0), release!.Version);
        Assert.Equal("PermaDel-1.2.0-Setup.exe", release.InstallerName);
        Assert.Equal("PermaDel-1.2.0-Setup.exe.sha256", release.SidecarName);
        Assert.EndsWith("/PermaDel-1.2.0-Setup.exe", release.InstallerUrl);
        Assert.EndsWith(".sha256", release.SidecarUrl);
    }

    [Fact]
    public void A_release_without_the_sidecar_still_parses_but_has_no_checksum()
    {
        Assert.True(ReleaseReader.TryParse(Releases.Json("v1.2.0", sidecar: false), out var release, out _));

        Assert.NotNull(release!.InstallerUrl);
        Assert.Null(release.SidecarUrl);
        Assert.Null(release.SidecarName);
    }

    [Fact]
    public void An_asset_url_from_another_host_is_ignored()
    {
        var json = Releases.Json("v1.2.0", sidecar: false, installerUrl: "https://evil.example/PermaDel-1.2.0-Setup.exe");

        Assert.True(ReleaseReader.TryParse(json, out var release, out _));
        Assert.Null(release!.InstallerUrl);
    }

    [Fact]
    public void An_unparseable_tag_is_a_failure()
    {
        Assert.False(ReleaseReader.TryParse(Releases.Json("nightly"), out _, out var failure));
        Assert.Equal("the release tag is not a version", failure);
    }

    [Fact]
    public void Malformed_json_is_a_failure()
    {
        Assert.False(ReleaseReader.TryParse("not json", out _, out var failure));
        Assert.Equal("the release data was not valid JSON", failure);
    }
}

public sealed class SidecarParserTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("  ")]
    [InlineData(" ")]
    [InlineData("\r\n")]
    public void Accepts_whitespace_between_the_hash_and_the_name(string separator)
    {
        Assert.True(SidecarParser.TryParse($"{Hash.ToUpperInvariant()}{separator}PermaDel-1.2.0-Setup.exe", "PermaDel-1.2.0-Setup.exe", out var hash));
        Assert.Equal(Hash, hash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("00ff  PermaDel-1.2.0-Setup.exe")]
    [InlineData("zz23456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  PermaDel-1.2.0-Setup.exe")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  Other.exe")]
    public void Rejects_malformed_bodies(string body) =>
        Assert.False(SidecarParser.TryParse(body, "PermaDel-1.2.0-Setup.exe", out _));
}

public sealed class UpdateCheckerTests
{
    private static UpdateChecker Checker(StubHandler handler) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, new Version(1, 1, 0));

    [Fact]
    public async Task Sends_the_GitHub_headers_and_a_newer_release_wins()
    {
        var handler = new StubHandler(_ => Releases.JsonResponse(Releases.Json("v1.2.0")));

        var result = await Checker(handler).CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(new Version(1, 2, 0), result.Release!.Version);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("PermaDel/1.1.0", request.Headers.UserAgent.ToString());
        Assert.Contains("application/vnd.github+json", request.Headers.Accept.ToString());
        Assert.Equal("2022-11-28", Assert.Single(request.Headers.GetValues("X-GitHub-Api-Version")));
    }

    [Fact]
    public async Task An_equal_or_older_release_is_up_to_date()
    {
        var handler = new StubHandler(_ => Releases.JsonResponse(Releases.Json("v1.1.0")));

        var result = await Checker(handler).CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task A_404_means_no_releases_yet()
    {
        var handler = new StubHandler(_ => Releases.JsonResponse("{}", HttpStatusCode.NotFound));

        var result = await Checker(handler).CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.NoReleases, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_rate_limit_answer_is_reported_as_such(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => Releases.JsonResponse("{}", status));

        var result = await Checker(handler).CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.RateLimited, result.Status);
    }

    [Fact]
    public async Task Malformed_json_fails_the_check()
    {
        var handler = new StubHandler(_ => Releases.JsonResponse("not json"));

        var result = await Checker(handler).CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal("the release data was not valid JSON", result.Detail);
    }

    [Fact]
    public async Task A_timed_out_request_fails_the_check()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timeout", new TimeoutException()));

        var result = await Checker(handler).CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal("the request timed out", result.Detail);
    }
}

public sealed class UpdateServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("permadel-updates-");

    public void Dispose() => _directory.Delete(recursive: true);

    private UpdateService Service(HttpClient http, FakePreferences preferences, TestTimeProvider clock, FakeLauncher launcher) =>
        new(http, new Version(1, 1, 0), clock, preferences, launcher, _directory.FullName);

    [Fact]
    public async Task The_automatic_check_runs_at_most_once_a_day()
    {
        var preferences = new FakePreferences { LastUpdateCheckUtc = Now.AddHours(-1) };
        var clock = new TestTimeProvider(Now);
        var handler = new StubHandler(_ => Releases.JsonResponse(Releases.Json("v1.1.0")));
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var service = Service(http, preferences, clock, new FakeLauncher());

        var throttled = await service.CheckAsync(manual: false, CancellationToken.None);
        Assert.Equal(UpdateCheckStatus.UpToDate, throttled.Status);
        Assert.Empty(handler.Requests);

        clock.Now = Now.AddHours(25);
        await service.CheckAsync(manual: false, CancellationToken.None);
        Assert.Single(handler.Requests);
        Assert.Equal(clock.Now, preferences.LastUpdateCheckUtc);
    }

    [Fact]
    public async Task Turning_the_toggle_off_stops_the_automatic_check()
    {
        var handler = new StubHandler(_ => Releases.JsonResponse(Releases.Json("v1.2.0")));
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var service = Service(http, new FakePreferences { CheckForUpdatesAutomatically = false }, new TestTimeProvider(Now), new FakeLauncher());

        var result = await service.CheckAsync(manual: false, CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_manual_check_ignores_the_throttle_and_the_toggle()
    {
        var preferences = new FakePreferences { CheckForUpdatesAutomatically = false, LastUpdateCheckUtc = Now };
        var handler = new StubHandler(_ => Releases.JsonResponse(Releases.Json("v1.2.0")));
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var service = Service(http, preferences, new TestTimeProvider(Now), new FakeLauncher());

        var result = await service.CheckAsync(manual: true, CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_failed_check_does_not_reset_the_throttle()
    {
        var preferences = new FakePreferences { LastUpdateCheckUtc = Now.AddHours(-25) };
        var handler = new StubHandler(_ => throw new HttpRequestException("offline"));
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var service = Service(http, preferences, new TestTimeProvider(Now), new FakeLauncher());

        var result = await service.CheckAsync(manual: false, CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal(Now.AddHours(-25), preferences.LastUpdateCheckUtc);
    }

    [Fact]
    public async Task Download_then_install_verifies_the_file_and_launches_it()
    {
        var bytes = Encoding.ASCII.GetBytes("installer bytes");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        const string installer = "PermaDel-1.2.0-Setup.exe";
        using var server = new LocalHttpServer(new Dictionary<string, byte[]>
        {
            ["/installer"] = bytes,
            ["/sidecar"] = Encoding.ASCII.GetBytes($"{hash}  {installer}\r\n"),
        });
        var launcher = new FakeLauncher();
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var service = new UpdateService(http, new Version(1, 1, 0), new TestTimeProvider(Now), new FakePreferences(), launcher, _directory.FullName);

        var download = await service.DownloadAsync(Releases.Local(server.BaseUrl, installer), CancellationToken.None);

        Assert.True(download.Success, download.Error);
        Assert.Equal(InstallOutcome.Started, await service.InstallAsync(download, CancellationToken.None));
        Assert.Equal(new[] { download.Path! }, launcher.Launched);
    }

    [Fact]
    public async Task A_file_that_changed_after_the_download_is_not_launched()
    {
        var bytes = Encoding.ASCII.GetBytes("installer bytes");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        const string installer = "PermaDel-1.2.0-Setup.exe";
        using var server = new LocalHttpServer(new Dictionary<string, byte[]>
        {
            ["/installer"] = bytes,
            ["/sidecar"] = Encoding.ASCII.GetBytes($"{hash}  {installer}\r\n"),
        });
        var launcher = new FakeLauncher();
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var service = new UpdateService(http, new Version(1, 1, 0), new TestTimeProvider(Now), new FakePreferences(), launcher, _directory.FullName);

        var download = await service.DownloadAsync(Releases.Local(server.BaseUrl, installer), CancellationToken.None);
        Assert.True(download.Success, download.Error);
        await File.WriteAllTextAsync(download.Path!, "tampered", CancellationToken.None);

        Assert.Equal(InstallOutcome.Failed, await service.InstallAsync(download, CancellationToken.None));
        Assert.Empty(launcher.Launched);
    }
}

public sealed class UpdateDownloaderTests : IDisposable
{
    private const string Installer = "PermaDel-1.2.0-Setup.exe";
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("permadel-updates-");

    public void Dispose() => _directory.Delete(recursive: true);

    private UpdateDownloader Downloader(HttpClient http) => new(http, _directory.FullName);

    private static HttpClient Client() => new() { Timeout = Timeout.InfiniteTimeSpan };

    [Fact]
    public async Task A_good_download_is_verified_and_kept()
    {
        var bytes = Encoding.ASCII.GetBytes("installer bytes");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        using var server = new LocalHttpServer(new Dictionary<string, byte[]>
        {
            ["/installer"] = bytes,
            ["/sidecar"] = Encoding.ASCII.GetBytes($"{hash}  {Installer}\r\n"),
        });
        var progress = new RecordingProgress<UpdateProgress>();
        using var http = Client();

        var result = await Downloader(http).DownloadAsync(Releases.Local(server.BaseUrl, Installer), CancellationToken.None, progress);

        Assert.True(result.Success, result.Error);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(result.Path!, CancellationToken.None));
        Assert.Contains(progress.Values, value => value.BytesReceived == bytes.Length);
    }

    [Fact]
    public async Task A_corrupted_download_is_rejected_and_deleted()
    {
        var bytes = Encoding.ASCII.GetBytes("installer bytes");
        var otherHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes("different")));
        using var server = new LocalHttpServer(new Dictionary<string, byte[]>
        {
            ["/installer"] = bytes,
            ["/sidecar"] = Encoding.ASCII.GetBytes($"{otherHash}  {Installer}\r\n"),
        });
        using var http = Client();

        var result = await Downloader(http).DownloadAsync(Releases.Local(server.BaseUrl, Installer), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("did not match its checksum", result.Error);
        Assert.Empty(_directory.EnumerateFiles());
    }

    [Fact]
    public async Task A_release_without_a_sidecar_is_refused_before_downloading()
    {
        using var http = Client();

        var result = await Downloader(http).DownloadAsync(Releases.Local("http://127.0.0.1:1", Installer, sidecar: false), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("no checksum file", result.Error);
        Assert.Empty(_directory.EnumerateFiles());
    }

    [Fact]
    public async Task A_malformed_sidecar_is_refused()
    {
        using var server = new LocalHttpServer(new Dictionary<string, byte[]>
        {
            ["/installer"] = Encoding.ASCII.GetBytes("installer bytes"),
            ["/sidecar"] = Encoding.ASCII.GetBytes("not a checksum"),
        });
        using var http = Client();

        var result = await Downloader(http).DownloadAsync(Releases.Local(server.BaseUrl, Installer), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("malformed", result.Error);
        Assert.Empty(_directory.EnumerateFiles());
    }

    [Fact]
    public async Task A_cancelled_download_leaves_no_file()
    {
        using var http = Client();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Downloader(http).DownloadAsync(Releases.Local("http://127.0.0.1:1", Installer), cancellation.Token));

        Assert.Empty(_directory.EnumerateFiles());
    }

    [Fact]
    public async Task Stale_downloads_are_removed_before_a_new_one()
    {
        var stale = Path.Combine(_directory.FullName, "PermaDel-1.1.0-Setup.exe");
        await File.WriteAllTextAsync(stale, "old", CancellationToken.None);
        var bytes = Encoding.ASCII.GetBytes("installer bytes");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        using var server = new LocalHttpServer(new Dictionary<string, byte[]>
        {
            ["/installer"] = bytes,
            ["/sidecar"] = Encoding.ASCII.GetBytes($"{hash}  {Installer}\r\n"),
        });
        using var http = Client();

        var result = await Downloader(http).DownloadAsync(Releases.Local(server.BaseUrl, Installer), CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.False(File.Exists(stale));
    }
}
