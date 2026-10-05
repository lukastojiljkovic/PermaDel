using System.Security.Cryptography;

namespace PermaDel.Core.Updates;

/// <summary>
/// The orchestrator the UI talks to: throttles the automatic check, records
/// the last successful check, downloads through the verifier, and re-verifies
/// the installer immediately before handing it to the launcher.
/// </summary>
public sealed class UpdateService
{
    private static readonly TimeSpan Throttle = TimeSpan.FromHours(24);

    public const string ReleasePageUrl = ReleaseReader.ReleasePageUrl;

    /// <summary>The version checks compare against: the app's own, or the debug override.</summary>
    public Version CurrentVersion { get; }

    private readonly UpdateChecker _checker;
    private readonly UpdateDownloader _downloader;
    private readonly IUpdatePreferences _preferences;
    private readonly IInstallerLauncher _launcher;
    private readonly TimeProvider _clock;

    /// <summary>Hash of the file the last successful download produced, checked again before launch.</summary>
    private string? _verifiedHash;

    public UpdateService(
        HttpClient http,
        Version currentVersion,
        TimeProvider clock,
        IUpdatePreferences preferences,
        IInstallerLauncher launcher,
        string? updatesDirectory = null)
    {
        CurrentVersion = CurrentVersionWithDebugOverride(currentVersion);
        _checker = new UpdateChecker(http, CurrentVersion);
        _downloader = new UpdateDownloader(http, updatesDirectory);
        _clock = clock;
        _preferences = preferences;
        _launcher = launcher;
    }

    /// <summary>True when the automatic check should run at startup (toggle on, last check older than a day).</summary>
    public bool AutoCheckDue => _preferences.CheckForUpdatesAutomatically && !CheckedWithinThrottle();

    /// <summary>
    /// Runs a check. The automatic path is a no-op when the toggle is off or the
    /// last check is younger than 24 hours; the manual path always runs.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken cancellationToken = default)
    {
        if (!manual && !_preferences.CheckForUpdatesAutomatically)
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, null, null);
        if (!manual && CheckedWithinThrottle())
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, null, null);

        var result = await _checker.CheckAsync(cancellationToken);
        // Only a completed conversation with GitHub resets the throttle; a
        // failure or rate limit leaves it so the next start retries.
        if (result.Status is UpdateCheckStatus.UpToDate
            or UpdateCheckStatus.UpdateAvailable
            or UpdateCheckStatus.NoReleases)
        {
            _preferences.LastUpdateCheckUtc = _clock.GetUtcNow();
        }
        return result;
    }

    public async Task<UpdateDownloadResult> DownloadAsync(
        ReleaseInfo release,
        CancellationToken cancellationToken = default,
        IProgress<UpdateProgress>? progress = null)
    {
        var result = await _downloader.DownloadAsync(release, cancellationToken, progress);
        _verifiedHash = result is { Success: true, Path: { } path }
            ? await HashFileAsync(path, cancellationToken)
            : null;
        return result;
    }

    /// <summary>
    /// Re-reads the downloaded installer and compares it with the hash recorded
    /// after the download, then starts it. The second check closes the window
    /// between "verified" and "launched".
    /// </summary>
    public async Task<InstallOutcome> InstallAsync(
        UpdateDownloadResult download, CancellationToken cancellationToken = default)
    {
        if (!download.Success || download.Path is null || _verifiedHash is null)
            return InstallOutcome.Failed;

        string actual;
        try
        {
            actual = await HashFileAsync(download.Path, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return InstallOutcome.Failed;
        }
        if (!string.Equals(actual, _verifiedHash, StringComparison.OrdinalIgnoreCase))
            return InstallOutcome.Failed;

        return await _launcher.LaunchAsync(download.Path, cancellationToken);
    }

    private bool CheckedWithinThrottle()
    {
        var last = _preferences.LastUpdateCheckUtc;
        return last is not null && _clock.GetUtcNow() - last.Value < Throttle;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Debug builds honour <c>PERMADEL_UPDATE_TEST_VERSION</c> so the owner
    /// can exercise the UI against the real latest release. Release builds
    /// contain no trace of it.
    /// </summary>
    private static Version CurrentVersionWithDebugOverride(Version version)
    {
#if DEBUG
        var raw = Environment.GetEnvironmentVariable("PERMADEL_UPDATE_TEST_VERSION");
        if (!string.IsNullOrWhiteSpace(raw) && UpdateVersion.TryParsePlain(raw, out var parsed))
            return parsed;
#endif
        return version;
    }
}
