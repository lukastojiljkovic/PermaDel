using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Dispatching;
using PermaDel.Core.Updates;

namespace PermaDel.Services;

/// <summary>
/// The window's view of the update check: it owns the HTTP client and the
/// <see cref="UpdateService"/>, runs the startup check in the background so the
/// window is never delayed, and raises <see cref="Checked"/> on the UI thread.
/// </summary>
internal sealed class UpdateCoordinator
{
    // The installer download is far larger than any API call, so the client
    // itself never times out; each request that needs a deadline gets one.
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    public UpdateCoordinator() =>
        Service = new UpdateService(
            _http,
            AppVersion.Current,
            TimeProvider.System,
            AppSettings.UpdatePreferences,
            new InstallerLauncher());

    /// <summary>Raised on the UI thread after the automatic startup check.</summary>
    public event EventHandler<UpdateCheckResult>? Checked;

    public UpdateService Service { get; }

    public Version CurrentVersion => Service.CurrentVersion;

    /// <summary>
    /// The startup check. A no-op when the toggle is off or the last check is
    /// younger than a day; failures stay silent.
    /// </summary>
    public async Task CheckOnStartupAsync()
    {
        if (!Service.AutoCheckDue)
            return;

        var result = await Service.CheckAsync(manual: false).ConfigureAwait(false);
        _dispatcher.TryEnqueue(() => Checked?.Invoke(this, result));
    }
}

/// <summary>The running version: the informational version without build metadata.</summary>
internal static class AppVersion
{
    public static Version Current { get; } = Read();

    private static Version Read()
    {
        var raw = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (raw is not null)
        {
            var plus = raw.IndexOf('+');
            if (plus >= 0)
                raw = raw[..plus];
            if (Version.TryParse(raw, out var parsed))
                return parsed;
        }
        return typeof(AppVersion).Assembly.GetName().Version ?? new Version(1, 0, 0);
    }
}

/// <summary>
/// Starts the verified installer through the shell, so its administrator
/// manifest raises the UAC prompt itself. Declining UAC is reported as
/// <see cref="InstallOutcome.Cancelled"/>, not as a failure.
/// </summary>
internal sealed class InstallerLauncher : IInstallerLauncher
{
    public Task<InstallOutcome> LaunchAsync(string installerPath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            return Task.FromResult(process is null ? InstallOutcome.Failed : InstallOutcome.Started);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user declined the UAC prompt. The app stays open.
            return Task.FromResult(InstallOutcome.Cancelled);
        }
        catch (Win32Exception)
        {
            // 740 (ERROR_ELEVATION_REQUIRED) cannot happen with UseShellExecute;
            // any other shell failure means the installer could not be started.
            return Task.FromResult(InstallOutcome.Failed);
        }
    }
}
